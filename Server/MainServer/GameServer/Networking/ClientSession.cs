using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Channels;
using GameServer.Combat;
using GameServer.Items;
using GameServer.Maps;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // 접속 클라이언트 1개를 담당: TLS 핸드셰이크 + 프레임 수신 루프 + OpCode 디스패치 + 전송 대기열/전송 루프
    public class ClientSession
    {
        private readonly TcpClient _tcpClient;

        // 생성 시점에는 아직 평문 NetworkStream이다. RunAsync가 TLS 핸드셰이크에 성공하면 이 필드를
        // SslStream으로 교체한다(암/복호화는 SslStream이 내부적으로 처리하고, 이후 코드는 Stream API만 사용하므로
        // PacketFrame.ReadFrameAsync/SendLoopAsync 쪽은 손댈 필요가 없다).
        private Stream _stream;
        private readonly MapRoomRegistry _mapRooms;
        private readonly PlayerAuthValidator _authValidator;
        private readonly MainServerInternalApi _mainServerApi;
        private readonly KillRewardSaver _killRewardSaver;
        private readonly DisconnectedPlayerStateStore _disconnectedStates;
        private readonly SessionRegistry _sessions;
        private readonly X509Certificate2 _serverCertificate;

        // 다른 세션이 이 세션을 강제로 끊을 때(같은 캐릭터 중복 접속, Kick)나 전송이 실패/적체됐을 때 수신 루프를 멈추는 데 쓴다.
        private readonly CancellationTokenSource _kickCts = new();

        // 이 세션으로 보낼 프레임 대기열. Send는 여기에 넣기만 하고(누가 호출하든 즉시 반환), 실제 소켓 쓰기는 SendLoopAsync
        // 하나만 한다 - 예전에는 브로드캐스트가 세션마다 쓰기를 await해서, 느린 클라이언트 한 명이 방 전체 전송과 AI 틱을
        // 붙잡았고, 한 세션의 쓰기 예외가 브로드캐스트를 호출한 다른 플레이어의 요청 처리/AI 루프로 번졌다.
        // 가득 찰 만큼 못 받아 가는 클라이언트는 끊는다(AbortSlowClient) - 무한정 쌓아 두면 서버 메모리가 계속 는다.
        // 정상 트래픽(주변 플레이어 이동 초당 10회 x 인원 + 몬스터 이동)이 몇 초간 막혀도 견딜 만큼 넉넉히 잡는다.
        // 한 명의 도배로 이 대기열이 차지 않도록 방 전체로 퍼지는 요청(채팅/이동)은 세션별로 빈도를 제한한다(RequestRateLimiter).
        private const int MaxQueuedFrames = 8192;
        private readonly Channel<byte[]> _outgoing = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(MaxQueuedFrames)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        // 연결을 닫기 전에 대기열에 남은 프레임(강제 종료 사유, 입장 거부 사유 등)을 보내도록 기다리는 최대 시간.
        private static readonly TimeSpan FlushTimeoutOnClose = TimeSpan.FromSeconds(2);

        // TLS 핸드셰이크를 마쳐야 하는 시간과, 프레임 없이 기다려 주는 최대 시간(클라이언트 하트비트 10초의 세 배).
        // 클라이언트 GameServerConnectManager.heartbeatInterval을 이보다 길게 바꾸면 정상 연결도 끊긴다.
        private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

        // 대기열이 가득 차 끊긴 경우 1. 이때는 남은 프레임을 보내려고 기다리지 않고 바로 닫는다.
        private int _abortedSlowClient;

        // 전송 대기열을 닫았으면 1(CloseOutgoing). 이후 Send의 TryWrite 실패는 "가득 참"이 아니라 "종료 중"이다.
        private int _outgoingClosed;

        private Task? _sendLoop;

        // 이 세션이 Game_EnterRequest로 등록한 플레이어 id. 등록 전이면 null.
        private long? _playerId;

        // 현재 속한 맵의 GameRoom과 그 mapId. Game_EnterRequest 전이면 null.
        // Game_MapChangeRequest로 다른 맵으로 옮길 때 이 필드 자체를 교체한다(GameRoom은 수정하지 않음).
        private GameRoom? _room;
        private string? _mapId;

        public ClientSession(TcpClient tcpClient, MapRoomRegistry mapRooms, PlayerAuthValidator authValidator, MainServerInternalApi mainServerApi, KillRewardSaver killRewardSaver, DisconnectedPlayerStateStore disconnectedStates, SessionRegistry sessions, X509Certificate2 serverCertificate)
        {
            _tcpClient = tcpClient;
            _stream = tcpClient.GetStream();
            _mapRooms = mapRooms;
            _authValidator = authValidator;
            _mainServerApi = mainServerApi;
            _killRewardSaver = killRewardSaver;
            _disconnectedStates = disconnectedStates;
            _sessions = sessions;
            _serverCertificate = serverCertificate;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var endpoint = _tcpClient.Client.RemoteEndPoint;
            Console.WriteLine($"[GameServer] 클라이언트 접속: {endpoint}");

            // 서버 종료(ct) 또는 강제 종료(Kick/전송 실패) 중 먼저 오는 쪽으로 수신 루프와 요청 처리를 멈춘다.
            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _kickCts.Token);
            CancellationToken sessionCt = sessionCts.Token;

            // 핸드셰이크/수신 대기 시간 제한. 받을 때마다 다시 CancelAfter로 연장한다 - 이 시간 동안 아무 프레임도 오지 않으면
            // (클라이언트는 10초마다 하트비트를 보낸다) 끊긴 연결로 보고 정리한다. 예전에는 서버가 먼저 끊지 않아, 응답 없이
            // 반쯤 끊긴 연결이나 핸드셰이크만 걸어 두고 아무 것도 보내지 않는 연결이 서버 자원을 계속 차지했다.
            using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(sessionCt);

            // 마지막으로 처리하던 요청의 OpCode(오류 로그용).
            ushort lastOpCode = 0;

            try
            {
                // 프레임을 하나라도 주고받기 전에 TLS 핸드셰이크부터 마친다 - 이후 _stream을 쓰는 모든 코드
                // (PacketFrame.ReadFrameAsync/SendLoopAsync)는 암호화 여부를 몰라도 되도록 Stream 인터페이스만 본다.
                idleCts.CancelAfter(HandshakeTimeout);
                var sslStream = new SslStream(_stream, leaveInnerStreamOpen: false);
                await sslStream.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _serverCertificate,
                        ClientCertificateRequired = false
                    }, idleCts.Token);
                _stream = sslStream;

                // 전송 루프는 강제 종료 토큰이 아니라 서버 수명 토큰(ct)으로 돈다 - 강제 종료 시에도 대기열의 마지막
                // 프레임(종료 사유)을 보낸 뒤 끝나야 하기 때문이다. 끝내는 신호는 대기열 완료(finally)다.
                _sendLoop = SendLoopAsync(ct);

                while (!sessionCt.IsCancellationRequested)
                {
                    idleCts.CancelAfter(IdleTimeout);
                    var frame = await PacketFrame.ReadFrameAsync(_stream, idleCts.Token);
                    if (frame is null)
                        break;

                    lastOpCode = frame.Value.OpCode;
                    await DispatchAsync(frame.Value.OpCode, frame.Value.Body, sessionCt);
                }
            }
            catch (InvalidDataException ex)
            {
                // 프레임 길이가 규격을 벗어남(PacketFrame.ReadFrameAsync) - 손상/조작된 스트림이라 끊는다.
                Console.WriteLine($"[GameServer] 잘못된 프레임으로 연결 종료: {endpoint}, PlayerId={_playerId} - {ex.Message}");
            }
            catch (EndOfStreamException ex)
            {
                // 바디가 OpCode가 기대하는 형식보다 짧음(BinaryReader) - 잘못 만들어진/조작된 패킷이라 끊는다.
                Console.WriteLine($"[GameServer] 잘못된 패킷으로 연결 종료: {endpoint}, PlayerId={_playerId}, OpCode=0x{lastOpCode:X4} - {ex.Message}");
            }
            catch (OperationCanceledException) when (idleCts.IsCancellationRequested && !sessionCt.IsCancellationRequested)
            {
                Console.WriteLine($"[GameServer] 응답 없는 연결 종료(시간 초과): {endpoint}, PlayerId={_playerId}");
            }
            catch (OperationCanceledException)
            {
                // 서버 종료 또는 강제 종료(Kick/전송 실패)로 인한 정상 취소
            }
            catch (IOException)
            {
                // 클라이언트 비정상 종료 (연결 끊김)
            }
            catch (AuthenticationException ex)
            {
                // TLS 핸드셰이크 실패(프로토콜 불일치, 인증서 미신뢰 등) - 위조/스캐너 트래픽일 수 있으므로
                // 조용히 연결만 닫는다.
                Console.WriteLine($"[GameServer] TLS 핸드셰이크 실패: {endpoint} - {ex.Message}");
            }
            catch (EnterRejectedException)
            {
                // Game_EnterRequest 거부(인증 실패/알 수 없는 맵)로 HandleEnterRequestAsync가 의도적으로 연결을 종료한 경우.
            }
            catch (Exception ex)
            {
                // 그 밖의 예외(요청 처리 중 서버 버그, 예상 못 한 해석 오류 등). 예전에는 여기서 잡지 않아 세션 Task가 관찰되지 않은
                // 예외로 끝났다(GameTcpServer가 기다리지 않는 fire-and-forget) - 원인을 남기고 이 연결만 정리한다.
                Console.WriteLine($"[GameServer] 요청 처리 중 오류로 연결 종료: {endpoint}, PlayerId={_playerId}, OpCode=0x{lastOpCode:X4} - {ex}");
            }
            finally
            {
                if (_playerId is { } playerId)
                {
                    // 이 플레이어를 보던 사람들에게 퇴장을 알리는 것까지 Remove가 한다. 같은 캐릭터가 새 세션으로 다시 입장해
                    // 방의 항목이 이미 교체됐다면 Remove는 아무 것도 하지 않는다(새 세션이 그 캐릭터로 방에 있는 중이다 -
                    // 그 세션은 입장할 때 이 세션의 상태를 직접 넘겨받았으므로 보관하지 않는다).
                    // 실제로 빠졌으면 체력/위치를 보관해 다시 입장할 때 이어받게 한다. 등록 해제보다 먼저 보관해야, 해제 직후
                    // 들어온 새 입장이 보관된 상태를 찾을 수 있다.
                    PlayerStateSnapshot? lastState = CaptureState();
                    if (_room is { } room && room.Remove(playerId, this) && lastState is not null)
                    {
                        _disconnectedStates.Save(playerId, lastState);
                    }

                    _sessions.Unregister(playerId, this);
                }

                // 더 이상 보낼 것이 없으니 대기열을 닫고, 남은 프레임(강제 종료/입장 거부 사유)이 나갈 때까지 잠깐 기다린 뒤
                // 소켓을 닫는다. 느린 클라이언트로 끊는 경우는 기다리지 않는다. 소켓을 닫으면 진행 중이던 쓰기도 실패하며
                // 전송 루프가 끝난다.
                CloseOutgoing();
                if (_sendLoop is not null && Volatile.Read(ref _abortedSlowClient) == 0)
                {
                    await Task.WhenAny(_sendLoop, Task.Delay(FlushTimeoutOnClose));
                }

                Console.WriteLine($"[GameServer] 클라이언트 종료: {endpoint}");
                _tcpClient.Close();
            }
        }

        // 이 세션으로 프레임 하나를 보낸다. 대기열에 넣기만 하고 즉시 반환하므로 어느 스레드에서 몇 번을 호출해도
        // 호출측이 막히거나 이 세션의 전송 오류를 받지 않는다. 이미 닫힌 세션이면 조용히 버린다.
        public void Send(OpCode opCode, byte[] body)
        {
            if (_outgoing.Writer.TryWrite(PacketFrame.Encode((ushort)opCode, body)))
            {
                return;
            }

            // TryWrite는 대기열이 닫혔거나(종료 중) 가득 찼을 때 실패한다. 닫힌 경우는 버리면 되고, 가득 찬 경우는
            // 클라이언트가 받아 가는 속도가 보내는 속도를 따라오지 못하는 것이라 연결을 끊는다.
            // (Reader.Completion은 남은 프레임까지 다 보낸 뒤에야 완료되므로 "닫혔는지" 판단에 쓸 수 없다 - 별도 플래그를 본다.)
            if (Volatile.Read(ref _outgoingClosed) == 0)
            {
                AbortSlowClient();
            }
        }

        // 전송 대기열을 닫는다(더 이상 넣지 않음). 이미 들어 있는 프레임은 전송 루프가 마저 보낸다.
        private void CloseOutgoing()
        {
            Volatile.Write(ref _outgoingClosed, 1);
            _outgoing.Writer.TryComplete();
        }

        private void AbortSlowClient()
        {
            if (Interlocked.Exchange(ref _abortedSlowClient, 1) != 0)
            {
                return;
            }

            Console.WriteLine($"[GameServer] 전송 대기열 초과({MaxQueuedFrames}) - 느린 클라이언트 연결을 종료합니다 (PlayerId={_playerId}).");
            CloseOutgoing();

            // Send는 GameRoom이 시야 목록 락(_viewLock)을 잡고 순회하는 중에 호출될 수 있다. 취소 콜백은 Cancel을 호출한 스레드에서
            // 동기로 돌 수 있어, 그 자리에서 세션 정리(GameRoom.Remove -> 같은 락 재진입 -> 순회 중인 목록 수정)가 일어나지 않도록
            // 취소는 다른 스레드에서 한다.
            _ = Task.Run(() => _kickCts.Cancel());
        }

        // 대기열의 프레임을 순서대로 소켓에 쓴다(이 세션의 유일한 writer). 대기열이 닫히고(finally) 비면 끝난다.
        // 쓰기가 실패하면(연결 끊김) 수신 루프도 멈춰 세션 정리가 곧바로 진행되게 한다.
        private async Task SendLoopAsync(CancellationToken ct)
        {
            try
            {
                await foreach (byte[] frame in _outgoing.Reader.ReadAllAsync(ct))
                {
                    await _stream.WriteAsync(frame, ct);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // 서버 종료, 연결 끊김, 또는 finally에서 소켓을 닫은 경우.
            }
            finally
            {
                _kickCts.Cancel();
            }
        }

        private Task DispatchAsync(ushort opCode, byte[] body, CancellationToken ct)
        {
            switch ((OpCode)opCode)
            {
                case OpCode.System_Heartbeat:
                    Send(OpCode.System_Heartbeat, Array.Empty<byte>());
                    return Task.CompletedTask;
                case OpCode.Game_EnterRequest:
                    return HandleEnterRequestAsync(body, ct);
                case OpCode.Game_MoveRequest:
                    HandleMoveRequest(body);
                    return Task.CompletedTask;
                case OpCode.Game_ChatRequest:
                    HandleChatRequest(body);
                    return Task.CompletedTask;
                case OpCode.Game_AttackRequest:
                    HandleAttackRequest(body);
                    return Task.CompletedTask;
                case OpCode.Game_MapChangeRequest:
                    HandleMapChangeRequest(body);
                    return Task.CompletedTask;
                case OpCode.Game_MonsterAttackRequest:
                    HandleMonsterAttackRequest(body);
                    return Task.CompletedTask;
                case OpCode.Game_AttackAnimationRequest:
                    HandleAttackAnimationRequest(body);
                    return Task.CompletedTask;
                case OpCode.Game_StatUpdateRequest:
                    HandleStatUpdateRequest(body, ct);
                    return Task.CompletedTask;
                case OpCode.Game_UseItemRequest:
                    return HandleUseItemRequestAsync(body, ct);
                default:
                    Console.WriteLine($"[GameServer] 처리되지 않은 OpCode: 0x{opCode:X4}");
                    return Task.CompletedTask;
            }
        }

        // mapId에 해당하는 GameRoom(없으면 새로 생성)에 자신을 등록하고, 본인에게는 시야 안의 기존
        // 접속자/몬스터 목록(Game_EnterAck)을 보낸다. 주변 플레이어는 다음 방 틱에 "시야 진입"(Game_PlayerJoined)으로 받는다.
        // 등록 전에 AccessToken이 info.PlayerId(characterId)를 실제로 소유한 계정의 것인지 AuthServer에
        // 확인한다 - 그렇지 않으면 누구나 임의의 PlayerId를 자칭해 접속/조작할 수 있기 때문이다.
        private static readonly TimeSpan PendingRewardWaitTimeout = TimeSpan.FromSeconds(10);

        private async Task HandleEnterRequestAsync(byte[] body, CancellationToken ct)
        {
            // 한 세션은 한 번만 입장한다(이미 입장한 세션의 재요청은 무시).
            if (_playerId is not null)
            {
                return;
            }

            var request = C2SEnterRequest.Decode(body);
            var info = request.Player;

            // 이 캐릭터의 이전 세션에서 저장이 끝나지 않은 처치 보상이 있으면 먼저 기다린다 - 그 전에 DB를 읽으면 옛 level/exp로
            // 시작하고, 이후 이 세션의 처치 보상이 옛 값 기준의 최종 level/exp로 저장돼 진행도가 되돌아간다(KillRewardSaver 참고).
            if (!await _killRewardSaver.WaitForPendingAsync(info.PlayerId, PendingRewardWaitTimeout, ct))
            {
                RejectEnter($"Game_EnterRequest 이전 처치 보상 저장 대기 시간 초과 (PlayerId={info.PlayerId})",
                    "이전 플레이 기록을 저장하는 중입니다. 잠시 후 다시 접속해 주세요.");
            }

            CharacterSnapshot? snapshot = await _authValidator.FetchOwnedCharacterAsync(request.AccessToken, info.PlayerId, ct);
            if (snapshot is null)
            {
                RejectEnter($"Game_EnterRequest 인증 실패 (PlayerId={info.PlayerId})", "인증에 실패했습니다.");
                return;
            }

            // 맵 데이터(MapData/{mapId}.json)에 등록된 맵에만 입장할 수 있다. 그렇지 않으면 임의의 mapId 문자열마다
            // 새 GameRoom이 생겨(메모리/AI 루프 낭비), 서버가 좌표를 모르는 맵에서는 입장/부활/포탈 검증도 할 수 없다.
            if (!MapDataCatalog.TryGet(info.MapId, out MapData mapData) || mapData.RespawnPoint is not { } respawnPoint)
            {
                RejectEnter($"Game_EnterRequest 알 수 없는 맵 (PlayerId={info.PlayerId}, MapId={info.MapId})", "알 수 없는 맵입니다.");
                return;
            }

            _playerId = info.PlayerId;

            // info의 닉네임/외형/레벨/경험치/전투 스탯은 클라이언트가 채워 보낸 값이라 위조 가능하다(PlayerInfo.cs 주석 참고).
            // MainServer에서 방금 받아온 snapshot(DB 원본)으로 전부 덮어쓰고, 클라이언트 값은 위치/맵만 사용한다 -
            // 이후 이 값이 GameRoom에 저장되고, 경험치 계산(처치 보상)과 다른 접속자 브로드캐스트에 그대로 쓰인다.
            info.Nickname = snapshot.Nickname;
            info.HairIndex = snapshot.HairIndex;
            info.EyeIndex = snapshot.EyeIndex;
            info.MouthIndex = snapshot.MouthIndex;
            info.Level = snapshot.Level;
            info.Exp = snapshot.Exp;
            (info.AttackPower, info.Defense) = CombatStatCalculator.Calculate(snapshot);

            // 체력은 DB에 저장하지 않는다. 기본은 가득 찬 상태로 시작하고(클라이언트 HealthComponent.ApplyFromUserStats와 동일),
            // 최근에 끊긴 상태가 있으면 아래에서 이어받는다.
            info.MaxHp = CombatStatCalculator.CalculateMaxHp(snapshot);
            info.CurrentHp = info.MaxHp;

            // 입장 위치도 서버가 맵 데이터로 정한다(클라이언트도 같은 RespawnPoint에 스폰한다). 클라이언트 좌표를 그대로
            // 받으면 입장 순간에 원하는 곳으로 순간이동할 수 있다.
            info.X = respawnPoint.X;
            info.Y = respawnPoint.Y;
            info.Z = respawnPoint.Z;
            info.RotationY = respawnPoint.RotationY;

            // 같은 캐릭터로 이미 접속 중인 세션이 있으면 끊는다(새 접속이 우선). 이전 세션은 방 항목이 이 세션으로 교체된 뒤
            // 종료되더라도 GameRoom.Remove(playerId, session)가 자기 항목만 지우므로 새 세션에는 영향이 없다.
            // 끊기 전에 그 세션의 현재 상태를 넘겨받는다 - 다른 곳에서 다시 접속하는 것으로 체력을 회복할 수 없게 한다.
            PlayerStateSnapshot? previousState = null;
            if (_sessions.Register(info.PlayerId, this) is { } previousSession)
            {
                Console.WriteLine($"[GameServer] 중복 접속 (PlayerId={info.PlayerId}) - 이전 세션을 종료합니다.");
                previousState = previousSession.CaptureState();
                previousSession.Kick("다른 곳에서 같은 캐릭터로 접속하여 연결이 종료되었습니다.");
            }

            // 보관된 상태는 항상 꺼낸다(이전 세션에서 넘겨받았더라도 남은 옛 항목이 다음 입장에 쓰이지 않게).
            PlayerStateSnapshot? storedState = _disconnectedStates.Take(info.PlayerId);
            RestorePreviousState(info, previousState ?? storedState);

            GameRoom room = _mapRooms.GetOrCreate(info.MapId);
            _room = room;
            _mapId = info.MapId;

            var (visiblePlayers, visibleMonsters) = room.Join(info, this);

            var ack = new S2CEnterAck { Self = info, ExistingPlayers = visiblePlayers, ExistingMonsters = visibleMonsters };
            Send(OpCode.Game_EnterAck, ack.Encode());
        }

        // 이 세션의 캐릭터가 지금 방에 있으면 그 체력/맵/위치를 돌려준다(없으면 null). 이 세션이 끊길 때와, 같은 캐릭터로
        // 새로 접속한 세션이 이 세션을 밀어낼 때(다른 스레드) 호출된다.
        public PlayerStateSnapshot? CaptureState()
        {
            if (_playerId is not { } playerId || _room is not { } room || _mapId is not { } mapId
                || !room.TryGetInfo(playerId, out var info))
            {
                return null;
            }

            lock (info)
            {
                return new PlayerStateSnapshot(mapId, info.X, info.Y, info.Z, info.RotationY, info.CurrentHp);
            }
        }

        // 입장하는 캐릭터에 이전 상태(끊기기 전, 또는 밀어낸 세션의 현재 상태)를 이어받게 한다. info는 가득 찬 체력과
        // 요청한 맵의 부활 지점으로 채워진 상태로 들어온다.
        // - 사망한 채 끊겼으면 그대로 둔다: 부활 지점에서 가득 찬 체력으로 시작하는 것은 사망 후 자동 부활과 같은 결과다.
        // - 살아 있었으면 체력을 이어받는다(최대 체력을 넘지 않게). 같은 맵으로 들어오면 위치도 이어받는다 - 서버가 마지막으로
        //   인정한 위치라 순간이동이 되지 않는다. 다른 맵으로 들어오면(로비에서 새로 시작 등) 그 맵의 부활 지점에서 시작한다.
        private static void RestorePreviousState(PlayerInfo info, PlayerStateSnapshot? state)
        {
            if (state is null || state.CurrentHp <= 0)
            {
                return;
            }

            info.CurrentHp = Math.Min(state.CurrentHp, info.MaxHp);

            if (state.MapId == info.MapId)
            {
                info.X = state.X;
                info.Y = state.Y;
                info.Z = state.Z;
                info.RotationY = state.RotationY;
            }
        }

        // mapId 맵에서 targetMapId로 가는 MapSwap 포탈 중 player가 범위 안에 있는 것을 찾는다(없으면 null).
        private static MapPortal? FindMapSwapPortal(string mapId, string targetMapId, PlayerInfo player)
        {
            if (!MapDataCatalog.TryGet(mapId, out MapData mapData))
            {
                return null;
            }

            return mapData.Portals.FirstOrDefault(portal =>
                portal.Type == MapPortal.MapSwapType
                && portal.TargetMapId == targetMapId
                && portal.IsWithinRange(player.X, player.Z));
        }

        // 같은 맵 안 좌표 이동 포탈을 탄 이동인지 확인한다: 서버가 마지막으로 인정한 위치가 CoordinateTeleport 포탈 범위 안이고,
        // 요청한 위치가 그 포탈의 목적지 근처면 속도 검증 대상이 아닌 정상 순간이동으로 본다.
        private const float TeleportDestinationTolerance = 2f;

        private static bool IsCoordinateTeleport(string mapId, PlayerInfo lastAccepted, float x, float z)
        {
            if (!MapDataCatalog.TryGet(mapId, out MapData mapData))
            {
                return false;
            }

            foreach (MapPortal portal in mapData.Portals)
            {
                if (portal.Type != MapPortal.CoordinateTeleportType || portal.Destination is not { } destination
                    || !portal.IsWithinRange(lastAccepted.X, lastAccepted.Z))
                {
                    continue;
                }

                float dx = x - destination.X;
                float dz = z - destination.Z;
                if (dx * dx + dz * dz <= TeleportDestinationTolerance * TeleportDestinationTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        // 다른 세션이 이 세션을 강제로 끊는다(같은 캐릭터 중복 접속). 사유(System_Kicked)를 대기열에 넣고 대기열을 닫은 뒤
        // 수신 루프를 취소한다 - 전송 루프는 사유까지 보내고 끝나며, 방/레지스트리 정리와 소켓 종료는 RunAsync의 finally가 맡는다.
        // 호출측(새 세션의 입장 처리)을 기다리게 하지 않는다.
        public void Kick(string reason)
        {
            Send(OpCode.System_Kicked, Encoding.UTF8.GetBytes(reason));
            CloseOutgoing();
            _kickCts.Cancel();
        }

        // 입장 거부: 사유를 System_Error로 알리고 연결을 끊는다. 사유는 RunAsync의 finally가 소켓을 닫기 전에 전송 대기열을
        // 비우면서 나간다. 소켓을 여기서 직접 닫지 않는 건 수신 루프가 닫힌 스트림을 읽다 예외를 내지 않게 하기 위함이다.
        [DoesNotReturn]
        private void RejectEnter(string logMessage, string clientMessage)
        {
            Console.WriteLine($"[GameServer] {logMessage} - 연결을 종료합니다.");
            Send(OpCode.System_Error, Encoding.UTF8.GetBytes(clientMessage));
            throw new EnterRejectedException();
        }

        // 같은 접속을 유지한 채 다른 맵으로 옮긴다: 이전 맵 방에서 빠지며(보던 사람들에게 Game_PlayerLeft) 새 맵 방에 들어가
        // 시야 안의 기존 접속자/몬스터 목록(Game_MapChangeAck)을 받는다. 새 맵의 주변 플레이어는 다음 틱에 "시야 진입"으로 받는다.
        private void HandleMapChangeRequest(byte[] body)
        {
            var request = C2SMapChangeRequest.Decode(body);

            if (_playerId is not { } playerId || request.PlayerId != playerId
                || _room is not { } previousRoom || _mapId is not { } previousMapId)
            {
                return;
            }

            // 사망 중에는 맵을 옮기지 않는다 - 부활 예약(GameRoom.ReviveAfterDelayAsync)이 사망한 방에 걸려 있어,
            // 다른 방으로 옮겨 가면 부활 알림이 새 방에 전달되지 않는다. 클라이언트도 사망 중에는 포탈을 막는다.
            if (!previousRoom.TryGetInfo(playerId, out var info) || info.CurrentHp <= 0)
            {
                return;
            }

            // 현재 맵에서 요청한 맵으로 가는 MapSwap 포탈 근처에 있을 때만 옮겨 준다. 도착 위치도 클라이언트가 보낸 좌표가 아니라
            // 맵 데이터의 진입 지점으로 정한다 - 그렇지 않으면 아무 맵의 아무 좌표로나 순간이동할 수 있다.
            MapPortal? portal = FindMapSwapPortal(previousMapId, request.MapId, info);
            if (portal?.Destination is not { } destination || !MapDataCatalog.TryGet(request.MapId, out _))
            {
                Console.WriteLine($"[GameServer] 맵 이동 거부 (PlayerId={playerId}) : {previousMapId} -> {request.MapId}, 위치=({info.X:F1},{info.Z:F1})");
                // 클라이언트는 이미 새 맵을 로드했으므로 조용히 무시하면 서버와 맵이 어긋난 채로 남는다 - 사유를 알린다.
                // 정상 클라이언트는 같은 맵 데이터로 포탈을 타므로 여기에 오지 않는다(맵 데이터를 다시 내보내지 않은 경우 제외).
                Send(OpCode.System_Error, Encoding.UTF8.GetBytes("맵 이동이 거부되었습니다."));
                return;
            }

            // 같은 캐릭터의 새 세션이 방 항목을 이미 차지했다면(이 세션은 곧 끊긴다) 옮기지 않는다.
            if (!previousRoom.Remove(playerId, this))
            {
                return;
            }

            // HP 등 나머지 전투 스탯은 PlayerInfo 인스턴스를 그대로 재사용해 유지하고, 위치/맵만 갱신한다.
            info.MapId = request.MapId;
            info.X = destination.X;
            info.Y = destination.Y;
            info.Z = destination.Z;
            info.RotationY = destination.RotationY;

            GameRoom nextRoom = _mapRooms.GetOrCreate(request.MapId);
            var (visiblePlayers, visibleMonsters) = nextRoom.Join(info, this);

            _room = nextRoom;
            _mapId = request.MapId;

            var ack = new S2CEnterAck { Self = info, ExistingPlayers = visiblePlayers, ExistingMonsters = visibleMonsters };
            Send(OpCode.Game_MapChangeAck, ack.Encode());
        }

        // 이동 속도 검증 - "이동 거리 예산" 방식. 예산은 초당 MoveBudgetRefillPerSecond(m)씩 차고 최대 MoveBudgetCapacity(m)까지
        // 쌓이며, 이동 요청마다 직전 인정 위치로부터의 수평 이동 거리만큼 쓴다. 클라이언트 PlayerMoveController 기본값
        // (걷기 5m/s, 대시 0.25초에 3m + 쿨다운 1초 → 지속 최대 약 8m/s)보다 여유 있게 잡아 정상 이동과 네트워크
        // 지연으로 몰려 들어온 패킷은 통과시키고, 스피드핵/순간이동만 거부한다. 이동 속도 버프 등이 생기면 함께 조정해야 한다.
        // 높이(Y)는 서버에 지형 정보가 없어 검증하지 않는다(알려진 한계).
        private const float MoveBudgetRefillPerSecond = 9f;
        private const float MoveBudgetCapacity = 5f;
        private float _moveBudget = MoveBudgetCapacity;
        private DateTime _lastMoveBudgetRefillAtUtc = DateTime.UtcNow;

        // 거부된 이동이 연달아 오면(보정 패킷이 도착하기 전 이미 보낸 이동들) 보정 패킷을 매번 보내지 않도록 제한한다.
        private static readonly TimeSpan PositionCorrectionInterval = TimeSpan.FromMilliseconds(500);
        private DateTime _lastPositionCorrectionAtUtc = DateTime.MinValue;

        // 룸의 위치를 갱신한다(다른 접속자에게는 방 틱의 스냅샷으로 전달된다).
        // request.PlayerId가 이 세션의 실제 플레이어와 같은지 검증한다 - 그렇지 않으면 다른 플레이어의
        // ID를 실어 보내는 것만으로 그 플레이어를 임의의 위치로 옮길 수 있다(다른 핸들러들과 동일한 검증).
        // 클라이언트 PlayerNetworkSender는 0.1초마다(초당 10회) 보낸다. 그보다 여유 있게 허용하고, 초과분은 브로드캐스트 없이 버린다 -
        // 이동은 방 전체로 퍼지므로 거리 0짜리 이동을 도배해도 다른 세션들의 전송 대기열이 찬다.
        private readonly RequestRateLimiter _moveRateLimiter = new(capacity: 20, refillPerSecond: 15);

        private void HandleMoveRequest(byte[] body)
        {
            if (!_moveRateLimiter.TryAcquire())
            {
                return;
            }

            var request = C2SMoveRequest.Decode(body);

            if (_playerId is not { } playerId || request.PlayerId != playerId || _room is not { } room
                || !room.TryGetInfo(playerId, out var info))
            {
                return;
            }

            // 사망 중에는 움직일 수 없다(클라이언트도 조작을 막는다). 부활 위치는 서버가 정한다(GameRoom.ReviveAfterDelayAsync).
            if (info.CurrentHp <= 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            float elapsedSeconds = (float)(now - _lastMoveBudgetRefillAtUtc).TotalSeconds;
            _lastMoveBudgetRefillAtUtc = now;
            _moveBudget = Math.Min(MoveBudgetCapacity, _moveBudget + elapsedSeconds * MoveBudgetRefillPerSecond);

            float dx = request.X - info.X;
            float dz = request.Z - info.Z;
            float horizontalDistance = MathF.Sqrt(dx * dx + dz * dz);

            // 같은 맵 안 좌표 이동 포탈은 예산과 무관하게 허용한다(예산도 쓰지 않는다).
            if (horizontalDistance > _moveBudget && _mapId is { } mapId && IsCoordinateTeleport(mapId, info, request.X, request.Z))
            {
                horizontalDistance = 0f;
            }

            if (horizontalDistance > _moveBudget)
            {
                Console.WriteLine($"[GameServer] 이동 거부 (PlayerId={playerId}) : {horizontalDistance:F2}m > 허용 {_moveBudget:F2}m");
                SendPositionCorrection(info, now);
                return;
            }

            // 위치만 갱신한다. 다른 접속자에게는 방 틱이 다음 스냅샷(S2CWorldSnapshot)에 모아서 보낸다.
            _moveBudget -= horizontalDistance;
            room.UpdatePosition(request.PlayerId, request.X, request.Y, request.Z, request.RotationY);
        }

        // 거부된 이동을 되돌리도록 본인에게 서버가 마지막으로 인정한 위치를 보낸다(PositionCorrectionInterval로 제한).
        private void SendPositionCorrection(PlayerInfo info, DateTime now)
        {
            if (now - _lastPositionCorrectionAtUtc < PositionCorrectionInterval)
            {
                return;
            }
            _lastPositionCorrectionAtUtc = now;

            var correction = new S2CPositionCorrection { X = info.X, Y = info.Y, Z = info.Z, RotationY = info.RotationY };
            Send(OpCode.Game_PositionCorrection, correction.Encode());
        }

        // Move와 달리 발신자 본인 화면에도 같은 메시지가 떠야 하므로 BroadcastToAll을 쓴다.
        // 닉네임은 클라이언트를 신뢰하지 않고 룸에 등록된(Game_EnterRequest 시점) 값을 서버가 직접 채운다.
        private const int MaxChatMessageLength = 200;

        // 채팅 도배 제한: 연달아 5개까지, 이후 초당 1개. 초과분은 조용히 버린다(방 전체로 퍼지는 요청이라 대기열 보호 목적도 있다).
        private readonly RequestRateLimiter _chatRateLimiter = new(capacity: 5, refillPerSecond: 1);

        private void HandleChatRequest(byte[] body)
        {
            if (!_chatRateLimiter.TryAcquire())
            {
                return;
            }

            var request = C2SChatRequest.Decode(body);

            if (_playerId is not { } playerId || request.PlayerId != playerId || _room is not { } room)
            {
                return;
            }

            var message = request.Message?.Trim() ?? string.Empty;
            if (message.Length == 0)
            {
                return;
            }

            if (message.Length > MaxChatMessageLength)
            {
                message = message[..MaxChatMessageLength];
            }

            var nickname = room.TryGetInfo(playerId, out var info) ? info.Nickname : string.Empty;

            var broadcast = new S2CChatBroadcast
            {
                PlayerId = playerId,
                Nickname = nickname,
                Message = message,
                Timestamp = request.Timestamp
            };

            room.BroadcastToAll(OpCode.Game_ChatBroadcast, broadcast.Encode());
        }

        // 여기서는 위조된 공격자 신원 차단, 최소 공격 간격, 사거리만 검증한다. 피해 계산(방어력 적용)과
        // 대상 HP 갱신/사망 처리는 GameRoom.ApplyPlayerAttack이 서버 권위로 수행한다.
        // 클라이언트 PlayerAttackController.comboInputGuard(150ms)가 지나면 2타 콤보 입력을 즉시 받아들여
        // 두 번째 Game_AttackRequest/Game_MonsterAttackRequest를 보낸다. 이 값이 그보다 크면(과거 300ms)
        // 정상적인 콤보 2타 요청까지 여기서 조용히 드롭되어 "애니메이션은 2콤보, 데미지는 1타"만 반영되는
        // 문제가 생기므로, comboInputGuard보다 여유를 두고 짧게 잡아 정상 콤보는 통과시키고 그보다
        // 빠른(매크로 등) 연타만 차단한다. comboInputGuard를 바꾸면 이 값도 함께 맞춰야 한다.
        private const double MinAttackIntervalMs = 100;
        private const float MaxAttackRangeSquared = 5f * 5f;
        private DateTime _lastAttackAtUtc = DateTime.MinValue;

        private void HandleAttackRequest(byte[] body)
        {
            var request = C2SAttackRequest.Decode(body);

            if (_playerId is not { } playerId || request.AttackerId != playerId || request.TargetId == playerId
                || _room is not { } room)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastAttackAtUtc).TotalMilliseconds < MinAttackIntervalMs)
            {
                return;
            }
            _lastAttackAtUtc = now;

            if (!room.TryGetInfo(playerId, out var attacker) || !room.TryGetInfo(request.TargetId, out var target))
            {
                return;
            }

            float dx = attacker.X - target.X;
            float dy = attacker.Y - target.Y;
            float dz = attacker.Z - target.Z;
            if (dx * dx + dy * dy + dz * dz > MaxAttackRangeSquared)
            {
                return;
            }

            room.ApplyPlayerAttack(playerId, request.TargetId, request.Timestamp);
        }

        // 데미지/쿨다운 판정 없이 그대로 중계만 한다. Game_AttackRequest와 같은 쿨다운(_lastAttackAtUtc)을
        // 적용하면 안 된다 - 이 요청은 대상이 없는 허공 스윙을 포함해 콤보 타수마다 항상 오므로, 공유 쿨다운을
        // 적용하면 실제 로컬 콤보 타이밍과 어긋난다. 순전히 연출용이라 위조돼도 다른 플레이어 화면에 잘못된
        // 모션이 보이는 것 이상의 피해가 없다.
        private void HandleAttackAnimationRequest(byte[] body)
        {
            var request = C2SAttackAnimationRequest.Decode(body);

            if (_playerId is not { } playerId || request.AttackerId != playerId || _room is not { } room)
            {
                return;
            }

            room.BroadcastAttackAnimation(playerId, request.ComboStage, request.WeaponType);
        }

        // 플레이어 공격(HandleAttackRequest)과 같은 쿨다운(_lastAttackAtUtc)을 공유한다 - 그렇지 않으면
        // 플레이어 공격과 몬스터 공격 요청을 번갈아 보내 최소 공격 간격 제한을 우회할 수 있다.
        // 데미지 계산 자체(공격력-방어력)는 GameRoom.ApplyMonsterAttack이 서버 권위로 수행한다.
        // 사망한 플레이어는 몬스터를 공격할 수 없다(PvP는 GameRoom.ApplyPlayerAttack이 같은 검사를 한다).
        private void HandleMonsterAttackRequest(byte[] body)
        {
            var request = C2SMonsterAttackRequest.Decode(body);

            if (_playerId is not { } playerId || request.AttackerId != playerId || _room is not { } room)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastAttackAtUtc).TotalMilliseconds < MinAttackIntervalMs)
            {
                return;
            }
            _lastAttackAtUtc = now;

            if (!room.TryGetInfo(playerId, out var attacker) || attacker.CurrentHp <= 0
                || !room.TryGetMonsterPosition(request.MonsterId, out var monsterPosition))
            {
                return;
            }

            float dx = attacker.X - monsterPosition.X;
            float dy = attacker.Y - monsterPosition.Y;
            float dz = attacker.Z - monsterPosition.Z;
            if (dx * dx + dy * dy + dz * dz > MaxAttackRangeSquared)
            {
                return;
            }

            MonsterAttackResult result = room.ApplyMonsterAttack(request.MonsterId, playerId, attacker.AttackPower, request.Timestamp);

            // 방 전체가 아니라 처치자 본인에게만 보낸다 - 다른 접속자는 이 몬스터를 잡은 게 아니므로 경험치와 무관하다.
            if (result is { MonsterDied: true, GainedExp: { } gainedExp })
            {
                var expGain = new S2CExpGainBroadcast
                {
                    MonsterId = request.MonsterId,
                    GainedExp = gainedExp,
                    TotalExp = attacker.Exp,
                    Level = result.NewLevel,
                    DidLevelUp = result.DidLevelUp,
                    ExpToNextLevel = result.ExpToNextLevel
                };
                Send(OpCode.Game_ExpGainBroadcast, expGain.Encode());
            }

            // 골드/아이템도 처치자 본인에게만 보낸다. 만렙이라 GainedExp가 없는 경우에도 드롭은 지급되므로
            // 위 exp 분기와 독립적으로 판단한다(둘 다 0/빈 목록이면 굳이 빈 패킷을 보내지 않는다).
            if (result.MonsterDied && (result.GainedGold > 0 || result.DroppedItems is { Count: > 0 }))
            {
                var loot = new S2CLootBroadcast
                {
                    MonsterId = request.MonsterId,
                    GoldGained = result.GainedGold,
                    Items = result.DroppedItems?.ToList() ?? new List<(string, int)>()
                };
                Send(OpCode.Game_LootBroadcast, loot.Encode());
            }

            // 위 두 패킷은 클라이언트 화면 표시용일 뿐이고, DB 저장은 GameServer가 MainServer 서버 간 API로 직접 한다 -
            // 클라이언트가 저장을 대신 요청하던 방식은 보상 값을 위조할 수 있었다(MainServerInternalApi 참고).
            // 저장은 기다리지 않고 KillRewardSaver에 맡긴다. 캐릭터별로 처치 순서대로 저장하고 실패하면 다시 보내며,
            // 이 세션이 끊겨도 계속 진행한다 - 최종값인 level/exp가 이전 값으로 덮어써지지 않는다.
            bool hasReward = result.GainedExp is not null || result.GainedGold > 0 || result.DroppedItems is { Count: > 0 };
            if (result.MonsterDied && hasReward)
            {
                _killRewardSaver.Enqueue(
                    playerId,
                    attacker.Level,
                    attacker.Exp,
                    result.GainedGold,
                    result.DroppedItems ?? Array.Empty<(string ItemId, int Qty)>());
            }
        }

        // 스탯 재조회(MainServer 호출) 최소 간격. 장비를 연달아 바꾸면 요청이 몰리는데, 그만큼 MainServer를 호출하지 않고
        // 하나로 합친다(아래 RunStatRefreshLoopAsync).
        private static readonly TimeSpan MinStatRefreshInterval = TimeSpan.FromSeconds(1);

        // 재조회 루프가 돌고 있으면 1. 루프 밖(수신 루프)과 루프 안(백그라운드)에서 함께 읽고 쓰므로 Interlocked로 다룬다.
        private int _statRefreshRunning;

        // 마지막 재조회 이후 새 요청이 들어왔으면 1.
        private int _statRefreshPending;

        // 인벤토리에서 장비를 장착/해제해 공격력/방어력이 바뀌었을 때 클라이언트가 보낸다. request.AttackPower/
        // Defense(클라이언트 자기 계산값)는 신뢰하지 않고 트리거로만 쓴다 - Game_EnterRequest 때와 동일하게
        // MainServer에서 str/agi/장착 아이템을 다시 조회해 서버가 직접 재계산한다(CombatStatCalculator).
        // 브로드캐스트는 필요 없어(GameRoom.TryUpdateCombatStats 주석 참고) 응답 없이 서버 캐시만 갱신한다.
        // 요청마다 바로 조회하지 않고 "갱신 필요" 표시만 한 뒤, 재조회 루프가 최소 간격을 지키며 한 번에 처리한다 - 요청을 그냥
        // 버리면 마지막 장비 상태가 반영되지 않을 수 있어서, 버리는 대신 합친다(마지막 상태는 항상 반영된다).
        private void HandleStatUpdateRequest(byte[] body, CancellationToken ct)
        {
            var request = C2SStatUpdateRequest.Decode(body);

            if (_playerId is not { } playerId || request.PlayerId != playerId)
            {
                return;
            }

            Volatile.Write(ref _statRefreshPending, 1);
            if (Interlocked.CompareExchange(ref _statRefreshRunning, 1, 0) == 0)
            {
                _ = RunStatRefreshLoopAsync(playerId, ct);
            }
        }

        // 수신 루프를 막지 않도록 백그라운드에서 돈다. 사용자 AccessToken(30분 만료)이 아니라 서버 간 API로 조회하므로
        // 오래 접속해 있어도 장비 변경이 계속 반영된다.
        private async Task RunStatRefreshLoopAsync(long playerId, CancellationToken ct)
        {
            try
            {
                while (Interlocked.Exchange(ref _statRefreshPending, 0) == 1)
                {
                    CharacterSnapshot? snapshot = await _mainServerApi.FetchCharacterAsync(playerId, ct);
                    if (snapshot is null)
                    {
                        // MainServer 순단 등으로 조회에 실패한 경우 - 이전에 검증된 값을 그대로 유지하고 이번 갱신만 건너뛴다.
                        Console.WriteLine($"[GameServer] Game_StatUpdateRequest 스탯 재조회 실패 (PlayerId={playerId}) - 이전 값을 유지합니다.");
                    }
                    else if (_room is { } room)
                    {
                        (int attackPower, int defense) = CombatStatCalculator.Calculate(snapshot);
                        room.TryUpdateCombatStats(playerId, attackPower, defense);
                    }

                    await Task.Delay(MinStatRefreshInterval, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // 세션 종료.
            }
            finally
            {
                Volatile.Write(ref _statRefreshRunning, 0);

                // 루프가 "대기 요청 없음"을 확인한 직후 새 요청이 들어오면, 그 요청은 루프가 돌고 있다고 보고 새로 시작하지 않았다 -
                // 여기서 한 번 더 확인해 놓치지 않게 한다.
                if (!ct.IsCancellationRequested && Volatile.Read(ref _statRefreshPending) == 1
                    && Interlocked.CompareExchange(ref _statRefreshRunning, 1, 0) == 0)
                {
                    _ = RunStatRefreshLoopAsync(playerId, ct);
                }
            }
        }

        // 물약 연타로 MainServer 차감 요청이 몰리지 않게 하는 최소 사용 간격.
        private const double MinUseItemIntervalMs = 300;
        private DateTime _lastUseItemAtUtc = DateTime.MinValue;

        // 소비 아이템(현재는 회복 물약) 사용. 예전에는 클라이언트가 로컬에서 회복하고 MainServer에 차감만 따로 요청해
        // 서버 HP에는 회복이 반영되지 않았다. 이제 서버가 효과 여부를 확인 -> MainServer에서 1개 차감 -> 회복 순서로
        // 처리하고, 결과를 요청자에게(Game_UseItemResult), 바뀐 체력을 방 전체에(Game_PlayerHpBroadcast) 알린다.
        private async Task HandleUseItemRequestAsync(byte[] body, CancellationToken ct)
        {
            var request = C2SUseItemRequest.Decode(body);

            if (_playerId is not { } playerId || request.PlayerId != playerId || _room is not { } room)
            {
                return;
            }

            bool success = await TryUseItemAsync(playerId, room, request.ItemId, ct);

            var result = new S2CUseItemResult { ItemId = request.ItemId, Success = success };
            Send(OpCode.Game_UseItemResult, result.Encode());
        }

        private async Task<bool> TryUseItemAsync(long playerId, GameRoom room, string itemId, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastUseItemAtUtc).TotalMilliseconds < MinUseItemIntervalMs)
            {
                return false;
            }
            _lastUseItemAtUtc = now;

            // 회복 아이템이 아니거나, 써도 효과가 없는 상태(사망/만피)면 차감하지 않는다.
            if (!ItemCatalog.TryGet(itemId, out ItemDefinition definition) || definition.HealPercent <= 0
                || !room.CanBeHealed(playerId))
            {
                return false;
            }

            if (!await _mainServerApi.ConsumeItemAsync(playerId, itemId, ct))
            {
                return false;
            }

            // 차감과 회복 사이에 사망/만피가 되면 아이템만 소모된다 - 두 호출 사이(MainServer 왕복 동안)의 짧은 틈이라
            // 드물고, 반대로 회복 먼저 하면 차감 실패 시 공짜 회복이 되므로 차감을 먼저 한다.
            return room.TryHealPlayer(playerId, definition.HealPercent);
        }

        // Game_EnterRequest 거부(인증 실패/알 수 없는 맵)를 RunAsync의 루프 종료 신호로 쓰기 위한 내부 전용 예외.
        private sealed class EnterRejectedException : Exception
        {
        }
    }
}
