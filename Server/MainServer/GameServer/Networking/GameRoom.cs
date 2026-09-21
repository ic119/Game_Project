using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using GameServer.Monsters;
using Shared;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // ApplyMonsterAttackAsync의 결과. GainedExp가 채워져 있으면(몬스터가 죽고 공격자를 찾은 경우)
    // 호출측(ClientSession)이 공격자 본인에게만 Game_ExpGainBroadcast를 보내야 한다는 뜻이다.
    public readonly struct MonsterAttackResult
    {
        public bool MonsterDied { get; init; }
        public int? GainedExp { get; init; }
        public int NewLevel { get; init; }
        public bool DidLevelUp { get; init; }
        public int ExpToNextLevel { get; init; }
    }

    // 하나의 맵(mapId)에 속한 접속자들의 그룹. MapRoomRegistry가 맵마다 이 인스턴스를 하나씩 관리한다.
    // 접속자 레지스트리 + "본인 제외 전원 브로드캐스트" 헬퍼에 더해, 이 맵의 몬스터 스폰/전투/리스폰까지 담당한다.
    // 몬스터는 소유 클라이언트가 없으므로(플레이어와 달리) 스탯/HP를 GameServer가 직접 들고 권위를 가진다.
    public class GameRoom
    {
        private readonly ConcurrentDictionary<long, (PlayerInfo Info, ClientSession Session)> _players = new();

        // 몬스터 id -> 현재 상태(+ 어느 스폰 포인트 소속인지). 포인트 단위 개체수/리스폰 판정에 쓴다.
        private readonly ConcurrentDictionary<long, MonsterRuntime> _monsters = new();

        // 서버 종료 시 대기 중인 리스폰 타이머(Task.Delay)를 함께 취소하기 위한 토큰.
        // 개별 요청(Game_MonsterAttackRequest 등)의 ct와 달리, 리스폰은 특정 요청에 종속되지 않는
        // 방 자체의 백그라운드 작업이라 서버 전체 수명 토큰을 별도로 받아 둔다.
        private readonly CancellationToken _serverLifetimeCt;

        // 방에 아무도 없으면 MapRoomRegistry가 이 방을 정리(제거)할 수 있도록 알려준다.
        // 몬스터 생존 여부는 판단 기준에 포함하지 않는다 - 몬스터만 남고 플레이어가 없는 방은 정리 대상이다.
        public bool IsEmpty => _players.IsEmpty;

        public GameRoom(List<MonsterSpawnPointDefinition> spawnPoints, CancellationToken serverLifetimeCt)
        {
            _serverLifetimeCt = serverLifetimeCt;

            // 방이 만들어지는 시점(첫 플레이어 입장)에 각 포인트를 최대 개체수까지 즉시 채운다.
            // 아직 아무도 접속하지 않은 시점이라 브로드캐스트가 필요 없다 - 첫 입장자는 S2CEnterAck의
            // ExistingMonsters로 이 초기 스폰 결과를 그대로 받는다.
            foreach (var point in spawnPoints)
            {
                for (int i = 0; i < point.MaxAlive; i++)
                {
                    SpawnMonsterAtPoint(point);
                }
            }

            _ = RunMonsterAiLoopAsync(_serverLifetimeCt);
        }

        public void Add(PlayerInfo info, ClientSession session)
        {
            _players[info.PlayerId] = (info, session);
        }

        public void UpdatePosition(long playerId, float x, float y, float z, float rotationY)
        {
            if (_players.TryGetValue(playerId, out var entry))
            {
                entry.Info.X = x;
                entry.Info.Y = y;
                entry.Info.Z = z;
                entry.Info.RotationY = rotationY;
            }
        }

        public void Remove(long playerId)
        {
            _players.TryRemove(playerId, out _);
        }

        // playerId를 제외한 현재 접속자 스냅샷(신규 입장자에게 Game_EnterAck으로 보내줄 목록).
        public List<PlayerInfo> SnapshotExcluding(long playerId)
        {
            var result = new List<PlayerInfo>();
            foreach (var (info, _) in _players.Values)
            {
                if (info.PlayerId != playerId)
                {
                    result.Add(info);
                }
            }
            return result;
        }

        // 현재 이 방(맵)에 살아있는 몬스터 전체 스냅샷. S2CEnterAck.ExistingMonsters로 그대로 쓰인다.
        public List<MonsterInfo> SnapshotMonsters()
        {
            var result = new List<MonsterInfo>();
            foreach (var runtime in _monsters.Values)
            {
                result.Add(runtime.Info);
            }
            return result;
        }

        public bool TryGetMonsterPosition(long monsterId, out (float X, float Y, float Z) position)
        {
            if (_monsters.TryGetValue(monsterId, out var runtime))
            {
                position = (runtime.Info.X, runtime.Info.Y, runtime.Info.Z);
                return true;
            }

            position = default;
            return false;
        }

        public async Task BroadcastAsync(OpCode opCode, byte[] body, long excludePlayerId, CancellationToken ct)
        {
            foreach (var (info, session) in _players.Values)
            {
                if (info.PlayerId == excludePlayerId)
                {
                    continue;
                }

                await session.SendAsync(opCode, body, ct);
            }
        }

        // 채팅처럼 발신자 본인에게도 동일한 메시지를 보여줘야 하는 이벤트용 (Move와 달리 제외 대상이 없다).
        public async Task BroadcastToAllAsync(OpCode opCode, byte[] body, CancellationToken ct)
        {
            foreach (var (_, session) in _players.Values)
            {
                await session.SendAsync(opCode, body, ct);
            }
        }

        public bool TryGetInfo(long playerId, [NotNullWhen(true)] out PlayerInfo? info)
        {
            if (_players.TryGetValue(playerId, out var entry))
            {
                info = entry.Info;
                return true;
            }

            info = null;
            return false;
        }

        // 공격 판정 자체(사거리/쿨다운 등)는 ClientSession이 검증한 뒤 이 메서드를 호출한다.
        // 데미지 계산(공격력-방어력)과 사망/리스폰 판정, 브로드캐스트까지 전부 여기서 처리한다 -
        // 몬스터의 Defense/HP를 아는 유일한 주체가 GameRoom(서버)이기 때문에, 클라이언트가 각자
        // 델타를 계산하는 플레이어 간 전투와 달리 여기서는 서버가 최종 결과(RemainingHp)를 직접 만들어 중계한다.
        public async Task<MonsterAttackResult> ApplyMonsterAttackAsync(long monsterId, long attackerId, int attackerAttackPower, long timestamp, CancellationToken ct)
        {
            if (!_monsters.TryGetValue(monsterId, out var runtime))
            {
                return default;
            }

            int damage = Math.Max(1, attackerAttackPower - runtime.Info.Defense);
            runtime.Info.CurrentHp = Math.Max(0, runtime.Info.CurrentHp - damage);

            var damageBroadcast = new S2CMonsterDamageBroadcast
            {
                MonsterId = monsterId,
                AttackerId = attackerId,
                Damage = damage,
                RemainingHp = runtime.Info.CurrentHp,
                Timestamp = timestamp
            };
            await BroadcastToAllAsync(OpCode.Game_MonsterDamageBroadcast, damageBroadcast.Encode(), ct);

            if (runtime.Info.CurrentHp > 0)
            {
                return default;
            }

            _monsters.TryRemove(monsterId, out _);

            var dieBroadcast = new S2CMonsterDieBroadcast { MonsterId = monsterId, Timestamp = timestamp };
            await BroadcastToAllAsync(OpCode.Game_MonsterDieBroadcast, dieBroadcast.Encode(), ct);

            // 리스폰은 이 공격 요청 처리와 독립적인 타이머이므로 기다리지 않고 흘려보낸다(fire-and-forget).
            _ = RespawnAfterDelayAsync(runtime.Point);

            // 처치자의 살아있는 PlayerInfo를 직접 찾아 경험치/레벨을 그 자리에서 갱신한다(플레이어 세션이
            // 유일하게 이 값을 들고 있는 주체 - GameServer는 DB가 없어 여기서만 값이 존재한다).
            if (!_players.TryGetValue(attackerId, out var attackerEntry))
            {
                return new MonsterAttackResult { MonsterDied = true };
            }

            int level = attackerEntry.Info.Level;
            int exp = attackerEntry.Info.Exp;
            bool applied = ExpTable.TryApplyExp(ref level, ref exp, runtime.ExpReward, out int expToNextLevel);
            if (!applied)
            {
                // 이미 만렙 - 경험치를 지급하지 않는다.
                return new MonsterAttackResult { MonsterDied = true };
            }

            bool didLevelUp = level != attackerEntry.Info.Level;
            attackerEntry.Info.Level = level;
            attackerEntry.Info.Exp = exp;

            return new MonsterAttackResult
            {
                MonsterDied = true,
                GainedExp = runtime.ExpReward,
                NewLevel = level,
                DidLevelUp = didLevelUp,
                ExpToNextLevel = expToNextLevel
            };
        }

        private async Task RespawnAfterDelayAsync(MonsterSpawnPointDefinition point)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(point.RespawnSeconds), _serverLifetimeCt);
            }
            catch (TaskCanceledException)
            {
                // 서버 종료로 인한 정상 취소 - 리스폰하지 않는다.
                return;
            }

            MonsterInfo spawned = SpawnMonsterAtPoint(point);

            var broadcast = new S2CMonsterSpawnBroadcast { Monster = spawned };
            try
            {
                await BroadcastToAllAsync(OpCode.Game_MonsterSpawnBroadcast, broadcast.Encode(), _serverLifetimeCt);
            }
            catch (OperationCanceledException)
            {
                // 서버 종료 - 무시.
            }
        }

        // 같은 포인트에서 maxAlive > 1로 여러 마리가 스폰될 때 한 좌표에 겹쳐 뭉치지 않도록,
        // 포인트 중심에서 이 반경 안의 원 안에 균등 분포로 스폰 좌표를 흩뿌린다.
        private const float SpawnJitterRadius = 1.5f;

        // point.Entries 중 하나를 무작위로 골라 그 타입/스탯으로 몬스터를 만든다. 리스폰마다 다시 호출되므로
        // 같은 포인트에서도 스폰될 때마다 다른 타입이 나올 수 있다.
        private MonsterInfo SpawnMonsterAtPoint(MonsterSpawnPointDefinition point)
        {
            MonsterSpawnEntry entry = point.Entries[Random.Shared.Next(point.Entries.Count)];
            (float homeX, float homeZ) = ApplySpawnJitter(point.X, point.Z);

            var info = new MonsterInfo
            {
                MonsterId = MonsterIdGenerator.Next(),
                MonsterType = entry.MonsterType,
                MaxHp = entry.MaxHp,
                CurrentHp = entry.MaxHp,
                AttackPower = entry.AttackPower,
                Defense = entry.Defense,
                X = homeX,
                Y = point.Y,
                Z = homeZ,
                RotationY = point.RotationY,
                ExpReward = entry.ExpReward,
                PointId = point.PointId
            };

            _monsters[info.MonsterId] = new MonsterRuntime(info, point, entry.ExpReward, homeX, homeZ);
            return info;
        }

        // 반지름에 sqrt(균등난수)를 곱해 원 "둘레"가 아니라 "넓이" 기준으로 균등 분포시킨다
        // (sqrt 보정이 없으면 중심 근처에 점이 몰린다).
        private static (float X, float Z) ApplySpawnJitter(float centerX, float centerZ)
        {
            float angle = Random.Shared.NextSingle() * MathF.PI * 2f;
            float radius = MathF.Sqrt(Random.Shared.NextSingle()) * SpawnJitterRadius;
            return (centerX + MathF.Cos(angle) * radius, centerZ + MathF.Sin(angle) * radius);
        }

        private sealed class MonsterRuntime
        {
            public MonsterInfo Info { get; }
            public MonsterSpawnPointDefinition Point { get; }

            // 스폰 시 지터가 적용된 이 개체만의 실제 스폰 좌표(SpawnMonsterAtPoint 참고). 리쉬 판정과 복귀
            // 목적지에 Point.X/Z(포인트 공유 중심) 대신 이 값을 써서, 전투 후 복귀해도 다시 한 좌표로
            // 뭉치지 않고 각자 스폰됐던 자리로 흩어진 채 대기하게 한다.
            public float HomeX { get; }
            public float HomeZ { get; }

            // 스폰 시 선택된 엔트리의 경험치. Point.Entries 중 어느 것이 뽑혔는지는 리스폰마다 달라질 수 있어
            // Point가 아니라 이 인스턴스에 따로 저장해둔다(ApplyMonsterAttackAsync가 처치 시 참조).
            public int ExpReward { get; }

            public MonsterAiState AiState { get; set; } = MonsterAiState.Idle;
            public long? TargetPlayerId { get; set; }

            public MonsterRuntime(MonsterInfo info, MonsterSpawnPointDefinition point, int expReward, float homeX, float homeZ)
            {
                Info = info;
                Point = point;
                ExpReward = expReward;
                HomeX = homeX;
                HomeZ = homeZ;
            }
        }

        #region Method - Monster AI
        private static readonly TimeSpan AiTickInterval = TimeSpan.FromMilliseconds(150);
        private const float ArrivalThreshold = 0.1f;

        // 몬스터 추적 AI 틱 루프. 방이 생성되는 시점(첫 입장자)에 시작해 서버가 종료될 때까지 돈다.
        // 개별 요청과 무관한 방의 백그라운드 작업이라 리스폰 타이머(RespawnAfterDelayAsync)와 같은
        // 서버 전체 수명 토큰을 쓴다 - 방이 비어도 이 루프 자체는 멈추지 않지만, 플레이어가 없으면
        // 감지 대상이 없어 순회 비용만 남는다(몬스터 수가 매우 적은 MVP 규모라 무시할 만하다).
        private async Task RunMonsterAiLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(AiTickInterval, ct);
                    await TickMonsterAiAsync((float)AiTickInterval.TotalSeconds, ct);
                }
            }
            catch (TaskCanceledException)
            {
                // 서버 종료로 인한 정상 취소.
            }
        }

        // 몬스터 종류에 관계없이 공통으로 동작한다 - 몬스터별 분기 없이 MonsterSpawnPointDefinition의
        // DetectionRange/ChaseSpeed/LeashRange 값만으로 감지/추적/복귀를 판단하므로, 새 몬스터 타입을
        // 추가해도 이 로직은 수정할 필요가 없다.
        private async Task TickMonsterAiAsync(float deltaSeconds, CancellationToken ct)
        {
            foreach (var runtime in _monsters.Values)
            {
                if (!UpdateMonster(runtime, deltaSeconds))
                {
                    continue;
                }

                var broadcast = new S2CMonsterMoveBroadcast
                {
                    MonsterId = runtime.Info.MonsterId,
                    X = runtime.Info.X,
                    Y = runtime.Info.Y,
                    Z = runtime.Info.Z,
                    RotationY = runtime.Info.RotationY,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                await BroadcastToAllAsync(OpCode.Game_MonsterMoveBroadcast, broadcast.Encode(), ct);
            }
        }

        // 몬스터 한 마리의 상태를 한 틱만큼 갱신한다. 위치가 실제로 바뀌었으면 true를 반환해
        // 호출측이 브로드캐스트를 보내게 한다(제자리 대기 중인 몬스터까지 매 틱 보낼 필요는 없다).
        private bool UpdateMonster(MonsterRuntime runtime, float deltaSeconds)
        {
            switch (runtime.AiState)
            {
                case MonsterAiState.Idle:
                    long? foundTargetId = FindNearestPlayerInRange(runtime.Info, runtime.Point.DetectionRange);
                    if (foundTargetId is { } targetId)
                    {
                        runtime.AiState = MonsterAiState.Chasing;
                        runtime.TargetPlayerId = targetId;
                    }
                    return false;

                case MonsterAiState.Chasing:
                    return TickChasing(runtime, deltaSeconds);

                case MonsterAiState.Returning:
                    return TickReturning(runtime, deltaSeconds);

                default:
                    return false;
            }
        }

        private bool TickChasing(MonsterRuntime runtime, float deltaSeconds)
        {
            if (runtime.TargetPlayerId is not { } targetId
                || !_players.TryGetValue(targetId, out var targetEntry)
                || targetEntry.Info.CurrentHp <= 0)
            {
                runtime.TargetPlayerId = null;
                runtime.AiState = MonsterAiState.Returning;
                return false;
            }

            MonsterInfo info = runtime.Info;
            PlayerInfo target = targetEntry.Info;

            float distanceFromSpawn = Distance(info.X, info.Z, runtime.HomeX, runtime.HomeZ);
            if (distanceFromSpawn > runtime.Point.LeashRange)
            {
                runtime.TargetPlayerId = null;
                runtime.AiState = MonsterAiState.Returning;
                return false;
            }

            return MoveToward(info, target.X, target.Z, runtime.Point.ChaseSpeed, deltaSeconds);
        }

        // 리쉬 범위를 벗어나 추적을 포기한 몬스터가 스폰 지점으로 되돌아간다. 도착 전까지는
        // 재감지를 하지 않는다(복귀 도중 계속 재어그로되면 영영 스폰 지점으로 못 돌아갈 수 있다).
        private bool TickReturning(MonsterRuntime runtime, float deltaSeconds)
        {
            MonsterInfo info = runtime.Info;

            bool moved = MoveToward(info, runtime.HomeX, runtime.HomeZ, runtime.Point.ChaseSpeed, deltaSeconds);

            if (Distance(info.X, info.Z, runtime.HomeX, runtime.HomeZ) <= ArrivalThreshold)
            {
                info.X = runtime.HomeX;
                info.Z = runtime.HomeZ;
                info.RotationY = runtime.Point.RotationY;
                runtime.AiState = MonsterAiState.Idle;
                return true;
            }

            return moved;
        }

        // targetX/targetZ 방향으로 moveSpeed(초당 이동 거리)만큼 이동시키고 그 방향을 바라보게 회전시킨다.
        // 한 틱에 이동할 거리가 남은 거리보다 크면 목표 지점에서 멈춘다(오버슈트 방지).
        private static bool MoveToward(MonsterInfo info, float targetX, float targetZ, float moveSpeed, float deltaSeconds)
        {
            float dx = targetX - info.X;
            float dz = targetZ - info.Z;
            float distance = MathF.Sqrt(dx * dx + dz * dz);

            if (distance <= ArrivalThreshold)
            {
                return false;
            }

            float step = Math.Min(distance, moveSpeed * deltaSeconds);
            info.X += dx / distance * step;
            info.Z += dz / distance * step;
            info.RotationY = MathF.Atan2(dx, dz) * (180f / MathF.PI);

            return true;
        }

        private long? FindNearestPlayerInRange(MonsterInfo monster, float range)
        {
            long? nearestId = null;
            float nearestDistanceSquared = range * range;

            foreach (var (info, _) in _players.Values)
            {
                if (info.CurrentHp <= 0)
                {
                    continue;
                }

                float dx = info.X - monster.X;
                float dz = info.Z - monster.Z;
                float distanceSquared = dx * dx + dz * dz;

                if (distanceSquared <= nearestDistanceSquared)
                {
                    nearestDistanceSquared = distanceSquared;
                    nearestId = info.PlayerId;
                }
            }

            return nearestId;
        }

        private static float Distance(float x1, float z1, float x2, float z2)
        {
            float dx = x1 - x2;
            float dz = z1 - z2;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
        #endregion
    }
}
