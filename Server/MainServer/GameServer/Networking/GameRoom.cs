using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using GameServer.Combat;
using GameServer.Maps;
using GameServer.Monsters;
using Shared;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // ApplyMonsterAttack의 결과. GainedExp가 채워져 있으면(몬스터가 죽고 공격자를 찾은 경우)
    // 호출측(ClientSession)이 공격자 본인에게만 Game_ExpGainBroadcast를 보내야 한다는 뜻이다.
    public readonly struct MonsterAttackResult
    {
        public bool MonsterDied { get; init; }
        public int? GainedExp { get; init; }
        public int NewLevel { get; init; }
        public bool DidLevelUp { get; init; }
        public int ExpToNextLevel { get; init; }

        // 처치 시 DropTableCatalog.Roll로 계산된 보상. 공격자를 찾지 못해 GainedExp가 비어도(즉시 return된
        // 경로) 드롭은 이미 굴려진 상태이므로 별도로 채워진다 - 몬스터 처치 자체는 성립했기 때문이다.
        public int GainedGold { get; init; }
        public IReadOnlyList<(string ItemId, int Qty)>? DroppedItems { get; init; }
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

        // 이 방의 맵 id. 부활 위치 등 맵 좌표 데이터(MapDataCatalog)를 찾는 키다.
        private readonly string _mapId;

        public GameRoom(string mapId, List<MonsterSpawnPointDefinition> spawnPoints, CancellationToken serverLifetimeCt)
        {
            _mapId = mapId;
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

        // session이 등록한 항목일 때만 제거하고, 실제로 제거했으면 true. 같은 캐릭터가 새 세션으로 다시 입장해 항목이
        // 교체된 뒤에는 이전 세션이 종료되면서 호출해도 새 세션의 항목을 지우지 않는다 - 이때 호출측은 퇴장 알림도 보내면 안 된다.
        public bool Remove(long playerId, ClientSession session)
        {
            if (!_players.TryGetValue(playerId, out var entry) || !ReferenceEquals(entry.Session, session))
            {
                return false;
            }

            return _players.TryRemove(new KeyValuePair<long, (PlayerInfo Info, ClientSession Session)>(playerId, entry));
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

        // 브로드캐스트는 각 세션의 전송 대기열에 넣기만 하고 바로 돌아온다(ClientSession.Send). 실제 소켓 쓰기는 세션마다
        // 전송 루프가 따로 하므로, 느린 클라이언트 한 명이 방 전체 전송이나 AI 틱을 붙잡거나, 한 세션의 전송 오류가
        // 호출측(다른 플레이어의 요청 처리, AI 루프)으로 번지지 않는다.
        public void Broadcast(OpCode opCode, byte[] body, long excludePlayerId)
        {
            foreach (var (info, session) in _players.Values)
            {
                if (info.PlayerId == excludePlayerId)
                {
                    continue;
                }

                session.Send(opCode, body);
            }
        }

        // 채팅처럼 발신자 본인에게도 동일한 메시지를 보여줘야 하는 이벤트용 (Move와 달리 제외 대상이 없다).
        public void BroadcastToAll(OpCode opCode, byte[] body)
        {
            foreach (var (_, session) in _players.Values)
            {
                session.Send(opCode, body);
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

        // 인벤토리에서 장비를 장착/해제해 바뀐 공격력/방어력을 반영한다(Game_StatUpdateRequest). PlayerInfo가
        // class(참조 타입)라 이 메서드로 값만 바꿔주면 ApplyMonsterAttack/AttackPlayer가 다음 판정부터
        // 곧바로 새 값을 쓴다 - Game_EnterRequest 스냅샷 이후 갱신 경로가 이것뿐이므로, 호출하지 않으면 세션 내내
        // 접속 시점 스탯으로 고정된다. 다른 접속자에게 알릴 필요는 없다(PvP 피해도 서버가 이 값으로 계산해 결과만 보낸다).
        public bool TryUpdateCombatStats(long playerId, int attackPower, int defense)
        {
            if (!_players.TryGetValue(playerId, out var entry))
            {
                return false;
            }

            entry.Info.AttackPower = attackPower;
            entry.Info.Defense = defense;
            return true;
        }

        // 공격 판정 자체(사거리/쿨다운 등)는 ClientSession이 검증한 뒤 이 메서드를 호출한다.
        // 데미지 계산(공격력-방어력)과 사망/리스폰 판정, 브로드캐스트까지 전부 여기서 처리한다 -
        // 몬스터의 Defense/HP를 아는 유일한 주체가 GameRoom(서버)이기 때문에, 서버가 최종 결과(RemainingHp)를
        // 직접 만들어 중계한다(플레이어 간 전투도 ApplyPlayerAttack에서 같은 방식으로 처리한다).
        public MonsterAttackResult ApplyMonsterAttack(long monsterId, long attackerId, int attackerAttackPower, long timestamp)
        {
            if (!_monsters.TryGetValue(monsterId, out var runtime))
            {
                return default;
            }

            // 여러 세션(다른 플레이어)이 같은 몬스터를 동시에 공격할 수 있으므로, HP 차감과 "이 공격으로 죽었는가"
            // 판정을 한 번에 원자적으로 처리한다. 예전에는 HP 차감 -> 피격 브로드캐스트(당시 await) -> 사망 처리 순서라,
            // 그 await 사이에 들어온 다른 공격도 HP 0을 보고 사망 처리를 반복해 드롭/리스폰이 중복됐다.
            // 이제 HP가 0이 되는 순간을 만든 공격 하나만 killedByThisAttack = true가 된다.
            int damage;
            int remainingHp;
            bool killedByThisAttack;
            lock (runtime)
            {
                if (runtime.Info.CurrentHp <= 0)
                {
                    // 이미 다른 공격으로 처치가 확정된 몬스터 - 피격/보상 모두 무시한다.
                    return default;
                }

                damage = Math.Max(1, attackerAttackPower - runtime.Info.Defense);
                runtime.Info.CurrentHp = Math.Max(0, runtime.Info.CurrentHp - damage);
                remainingHp = runtime.Info.CurrentHp;
                killedByThisAttack = remainingHp <= 0;
            }

            if (killedByThisAttack)
            {
                // 브로드캐스트 전에 목록에서 빼서, 그 사이 들어온 공격/AI 틱/신규 입장자 스냅샷이
                // 이미 죽은 몬스터를 더 이상 보지 않게 한다.
                _monsters.TryRemove(monsterId, out _);
            }

            var damageBroadcast = new S2CMonsterDamageBroadcast
            {
                MonsterId = monsterId,
                AttackerId = attackerId,
                Damage = damage,
                RemainingHp = remainingHp,
                Timestamp = timestamp
            };
            BroadcastToAll(OpCode.Game_MonsterDamageBroadcast, damageBroadcast.Encode());

            if (!killedByThisAttack)
            {
                return default;
            }

            // 골드/아이템 드롭은 경험치 지급 성공 여부(공격자 존재, 만렙 여부)와 무관하게 처치 자체에
            // 대한 보상이므로, 아래 조기 반환 분기들과 상관없이 항상 한 번만 굴려 결과에 실어 보낸다.
            (int gainedGold, List<(string ItemId, int Qty)> droppedItems) = DropTableCatalog.Roll(runtime.Info.MonsterType);

            var dieBroadcast = new S2CMonsterDieBroadcast { MonsterId = monsterId, Timestamp = timestamp };
            BroadcastToAll(OpCode.Game_MonsterDieBroadcast, dieBroadcast.Encode());

            // 리스폰은 이 공격 요청 처리와 독립적인 타이머이므로 기다리지 않고 흘려보낸다(fire-and-forget).
            _ = RespawnAfterDelayAsync(runtime.Point);

            // 처치자의 살아있는 PlayerInfo를 직접 찾아 경험치/레벨을 그 자리에서 갱신한다(플레이어 세션이
            // 유일하게 이 값을 들고 있는 주체 - GameServer는 DB가 없어 여기서만 값이 존재한다).
            if (!_players.TryGetValue(attackerId, out var attackerEntry))
            {
                return new MonsterAttackResult { MonsterDied = true, GainedGold = gainedGold, DroppedItems = droppedItems };
            }

            int level = attackerEntry.Info.Level;
            int exp = attackerEntry.Info.Exp;
            bool applied = ExpTable.TryApplyExp(ref level, ref exp, runtime.ExpReward, out int expToNextLevel);
            if (!applied)
            {
                // 이미 만렙 - 경험치는 지급하지 않지만 골드/아이템 드롭은 그대로 지급한다.
                return new MonsterAttackResult { MonsterDied = true, GainedGold = gainedGold, DroppedItems = droppedItems };
            }

            int previousLevel = attackerEntry.Info.Level;
            bool didLevelUp = level != previousLevel;
            attackerEntry.Info.Level = level;
            attackerEntry.Info.Exp = exp;

            if (didLevelUp)
            {
                ApplyLevelUpHealth(attackerEntry.Info, level - previousLevel);
            }

            return new MonsterAttackResult
            {
                MonsterDied = true,
                GainedExp = runtime.ExpReward,
                NewLevel = level,
                DidLevelUp = didLevelUp,
                ExpToNextLevel = expToNextLevel,
                GainedGold = gainedGold,
                DroppedItems = droppedItems
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
            BroadcastToAll(OpCode.Game_MonsterSpawnBroadcast, broadcast.Encode());
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
            // Point가 아니라 이 인스턴스에 따로 저장해둔다(ApplyMonsterAttack이 처치 시 참조).
            public int ExpReward { get; }

            public MonsterAiState AiState { get; set; } = MonsterAiState.Idle;
            public long? TargetPlayerId { get; set; }

            // TickChasing이 근접 사거리 안에서 공격할 때마다 AttackIntervalSeconds로 리셋하고, 매 틱
            // deltaSeconds만큼 줄인다. 0 이하면 다음 틱에 바로 공격 가능.
            public float AttackCooldownRemaining { get; set; }

            public MonsterRuntime(MonsterInfo info, MonsterSpawnPointDefinition point, int expReward, float homeX, float homeZ)
            {
                Info = info;
                Point = point;
                ExpReward = expReward;
                HomeX = homeX;
                HomeZ = homeZ;
            }
        }

        #region Method - Player Health
        // 플레이어 HP는 서버(이 GameRoom)가 유일한 권위다. 피해/레벨업/사망/부활은 모두 여기서 계산하고,
        // 클라이언트에는 결과(최종 피해량, 남은 체력)만 보낸다 - 클라이언트가 계산하던 예전 방식은
        // 피격을 무시하는 변조 클라이언트가 무적이 될 수 있었고, 서버/클라이언트 HP가 서로 어긋났다.
        // HP는 AI 루프(몬스터 공격)와 여러 세션(PvP)이 동시에 바꿀 수 있으므로 PlayerInfo 인스턴스를 lock으로 쓴다.

        // 사망 후 자동 부활까지 걸리는 시간.
        private static readonly TimeSpan ReviveDelay = TimeSpan.FromSeconds(5);

        // target에게 방어력 적용 전 피해(rawDamage)를 준다. 이미 사망한 대상이면 false(피해 없음).
        // died는 이 피해로 체력이 0이 된 경우에만 true다 - 사망 처리(부활 예약)가 한 번만 일어나게 한다.
        private static bool TryDamagePlayer(PlayerInfo target, int rawDamage, out int finalDamage, out int remainingHp, out bool died)
        {
            lock (target)
            {
                if (target.CurrentHp <= 0)
                {
                    finalDamage = 0;
                    remainingHp = 0;
                    died = false;
                    return false;
                }

                finalDamage = CombatStatCalculator.ApplyDefense(rawDamage, target.Defense);
                target.CurrentHp = Math.Max(0, target.CurrentHp - finalDamage);
                remainingHp = target.CurrentHp;
                died = remainingHp <= 0;
                return true;
            }
        }

        // PvP 공격. 사거리/쿨다운은 ClientSession이 검증한 뒤 호출한다. 공격자나 대상이 이미 사망했으면 무시한다.
        public void ApplyPlayerAttack(long attackerId, long targetId, long timestamp)
        {
            if (!_players.TryGetValue(attackerId, out var attackerEntry) || attackerEntry.Info.CurrentHp <= 0
                || !_players.TryGetValue(targetId, out var targetEntry))
            {
                return;
            }

            if (!TryDamagePlayer(targetEntry.Info, attackerEntry.Info.AttackPower, out int finalDamage, out int remainingHp, out bool died))
            {
                return;
            }

            var broadcast = new S2CDamageBroadcast
            {
                AttackerId = attackerId,
                TargetId = targetId,
                Damage = finalDamage,
                RemainingHp = remainingHp,
                Timestamp = timestamp
            };
            BroadcastToAll(OpCode.Game_DamageBroadcast, broadcast.Encode());

            if (died)
            {
                _ = ReviveAfterDelayAsync(targetEntry.Info);
            }
        }

        // 회복 아이템을 써도 효과가 있는 상태인지(살아 있고 체력이 가득 차지 않음). 아이템을 차감하기 전에 확인해
        // "효과 없는 사용"으로 아이템만 사라지는 것을 막는다.
        public bool CanBeHealed(long playerId)
        {
            if (!_players.TryGetValue(playerId, out var entry))
            {
                return false;
            }

            lock (entry.Info)
            {
                return entry.Info.CurrentHp > 0 && entry.Info.CurrentHp < entry.Info.MaxHp;
            }
        }

        // 최대 체력의 healPercent%만큼 회복하고 방 전체에 새 체력을 알린다. 그 사이 사망했거나 이미 가득 찼으면 false.
        public bool TryHealPlayer(long playerId, int healPercent)
        {
            if (!_players.TryGetValue(playerId, out var entry))
            {
                return false;
            }

            PlayerInfo player = entry.Info;
            int currentHp;
            int maxHp;
            lock (player)
            {
                if (player.CurrentHp <= 0 || player.CurrentHp >= player.MaxHp)
                {
                    return false;
                }

                player.CurrentHp = Math.Min(player.MaxHp, player.CurrentHp + CombatStatCalculator.CalculateHealAmount(player.MaxHp, healPercent));
                currentHp = player.CurrentHp;
                maxHp = player.MaxHp;
            }

            var broadcast = new S2CPlayerHpBroadcast { PlayerId = playerId, CurrentHp = currentHp, MaxHp = maxHp };
            BroadcastToAll(OpCode.Game_PlayerHpBroadcast, broadcast.Encode());
            return true;
        }

        // 레벨업 시 최대 체력을 올리고 체력을 가득 채운다(클라이언트가 레벨업 때 하던 처리를 서버로 옮긴 것).
        private void ApplyLevelUpHealth(PlayerInfo player, int levelsGained)
        {
            int currentHp;
            int maxHp;
            lock (player)
            {
                player.MaxHp += levelsGained * CombatStatCalculator.MaxHpPerLevel;
                player.CurrentHp = player.MaxHp;
                currentHp = player.CurrentHp;
                maxHp = player.MaxHp;
            }

            var broadcast = new S2CPlayerHpBroadcast { PlayerId = player.PlayerId, CurrentHp = currentHp, MaxHp = maxHp };
            BroadcastToAll(OpCode.Game_PlayerHpBroadcast, broadcast.Encode());
        }

        // 사망한 플레이어를 ReviveDelay 뒤 가득 찬 체력으로 부활시킨다. 그 사이 접속이 끊겼으면(방에서 빠짐)
        // 아무 것도 하지 않는다 - 재접속하면 입장 처리에서 어차피 가득 찬 체력으로 시작한다.
        // 사망 중에는 맵 이동이 막혀 있으므로(ClientSession.HandleMapChangeRequest) 다른 방으로 옮겨 갔을 일은 없다.
        private async Task ReviveAfterDelayAsync(PlayerInfo player)
        {
            try
            {
                await Task.Delay(ReviveDelay, _serverLifetimeCt);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (!_players.TryGetValue(player.PlayerId, out var entry) || !ReferenceEquals(entry.Info, player))
            {
                return;
            }

            int currentHp;
            int maxHp;
            lock (player)
            {
                if (player.CurrentHp > 0)
                {
                    return;
                }

                player.CurrentHp = player.MaxHp;
                currentHp = player.CurrentHp;
                maxHp = player.MaxHp;

                // 부활 위치는 서버가 맵 데이터로 정한다(클라이언트가 옮긴 좌표를 받아주면 속도 검증을 우회하는 순간이동이 된다).
                // 맵 데이터가 없으면 쓰러진 자리에서 부활한다. 사망 중에는 이동 요청이 거부되므로 여기서 위치를 바꿔도 경합이 없다.
                if (MapDataCatalog.TryGet(_mapId, out MapData mapData) && mapData.RespawnPoint is { } respawnPoint)
                {
                    player.X = respawnPoint.X;
                    player.Y = respawnPoint.Y;
                    player.Z = respawnPoint.Z;
                    player.RotationY = respawnPoint.RotationY;
                }
            }

            var revived = new S2CPlayerRevived
            {
                PlayerId = player.PlayerId,
                CurrentHp = currentHp,
                MaxHp = maxHp,
                X = player.X,
                Y = player.Y,
                Z = player.Z,
                RotationY = player.RotationY
            };
            BroadcastToAll(OpCode.Game_PlayerRevived, revived.Encode());
        }
        #endregion

        #region Method - Monster AI
        private static readonly TimeSpan AiTickInterval = TimeSpan.FromMilliseconds(150);
        private const float ArrivalThreshold = 0.1f;

        // 몬스터 추적 AI 틱 루프. 방이 생성되는 시점(첫 입장자)에 시작해 서버가 종료될 때까지 돈다.
        // 개별 요청과 무관한 방의 백그라운드 작업이라 리스폰 타이머(RespawnAfterDelayAsync)와 같은
        // 서버 전체 수명 토큰을 쓴다 - 방은 비어도 제거되지 않고(MapRoomRegistry, 맵마다 하나) 이 루프도 멈추지 않지만, 플레이어가 없으면
        // 감지 대상이 없어 순회 비용만 남는다(몬스터 수가 매우 적은 MVP 규모라 무시할 만하다).
        private async Task RunMonsterAiLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(AiTickInterval, ct);

                    // 한 틱에서 예상치 못한 예외가 나도 루프 자체는 계속 돈다 - 그렇지 않으면 이 방의 몬스터 AI가
                    // 서버가 재시작될 때까지 영구히 멈춘다(fire-and-forget이라 예외를 받아줄 호출측도 없다).
                    try
                    {
                        TickMonsterAi((float)AiTickInterval.TotalSeconds);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GameServer] 몬스터 AI 틱 오류 ({_mapId}) : {ex}");
                    }
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
        private void TickMonsterAi(float deltaSeconds)
        {
            foreach (var runtime in _monsters.Values)
            {
                // 이 순회 도중 ApplyMonsterAttack이 처치해 목록에서 뺀 몬스터가 아직 보일 수 있다 -
                // 죽은 몬스터가 한 번 더 이동/공격하지 않도록 건너뛴다.
                if (runtime.Info.CurrentHp <= 0)
                {
                    continue;
                }

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

                BroadcastToAll(OpCode.Game_MonsterMoveBroadcast, broadcast.Encode());
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

        // 근접 사거리(공격 판정 자체는 서버 권위 - ApplyMonsterAttack/HandleMonsterAttackRequestAsync와
        // 같은 이유로 몬스터는 신뢰할 공격 요청 주체가 없다) 및 공격 쿨다운.
        private const float MeleeAttackRange = 1.5f;
        private const float AttackIntervalSeconds = 1.5f;

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

            if (runtime.AttackCooldownRemaining > 0f)
            {
                runtime.AttackCooldownRemaining -= deltaSeconds;
            }

            if (Distance(info.X, info.Z, target.X, target.Z) <= MeleeAttackRange)
            {
                // 사거리 안에 들어오면 더 붙지 않고 그 자리에서 대상을 바라보며 공격만 한다.
                info.RotationY = MathF.Atan2(target.X - info.X, target.Z - info.Z) * (180f / MathF.PI);

                if (runtime.AttackCooldownRemaining <= 0f)
                {
                    runtime.AttackCooldownRemaining = AttackIntervalSeconds;
                    AttackPlayer(runtime, target);
                }

                return true;
            }

            return MoveToward(info, target.X, target.Z, runtime.Point.ChaseSpeed, deltaSeconds);
        }

        // 근접 사거리 안에서 공격 쿨다운마다 호출된다. PvP(ApplyPlayerAttack)와 같은 TryDamagePlayer로
        // 서버가 최종 피해와 남은 체력을 계산하고, 클라이언트에는 그 결과만 보낸다(Player Health 영역 참고).
        private void AttackPlayer(MonsterRuntime runtime, PlayerInfo target)
        {
            if (!TryDamagePlayer(target, runtime.Info.AttackPower, out int finalDamage, out int remainingHp, out bool died))
            {
                return;
            }

            var broadcast = new S2CMonsterAttackBroadcast
            {
                MonsterId = runtime.Info.MonsterId,
                TargetPlayerId = target.PlayerId,
                Damage = finalDamage,
                RemainingHp = remainingHp,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            BroadcastToAll(OpCode.Game_MonsterAttackBroadcast, broadcast.Encode());

            if (died)
            {
                _ = ReviveAfterDelayAsync(target);
            }
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
