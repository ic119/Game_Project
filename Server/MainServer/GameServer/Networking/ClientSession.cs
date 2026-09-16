using System.Net.Sockets;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // 접속 클라이언트 1개를 담당: 프레임 수신 루프 + OpCode 디스패치
    public class ClientSession
    {
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _stream;
        private readonly MapRoomRegistry _mapRooms;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        // 이 세션이 Game_EnterRequest로 등록한 플레이어 id. 등록 전이면 null.
        private long? _playerId;

        // 현재 속한 맵의 GameRoom과 그 mapId. Game_EnterRequest 전이면 null.
        // Game_MapChangeRequest로 다른 맵으로 옮길 때 이 필드 자체를 교체한다(GameRoom은 수정하지 않음).
        private GameRoom? _room;
        private string? _mapId;

        public ClientSession(TcpClient tcpClient, MapRoomRegistry mapRooms)
        {
            _tcpClient = tcpClient;
            _stream = tcpClient.GetStream();
            _mapRooms = mapRooms;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var endpoint = _tcpClient.Client.RemoteEndPoint;
            Console.WriteLine($"[GameServer] 클라이언트 접속: {endpoint}");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var frame = await PacketFrame.ReadFrameAsync(_stream, ct);
                    if (frame is null)
                        break;

                    await DispatchAsync(frame.Value.OpCode, frame.Value.Body, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // 서버 종료로 인한 정상 취소
            }
            catch (IOException)
            {
                // 클라이언트 비정상 종료 (연결 끊김)
            }
            finally
            {
                if (_playerId is { } playerId && _room is { } room && _mapId is { } mapId)
                {
                    room.Remove(playerId);
                    var left = new S2CPlayerLeft { PlayerId = playerId };
                    await room.BroadcastAsync(OpCode.Game_PlayerLeft, left.Encode(), playerId, ct);
                    _mapRooms.RemoveIfEmpty(mapId, room);
                }

                Console.WriteLine($"[GameServer] 클라이언트 종료: {endpoint}");
                _tcpClient.Close();
            }
        }

        private Task DispatchAsync(ushort opCode, byte[] body, CancellationToken ct)
        {
            return (OpCode)opCode switch
            {
                OpCode.System_Heartbeat => SendAsync(OpCode.System_Heartbeat, Array.Empty<byte>(), ct),
                OpCode.Game_EnterRequest => HandleEnterRequestAsync(body, ct),
                OpCode.Game_MoveRequest => HandleMoveRequestAsync(body, ct),
                OpCode.Game_ChatRequest => HandleChatRequestAsync(body, ct),
                OpCode.Game_AttackRequest => HandleAttackRequestAsync(body, ct),
                OpCode.Game_MapChangeRequest => HandleMapChangeRequestAsync(body, ct),
                _ => LogUnhandledAsync(opCode)
            };
        }

        private Task LogUnhandledAsync(ushort opCode)
        {
            Console.WriteLine($"[GameServer] 처리되지 않은 OpCode: 0x{opCode:X4}");
            return Task.CompletedTask;
        }

        // mapId에 해당하는 GameRoom(없으면 새로 생성)에 자신을 등록하고, 본인에게는 그 방의 기존
        // 접속자 목록(Game_EnterAck)을, 나머지에게는 자신의 입장(Game_PlayerJoined)을 알린다.
        private async Task HandleEnterRequestAsync(byte[] body, CancellationToken ct)
        {
            var info = PlayerInfo.Decode(body);
            _playerId = info.PlayerId;

            GameRoom room = _mapRooms.GetOrCreate(info.MapId);
            _room = room;
            _mapId = info.MapId;

            var existingPlayers = room.SnapshotExcluding(info.PlayerId);
            room.Add(info, this);

            var ack = new S2CEnterAck { ExistingPlayers = existingPlayers };
            await SendAsync(OpCode.Game_EnterAck, ack.Encode(), ct);

            var joined = new S2CPlayerJoined { Player = info };
            await room.BroadcastAsync(OpCode.Game_PlayerJoined, joined.Encode(), info.PlayerId, ct);
        }

        // 같은 접속을 유지한 채 다른 맵으로 옮긴다: 이전 맵 방에서 빠지며 Game_PlayerLeft를 알리고,
        // 새 맵 방에 들어가며 그 방의 기존 접속자 목록(Game_MapChangeAck)을 받고 자신의 입장을 알린다.
        private async Task HandleMapChangeRequestAsync(byte[] body, CancellationToken ct)
        {
            var request = C2SMapChangeRequest.Decode(body);

            if (_playerId is not { } playerId || request.PlayerId != playerId
                || _room is not { } previousRoom || _mapId is not { } previousMapId)
            {
                return;
            }

            if (!previousRoom.TryGetInfo(playerId, out var info))
            {
                return;
            }

            previousRoom.Remove(playerId);
            var left = new S2CPlayerLeft { PlayerId = playerId };
            await previousRoom.BroadcastAsync(OpCode.Game_PlayerLeft, left.Encode(), playerId, ct);
            _mapRooms.RemoveIfEmpty(previousMapId, previousRoom);

            // HP 등 나머지 전투 스탯은 PlayerInfo 인스턴스를 그대로 재사용해 유지하고, 위치/맵만 갱신한다.
            info.MapId = request.MapId;
            info.X = request.X;
            info.Y = request.Y;
            info.Z = request.Z;
            info.RotationY = request.RotationY;

            GameRoom nextRoom = _mapRooms.GetOrCreate(request.MapId);
            var existingPlayers = nextRoom.SnapshotExcluding(playerId);
            nextRoom.Add(info, this);

            _room = nextRoom;
            _mapId = request.MapId;

            var ack = new S2CEnterAck { ExistingPlayers = existingPlayers };
            await SendAsync(OpCode.Game_MapChangeAck, ack.Encode(), ct);

            var joined = new S2CPlayerJoined { Player = info };
            await nextRoom.BroadcastAsync(OpCode.Game_PlayerJoined, joined.Encode(), playerId, ct);
        }

        // 룸의 위치를 갱신하고, 본인을 제외한 나머지 접속자에게 브로드캐스트한다.
        private async Task HandleMoveRequestAsync(byte[] body, CancellationToken ct)
        {
            if (_room is not { } room)
            {
                return;
            }

            var request = C2SMoveRequest.Decode(body);
            room.UpdatePosition(request.PlayerId, request.X, request.Y, request.Z, request.RotationY);

            var broadcast = new S2CMoveBroadcast
            {
                PlayerId = request.PlayerId,
                X = request.X,
                Y = request.Y,
                Z = request.Z,
                RotationY = request.RotationY,
                Timestamp = request.Timestamp
            };

            await room.BroadcastAsync(OpCode.Game_MoveBroadcast, broadcast.Encode(), request.PlayerId, ct);
        }

        // Move와 달리 발신자 본인 화면에도 같은 메시지가 떠야 하므로 BroadcastToAllAsync를 쓴다.
        // 닉네임은 클라이언트를 신뢰하지 않고 룸에 등록된(Game_EnterRequest 시점) 값을 서버가 직접 채운다.
        private const int MaxChatMessageLength = 200;

        private async Task HandleChatRequestAsync(byte[] body, CancellationToken ct)
        {
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

            await room.BroadcastToAllAsync(OpCode.Game_ChatBroadcast, broadcast.Encode(), ct);
        }

        // Damage 자체(방어력 적용 전 원본 공격력)는 GameServer가 계산하지 않는다 - 각 클라이언트가
        // 로컬로 들고 있는 target의 실제 Defense로 계산해야 모든 클라이언트가 일관된 결과를 얻는다
        // (attacker/target의 스탯은 Game_EnterRequest 시점에 이미 전원에게 동기화되어 있음).
        // 여기서는 위조된 공격자 신원 차단, 최소 공격 간격, 사거리만 검증하고 그대로 중계한다.
        private const double MinAttackIntervalMs = 300;
        private const float MaxAttackRangeSquared = 5f * 5f;
        private DateTime _lastAttackAtUtc = DateTime.MinValue;

        private async Task HandleAttackRequestAsync(byte[] body, CancellationToken ct)
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

            var broadcast = new S2CDamageBroadcast
            {
                AttackerId = playerId,
                TargetId = request.TargetId,
                Damage = attacker.AttackPower,
                Timestamp = request.Timestamp
            };

            await room.BroadcastToAllAsync(OpCode.Game_DamageBroadcast, broadcast.Encode(), ct);
        }

        // 여러 세션이 동시에(다른 플레이어의 브로드캐스트로) 같은 스트림에 쓸 수 있으므로 직렬화한다.
        public async Task SendAsync(OpCode opCode, byte[] body, CancellationToken ct)
        {
            var frame = PacketFrame.Encode((ushort)opCode, body);

            await _writeLock.WaitAsync(ct);
            try
            {
                await _stream.WriteAsync(frame, ct);
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }
}
