using GameServer.Logging;
using Microsoft.Extensions.Logging;
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
    public partial class ClientSession : ISessionSender
    {
        private static readonly ILogger Log = GameLog.For<ClientSession>();

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
            Log.LogInformation("클라이언트 접속: {Endpoint}", endpoint);

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
                Log.LogWarning("잘못된 프레임으로 연결 종료: {Endpoint}, PlayerId={PlayerId} - {Reason}", endpoint, _playerId, ex.Message);
            }
            catch (EndOfStreamException ex)
            {
                // 바디가 OpCode가 기대하는 형식보다 짧음(BinaryReader) - 잘못 만들어진/조작된 패킷이라 끊는다.
                Log.LogWarning("잘못된 패킷으로 연결 종료: {Endpoint}, PlayerId={PlayerId}, OpCode=0x{OpCode:X4} - {Reason}", endpoint, _playerId, lastOpCode, ex.Message);
            }
            catch (OperationCanceledException) when (idleCts.IsCancellationRequested && !sessionCt.IsCancellationRequested)
            {
                Log.LogWarning("응답 없는 연결 종료(시간 초과): {Endpoint}, PlayerId={PlayerId}", endpoint, _playerId);
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
                Log.LogWarning("TLS 핸드셰이크 실패: {Endpoint} - {Reason}", endpoint, ex.Message);
            }
            catch (EnterRejectedException)
            {
                // Game_EnterRequest 거부(인증 실패/알 수 없는 맵)로 HandleEnterRequestAsync가 의도적으로 연결을 종료한 경우.
            }
            catch (Exception ex)
            {
                // 그 밖의 예외(요청 처리 중 서버 버그, 예상 못 한 해석 오류 등). 예전에는 여기서 잡지 않아 세션 Task가 관찰되지 않은
                // 예외로 끝났다(GameTcpServer가 기다리지 않는 fire-and-forget) - 원인을 남기고 이 연결만 정리한다.
                Log.LogError(ex, "요청 처리 중 오류로 연결 종료: {Endpoint}, PlayerId={PlayerId}, OpCode=0x{OpCode:X4}", endpoint, _playerId, lastOpCode);
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

                Log.LogInformation("클라이언트 종료: {Endpoint}", endpoint);
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

            Log.LogWarning("전송 대기열 초과({MaxQueuedFrames}) - 느린 클라이언트 연결을 종료합니다 (PlayerId={PlayerId}).", MaxQueuedFrames, _playerId);
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
                case OpCode.Game_ChestOpenRequest:
                    HandleChestOpenRequest(body);
                    return Task.CompletedTask;
                case OpCode.Game_DashRequest:
                    HandleDashRequest(body);
                    return Task.CompletedTask;
                case OpCode.Game_StatUpdateRequest:
                    HandleStatUpdateRequest(body, ct);
                    return Task.CompletedTask;
                case OpCode.Game_UseItemRequest:
                    return HandleUseItemRequestAsync(body, ct);
                default:
                    Log.LogWarning("처리되지 않은 OpCode: 0x{OpCode:X4}", opCode);
                    return Task.CompletedTask;
            }
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
                return new PlayerStateSnapshot(mapId, info.X, info.Y, info.Z, info.RotationY, info.CurrentHp, info.CurrentMp);
            }
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

    }
}
