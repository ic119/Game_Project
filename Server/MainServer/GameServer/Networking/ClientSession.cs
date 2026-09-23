using System.Net.Sockets;
using System.Text;
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
        private readonly PlayerAuthValidator _authValidator;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        // 이 세션이 Game_EnterRequest로 등록한 플레이어 id. 등록 전이면 null.
        private long? _playerId;

        // 현재 속한 맵의 GameRoom과 그 mapId. Game_EnterRequest 전이면 null.
        // Game_MapChangeRequest로 다른 맵으로 옮길 때 이 필드 자체를 교체한다(GameRoom은 수정하지 않음).
        private GameRoom? _room;
        private string? _mapId;

        public ClientSession(TcpClient tcpClient, MapRoomRegistry mapRooms, PlayerAuthValidator authValidator)
        {
            _tcpClient = tcpClient;
            _stream = tcpClient.GetStream();
            _mapRooms = mapRooms;
            _authValidator = authValidator;
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
            catch (EnterAuthFailedException)
            {
                // Game_EnterRequest 인증 실패로 HandleEnterRequestAsync가 의도적으로 연결을 종료한 경우.
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
                OpCode.Game_MonsterAttackRequest => HandleMonsterAttackRequestAsync(body, ct),
                OpCode.Game_StatUpdateRequest => HandleStatUpdateRequestAsync(body),
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
        // 등록 전에 AccessToken이 info.PlayerId(characterId)를 실제로 소유한 계정의 것인지 AuthServer에
        // 확인한다 - 그렇지 않으면 누구나 임의의 PlayerId를 자칭해 접속/조작할 수 있기 때문이다.
        private async Task HandleEnterRequestAsync(byte[] body, CancellationToken ct)
        {
            var request = C2SEnterRequest.Decode(body);
            var info = request.Player;

            if (!await _authValidator.OwnsCharacterAsync(request.AccessToken, info.PlayerId, ct))
            {
                Console.WriteLine($"[GameServer] Game_EnterRequest 인증 실패 (PlayerId={info.PlayerId}) - 연결을 종료합니다.");
                byte[] errorBody = Encoding.UTF8.GetBytes("인증에 실패했습니다.");
                await SendAsync(OpCode.System_Error, errorBody, ct);
                // 소켓은 RunAsync의 finally에서 정리한다 - 여기서 직접 Close()하면 그 직후 루프가
                // 다시 스트림을 읽으려 할 때 처리되지 않은 ObjectDisposedException이 발생한다.
                throw new EnterAuthFailedException();
            }

            _playerId = info.PlayerId;

            GameRoom room = _mapRooms.GetOrCreate(info.MapId);
            _room = room;
            _mapId = info.MapId;

            var existingPlayers = room.SnapshotExcluding(info.PlayerId);
            var existingMonsters = room.SnapshotMonsters();
            room.Add(info, this);

            var ack = new S2CEnterAck { ExistingPlayers = existingPlayers, ExistingMonsters = existingMonsters };
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
            var existingMonsters = nextRoom.SnapshotMonsters();
            nextRoom.Add(info, this);

            _room = nextRoom;
            _mapId = request.MapId;

            var ack = new S2CEnterAck { ExistingPlayers = existingPlayers, ExistingMonsters = existingMonsters };
            await SendAsync(OpCode.Game_MapChangeAck, ack.Encode(), ct);

            var joined = new S2CPlayerJoined { Player = info };
            await nextRoom.BroadcastAsync(OpCode.Game_PlayerJoined, joined.Encode(), playerId, ct);
        }

        // 룸의 위치를 갱신하고, 본인을 제외한 나머지 접속자에게 브로드캐스트한다.
        // request.PlayerId가 이 세션의 실제 플레이어와 같은지 검증한다 - 그렇지 않으면 다른 플레이어의
        // ID를 실어 보내는 것만으로 그 플레이어를 임의의 위치로 옮길 수 있다(다른 핸들러들과 동일한 검증).
        private async Task HandleMoveRequestAsync(byte[] body, CancellationToken ct)
        {
            var request = C2SMoveRequest.Decode(body);

            if (_playerId is not { } playerId || request.PlayerId != playerId || _room is not { } room)
            {
                return;
            }

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
        // 클라이언트 PlayerAttackController.comboInputGuard(150ms)가 지나면 2타 콤보 입력을 즉시 받아들여
        // 두 번째 Game_AttackRequest/Game_MonsterAttackRequest를 보낸다. 이 값이 그보다 크면(과거 300ms)
        // 정상적인 콤보 2타 요청까지 여기서 조용히 드롭되어 "애니메이션은 2콤보, 데미지는 1타"만 반영되는
        // 문제가 생기므로, comboInputGuard보다 여유를 두고 짧게 잡아 정상 콤보는 통과시키고 그보다
        // 빠른(매크로 등) 연타만 차단한다. comboInputGuard를 바꾸면 이 값도 함께 맞춰야 한다.
        private const double MinAttackIntervalMs = 100;
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

        // 플레이어 공격(HandleAttackRequestAsync)과 같은 쿨다운(_lastAttackAtUtc)을 공유한다 - 그렇지 않으면
        // 플레이어 공격과 몬스터 공격 요청을 번갈아 보내 최소 공격 간격 제한을 우회할 수 있다.
        // 데미지 계산 자체(공격력-방어력)는 GameRoom.ApplyMonsterAttackAsync가 서버 권위로 수행한다 -
        // 몬스터는 소유 클라이언트가 없어 HandleAttackRequestAsync(PvP)처럼 "그대로 중계만" 할 수 없기 때문이다.
        private async Task HandleMonsterAttackRequestAsync(byte[] body, CancellationToken ct)
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

            if (!room.TryGetInfo(playerId, out var attacker) || !room.TryGetMonsterPosition(request.MonsterId, out var monsterPosition))
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

            MonsterAttackResult result = await room.ApplyMonsterAttackAsync(request.MonsterId, playerId, attacker.AttackPower, request.Timestamp, ct);

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
                await SendAsync(OpCode.Game_ExpGainBroadcast, expGain.Encode(), ct);
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
                await SendAsync(OpCode.Game_LootBroadcast, loot.Encode(), ct);
            }
        }

        // 인벤토리에서 장비를 장착/해제해 공격력/방어력이 바뀌었을 때 클라이언트가 보낸다. 브로드캐스트가
        // 필요 없어(GameRoom.TryUpdateCombatStats 주석 참고) 응답 없이 서버 캐시만 갱신한다.
        private Task HandleStatUpdateRequestAsync(byte[] body)
        {
            var request = C2SStatUpdateRequest.Decode(body);

            if (_playerId is { } playerId && request.PlayerId == playerId && _room is { } room)
            {
                room.TryUpdateCombatStats(playerId, request.AttackPower, request.Defense);
            }

            return Task.CompletedTask;
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

        // Game_EnterRequest 인증 실패를 RunAsync의 루프 종료 신호로 쓰기 위한 내부 전용 예외.
        private sealed class EnterAuthFailedException : Exception
        {
        }
    }
}
