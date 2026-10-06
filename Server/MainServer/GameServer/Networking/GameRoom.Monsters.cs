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
    // 몬스터 전투/스폰: 플레이어의 몬스터 공격 적용(피해/처치/보상), 스폰/리스폰, MonsterRuntime.
    public partial class GameRoom
    {
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
            SendToViewersOfMonster(monsterId, OpCode.Game_MonsterDamageBroadcast, damageBroadcast.Encode(), alsoToPlayerId: attackerId);

            if (!killedByThisAttack)
            {
                return default;
            }

            // 골드/아이템 드롭은 경험치 지급 성공 여부(공격자 존재, 만렙 여부)와 무관하게 처치 자체에
            // 대한 보상이므로, 아래 조기 반환 분기들과 상관없이 항상 한 번만 굴려 결과에 실어 보낸다.
            (int gainedGold, List<(string ItemId, int Qty)> droppedItems) = DropTableCatalog.Roll(runtime.Info.MonsterType);

            // 이 몬스터를 보던 사람에게만 사망을 알리고 시야 목록에서 지운다 - 다음 틱이 "시야 이탈"로 오인해
            // 사망 연출 없이 즉시 지우는 알림(MonsterLeaveView)을 보내지 않게 하기 위함이다.
            var dieBroadcast = new S2CMonsterDieBroadcast { MonsterId = monsterId, Timestamp = timestamp };
            SendToViewersOfMonster(monsterId, OpCode.Game_MonsterDieBroadcast, dieBroadcast.Encode(), alsoToPlayerId: attackerId);
            ForgetMonsterInAllViews(monsterId);

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
                ApplyLevelUp(attackerEntry.Info, previousLevel);
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

            // 스폰만 한다. 근처 플레이어에게는 다음 틱의 시야 갱신이 "시야 진입"(Game_MonsterSpawnBroadcast)으로 알린다.
            SpawnMonsterAtPoint(point);
        }

        // 같은 포인트에서 maxAlive > 1로 여러 마리가 스폰될 때 한 좌표에 겹쳐 뭉치지 않도록,
        // 포인트 중심에서 이 반경 안의 원 안에 균등 분포로 스폰 좌표를 흩뿌린다.
        private const float SpawnJitterRadius = 1.5f;

        // point.Entries 중 하나를 Weight 비율로 골라 그 타입의 정의(MonsterDefinitionCatalog)로 몬스터를 만든다.
        // 리스폰마다 다시 호출되므로 같은 포인트에서도 스폰될 때마다 다른 타입이 나올 수 있다.
        private MonsterInfo SpawnMonsterAtPoint(MonsterSpawnPointDefinition point)
        {
            MonsterSpawnEntry entry = MonsterSpawnSelector.Pick(point.Entries, Random.Shared);
            MonsterDefinition definition = MonsterDefinitionCatalog.Get(entry.MonsterType);
            (float homeX, float homeZ) = ApplySpawnJitter(point.X, point.Z);

            var info = new MonsterInfo
            {
                MonsterId = MonsterIdGenerator.Next(),
                MonsterType = entry.MonsterType,
                MaxHp = definition.MaxHp,
                CurrentHp = definition.MaxHp,
                AttackPower = definition.AttackPower,
                Defense = definition.Defense,
                X = homeX,
                Y = point.Y,
                Z = homeZ,
                RotationY = point.RotationY,
                ExpReward = definition.ExpReward,
                PointId = point.PointId
            };

            _monsters[info.MonsterId] = new MonsterRuntime(info, point, definition, homeX, homeZ);
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

            // 스폰 시 선택된 타입의 정의(스탯/AI 튜닝). Point.Entries 중 어느 것이 뽑혔는지는 리스폰마다 달라질 수 있어
            // Point가 아니라 이 인스턴스에 따로 저장해둔다(AI 틱과 ApplyMonsterAttack이 참조).
            public MonsterDefinition Definition { get; }
            public int ExpReward => Definition.ExpReward;

            public MonsterAiState AiState { get; set; } = MonsterAiState.Idle;
            public long? TargetPlayerId { get; set; }

            // TickChasing이 근접 사거리 안에서 공격할 때마다 AttackIntervalSeconds로 리셋하고, 매 틱
            // deltaSeconds만큼 줄인다. 0 이하면 다음 틱에 바로 공격 가능.
            public float AttackCooldownRemaining { get; set; }

            // 공격 선딜(공격 시작 → 피해 판정) 남은 시간. 0보다 크면 공격 모션 중이라 제자리에서 대상을 바라보며 기다리다가
            // 0이 되는 틱에 판정한다(GameRoom.ResolveMonsterAttack). AttackWindupStartedAtUtcTicks는 이번 선딜이 시작된
            // 시각으로, 그 뒤에 대쉬한 플레이어를 "선딜 중 회피"로 인정하는 기준이다.
            public float AttackWindupRemaining { get; set; }
            public long AttackWindupStartedAtUtcTicks { get; set; }

            public MonsterRuntime(MonsterInfo info, MonsterSpawnPointDefinition point, MonsterDefinition definition, float homeX, float homeZ)
            {
                Info = info;
                Point = point;
                Definition = definition;
                HomeX = homeX;
                HomeZ = homeZ;
            }
        }
    }
}
