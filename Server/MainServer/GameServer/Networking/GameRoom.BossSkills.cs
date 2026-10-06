using GameServer.Combat;
using GameServer.Monsters;
using GameServer.Navigation;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // 보스 스킬: 체력 구간별로 범위 공격/돌진/소환을 고르고, 예고 -> 발동 -> 후딜의 순서로 실행한다.
    //
    // 흐름(BossSkillStage):
    //  Telegraph - 시전을 시작했다. 보스는 제자리에서 멈추고 클라이언트에 위험 범위를 예고한다(S2CBossSkillTelegraphBroadcast).
    //              이 시간(TelegraphSeconds)이 플레이어가 범위 밖으로 벗어나거나 대쉬로 피할 기회다.
    //  Charging  - (돌진만) 예고 시간이 끝나 보스가 예고된 방향으로 달려간다. 지나가는 경로 위의 플레이어를 판정한다.
    //  Recover   - 발동이 끝난 뒤 보스가 멈춰 있는 후딜. 이 동안 일반 공격도 하지 않는다(플레이어의 공략 기회).
    //
    // 판정은 서버가 예고가 끝나는 순간(돌진은 지나가는 순간)에 하며, 기존 몬스터 근접 공격과 같은 규칙(MonsterAttackJudge)을 쓴다:
    // 범위 안이고 대쉬 무적이 아니면 명중(Game_MonsterAttackBroadcast), 대쉬 무적이면 회피(Game_MonsterAttackDodgedBroadcast).
    public partial class GameRoom
    {
        #region Method - Boss Skills
        private enum BossSkillStage
        {
            Telegraph,
            Charging,
            Recover
        }

        // 시전 중인 스킬 하나의 진행 상태. 보스가 시전을 시작할 때 만들어지고 후딜이 끝나면 버려진다.
        private sealed class BossSkillExecution
        {
            public required string SkillId { get; init; }
            public required BossSkillDefinition Skill { get; init; }

            // 시전을 시작한 시각 - 대쉬 회피 판정(GetDashStatus)이 "예고 중에 대쉬했는가"를 가리는 기준이다.
            public long StartedAtUtcTicks { get; init; }

            public BossSkillStage Stage { get; set; } = BossSkillStage.Telegraph;
            public float StageRemaining { get; set; }

            // 범위 공격의 중심 / 돌진의 출발점. 시전을 시작한 순간의 보스 위치로 고정된다(예고된 위치가 그대로 위험 범위가 되도록).
            public float CenterX { get; init; }
            public float CenterZ { get; init; }

            // 돌진 방향(단위 벡터)과 남은 이동 거리. 방향은 시전 시작 때 대상 쪽으로 한 번 정해 고정한다(예고된 직선 그대로 달려간다).
            public float DirX { get; init; }
            public float DirZ { get; init; }
            public float TravelRemaining { get; set; }

            // 돌진 중 이미 판정을 받은 플레이어 - 한 번의 돌진에 한 사람이 여러 틱 겹쳐도 한 번만 맞거나 피한다.
            public HashSet<long> JudgedPlayers { get; } = new();

            // 소환 스킬의 하수인이 나타날 자리(예고 때 정해 알린 그대로 발동 때 나타난다).
            public List<(float X, float Z)> SummonPoints { get; } = new();
        }

        // 보스 몸집 대비 플레이어 판정 반경(m). 돌진 경로의 폭에 더해 "스친 것"을 인정하는 여유다.
        private const float PlayerHitRadius = 0.4f;

        // 범위 공격 판정에서 범위 밖이라도 이 거리(m) 안이면 "범위 바깥으로 피한 사람"으로 보고 대쉬로 벗어난 경우 회피 연출을 보낸다.
        // 멀리 떨어진 채 우연히 대쉬한 사람에게까지 회피 연출이 가지 않게 한다.
        private const float SlamNearbyMargin = 4f;

        // 하수인이 나타날 자리를 고를 때 보스에게 너무 붙지 않게 하는 최소 거리(m)와, 이동 가능 칸을 찾을 때 보는 최대 칸 수.
        private const float SummonMinDistanceFromBoss = 1.5f;
        private const int SummonSnapRing = 4;

        // 이 보스의 스킬 재사용 대기/스킬 간 간격 타이머를 한 틱만큼 줄인다. 시전 중이 아니어도(추적 중이 아니어도) 흐른다.
        private static void TickBossTimers(MonsterRuntime runtime, float deltaSeconds)
        {
            if (runtime.SkillIntervalRemaining > 0f)
            {
                runtime.SkillIntervalRemaining -= deltaSeconds;
            }

            if (runtime.SkillCooldowns.Count == 0)
            {
                return;
            }

            foreach (string name in runtime.SkillCooldowns.Keys.ToArray())
            {
                float remaining = runtime.SkillCooldowns[name] - deltaSeconds;
                if (remaining <= 0f)
                {
                    runtime.SkillCooldowns.Remove(name);
                }
                else
                {
                    runtime.SkillCooldowns[name] = remaining;
                }
            }
        }

        // 추적 중인 보스가 지금 쓸 스킬을 골라 시전을 시작한다. 시작했으면 true(그 틱은 일반 공격을 하지 않는다).
        private bool TryStartBossSkill(MonsterRuntime runtime, PlayerInfo target)
        {
            BossPatternDefinition pattern = runtime.Definition.BossPattern!;
            if (runtime.SkillIntervalRemaining > 0f)
            {
                return false;
            }

            MonsterInfo info = runtime.Info;
            int phase = BossPatternPlanner.PhaseIndexFor(pattern, info.CurrentHp, info.MaxHp);
            string? skillId = BossPatternPlanner.PickSkill(
                pattern, phase,
                name => runtime.SkillCooldowns.ContainsKey(name),
                Distance(info.X, info.Z, target.X, target.Z),
                CountSummonsOf(info.MonsterId),
                Random.Shared);

            if (skillId == null)
            {
                return false;
            }

            StartBossSkill(runtime, target, skillId, pattern.Skills[skillId]);
            return true;
        }

        private void StartBossSkill(MonsterRuntime runtime, PlayerInfo target, string skillId, BossSkillDefinition skill)
        {
            MonsterInfo info = runtime.Info;

            // 시전 중에는 기본 공격 선딜과 이동 경로를 접는다.
            runtime.AttackWindupRemaining = 0f;
            runtime.ClearPath();
            FaceTarget(info, target);

            // 방향은 시전 시작 시점의 대상 쪽(돌진용). 대상과 겹쳐 있으면 보스가 바라보던 방향을 쓴다.
            float dx = target.X - info.X;
            float dz = target.Z - info.Z;
            float length = MathF.Sqrt(dx * dx + dz * dz);
            (float dirX, float dirZ) = length > 1e-3f
                ? (dx / length, dz / length)
                : (MathF.Sin(info.RotationY * MathF.PI / 180f), MathF.Cos(info.RotationY * MathF.PI / 180f));

            var execution = new BossSkillExecution
            {
                SkillId = skillId,
                Skill = skill,
                StartedAtUtcTicks = DateTime.UtcNow.Ticks,
                StageRemaining = skill.TelegraphSeconds,
                CenterX = info.X,
                CenterZ = info.Z,
                DirX = dirX,
                DirZ = dirZ,
                TravelRemaining = skill.Length
            };

            if (skill.Type == BossSkillType.Charge)
            {
                info.RotationY = MathF.Atan2(dirX, dirZ) * (180f / MathF.PI);
            }

            if (skill.Type == BossSkillType.Summon)
            {
                PickSummonPoints(runtime, skill, execution.SummonPoints);
            }

            runtime.ActiveSkill = execution;

            var telegraph = new S2CBossSkillTelegraphBroadcast
            {
                MonsterId = info.MonsterId,
                SkillType = (byte)skill.Type,
                CenterX = execution.CenterX,
                CenterZ = execution.CenterZ,
                RotationY = info.RotationY,
                Radius = skill.Type == BossSkillType.AreaSlam ? skill.Radius : 0f,
                Width = skill.Type == BossSkillType.Charge ? skill.Width : 0f,
                Length = skill.Type == BossSkillType.Charge ? skill.Length : 0f,
                DurationMs = (int)MathF.Round(skill.TelegraphSeconds * 1000f),
                Points = execution.SummonPoints.Select(p => new BossSkillPoint { X = p.X, Z = p.Z }).ToList()
            };
            SendToViewersOfMonster(info.MonsterId, OpCode.Game_BossSkillTelegraphBroadcast, telegraph.Encode(), alsoToPlayerId: target.PlayerId);
        }

        // 하수인이 나타날 자리를 정한다. 보스 주변 SummonRadius 안에서 무작위로 뽑고, 활동 영역/이동 격자 안의 가장 가까운 설 수 있는 칸으로 옮긴다.
        private void PickSummonPoints(MonsterRuntime boss, BossSkillDefinition skill, List<(float X, float Z)> points)
        {
            int count = Math.Min(skill.SummonCount, skill.MaxAlive - CountSummonsOf(boss.Info.MonsterId));
            MonsterDefinition minion = MonsterDefinitionCatalog.Get(skill.SummonMonsterType);
            NavGrid? grid = NavGridCatalog.Get(_mapId, minion.AgentRadius);

            for (int i = 0; i < count; i++)
            {
                float angle = Random.Shared.NextSingle() * MathF.PI * 2f;
                float minDistance = Math.Min(SummonMinDistanceFromBoss, skill.SummonRadius);
                float distance = minDistance + Random.Shared.NextSingle() * (skill.SummonRadius - minDistance);

                (float x, float z) = boss.Point.ConstrainToArea(
                    boss.Info.X + MathF.Cos(angle) * distance,
                    boss.Info.Z + MathF.Sin(angle) * distance);

                // 가구/벽 안이면 가까운 빈 칸으로 옮긴다. 옮길 곳이 없으면 보스 자리를 쓴다(적어도 서 있을 수 있는 곳이다).
                if (grid != null)
                {
                    (x, z) = grid.TrySnapToWalkable(x, z, SummonSnapRing, out float snappedX, out float snappedZ)
                        ? (snappedX, snappedZ)
                        : (boss.Info.X, boss.Info.Z);
                }

                points.Add((x, z));
            }
        }

        // 시전 중인 스킬을 한 틱 진행한다. 이번 틱에 보스 위치가 바뀌었으면 true(돌진 중).
        private bool TickActiveBossSkill(MonsterRuntime runtime, float deltaSeconds)
        {
            // 보스가 다른 스레드(공격 처리)에서 취소/처치됐을 수 있으므로 한 번만 읽어 쓴다.
            BossSkillExecution? execution = runtime.ActiveSkill;
            if (execution == null)
            {
                return false;
            }

            switch (execution.Stage)
            {
                case BossSkillStage.Telegraph:
                    execution.StageRemaining -= deltaSeconds;
                    return execution.StageRemaining <= 0f && ExecuteBossSkill(runtime, execution);

                case BossSkillStage.Charging:
                    return TickCharge(runtime, execution, deltaSeconds);

                default:
                    execution.StageRemaining -= deltaSeconds;
                    if (execution.StageRemaining <= 0f)
                    {
                        FinishBossSkill(runtime);
                    }

                    return false;
            }
        }

        // 예고 시간이 끝났다 - 스킬이 발동한다. 위치가 바뀌었으면 true.
        private bool ExecuteBossSkill(MonsterRuntime runtime, BossSkillExecution execution)
        {
            BossSkillDefinition skill = execution.Skill;

            if (skill.CooldownSeconds > 0f)
            {
                runtime.SkillCooldowns[execution.SkillId] = skill.CooldownSeconds;
            }

            var end = new S2CBossSkillEndBroadcast { MonsterId = runtime.Info.MonsterId, SkillType = (byte)skill.Type, Executed = true };
            SendToViewersOfMonster(runtime.Info.MonsterId, OpCode.Game_BossSkillEndBroadcast, end.Encode());

            switch (skill.Type)
            {
                case BossSkillType.AreaSlam:
                    ResolveSlam(runtime, execution);
                    BeginRecover(execution);
                    return false;

                case BossSkillType.Charge:
                    // 돌진은 이 틱부터 달리기 시작한다(첫 이동은 다음 틱).
                    execution.Stage = BossSkillStage.Charging;
                    return false;

                case BossSkillType.Summon:
                    SpawnSummons(runtime, execution);
                    BeginRecover(execution);
                    return false;

                default:
                    BeginRecover(execution);
                    return false;
            }
        }

        private static void BeginRecover(BossSkillExecution execution)
        {
            execution.Stage = BossSkillStage.Recover;
            execution.StageRemaining = execution.Skill.RecoverSeconds;
        }

        // 후딜이 끝났다 - 다음 스킬까지 현재 체력 구간의 간격만큼 쉰다.
        private void FinishBossSkill(MonsterRuntime runtime)
        {
            runtime.ActiveSkill = null;

            BossPatternDefinition pattern = runtime.Definition.BossPattern!;
            int phase = BossPatternPlanner.PhaseIndexFor(pattern, runtime.Info.CurrentHp, runtime.Info.MaxHp);
            runtime.SkillIntervalRemaining = pattern.Phases[phase].SkillIntervalSeconds;
        }

        // 범위 공격 판정: 중심에서 Radius 안의 플레이어를 친다. 근처(범위 밖 SlamNearbyMargin 이내)에서 대쉬로 벗어난 사람은 회피 연출을 받는다.
        private void ResolveSlam(MonsterRuntime runtime, BossSkillExecution execution)
        {
            foreach (var (player, _) in _players.Values.ToArray())
            {
                if (player.CurrentHp <= 0)
                {
                    continue;
                }

                float distance = Distance(execution.CenterX, execution.CenterZ, player.X, player.Z);
                if (distance > execution.Skill.Radius + SlamNearbyMargin)
                {
                    continue;
                }

                bool isInRange = distance <= execution.Skill.Radius + MonsterAttackJudge.HitRangeTolerance;
                JudgeBossStrike(runtime, execution, player, isInRange);
            }
        }

        // 돌진을 한 틱 진행한다. 이동 가능한 곳까지만 달리고, 그 구간 위에 닿은 플레이어를 판정한다. 위치가 바뀌었으면 true.
        private bool TickCharge(MonsterRuntime runtime, BossSkillExecution execution, float deltaSeconds)
        {
            MonsterInfo info = runtime.Info;
            BossSkillDefinition skill = execution.Skill;

            float step = Math.Min(skill.Speed * deltaSeconds, execution.TravelRemaining);
            float previousX = info.X;
            float previousZ = info.Z;
            float nextX = previousX + execution.DirX * step;
            float nextZ = previousZ + execution.DirZ * step;

            // 벽/가구/방 영역 밖이면 거기서 멈춘다(벽에 부딪힌 것) - 이 경우에도 후딜은 그대로 적용된다.
            NavGrid? grid = NavGridCatalog.Get(_mapId, runtime.Definition.AgentRadius);
            bool blocked = step > 0f && (!runtime.Point.AllowsPosition(nextX, nextZ) || (grid != null && !grid.IsWalkableAt(nextX, nextZ)));

            bool moved = step > 0f && !blocked;
            if (moved)
            {
                info.X = nextX;
                info.Z = nextZ;
                execution.TravelRemaining -= step;
            }

            JudgeChargeHits(runtime, execution, previousX, previousZ, info.X, info.Z);

            if (blocked || execution.TravelRemaining <= 0.01f)
            {
                BeginRecover(execution);
            }

            return moved;
        }

        // 이번 틱에 보스가 지나간 선분과 플레이어의 거리로 돌진 판정을 한다. 한 번의 돌진에서 사람당 한 번만 판정한다.
        private void JudgeChargeHits(MonsterRuntime runtime, BossSkillExecution execution, float fromX, float fromZ, float toX, float toZ)
        {
            float reach = execution.Skill.Width / 2f + PlayerHitRadius;
            foreach (var (player, _) in _players.Values.ToArray())
            {
                if (player.CurrentHp <= 0 || execution.JudgedPlayers.Contains(player.PlayerId))
                {
                    continue;
                }

                if (BossPatternPlanner.DistanceToSegment(player.X, player.Z, fromX, fromZ, toX, toZ) > reach)
                {
                    continue;
                }

                execution.JudgedPlayers.Add(player.PlayerId);
                JudgeBossStrike(runtime, execution, player, isInRange: true);
            }
        }

        // 보스 스킬 한 방의 판정. 기존 근접 공격과 같은 규칙(MonsterAttackJudge)이라 대쉬 무적/선딜 중 대쉬가 똑같이 적용된다.
        private void JudgeBossStrike(MonsterRuntime runtime, BossSkillExecution execution, PlayerInfo target, bool isInRange)
        {
            GetDashStatus(target.PlayerId, execution.StartedAtUtcTicks, out bool isInvulnerable, out bool dashedDuringWindup);

            switch (MonsterAttackJudge.Judge(isInRange, isInvulnerable, dashedDuringWindup))
            {
                case MonsterAttackOutcome.Hit:
                    AttackPlayer(runtime, target, execution.Skill.DamageMultiplier);
                    break;

                case MonsterAttackOutcome.Dodged:
                    var dodged = new S2CMonsterAttackDodgedBroadcast
                    {
                        MonsterId = runtime.Info.MonsterId,
                        TargetPlayerId = target.PlayerId
                    };
                    SendToViewersOfMonster(runtime.Info.MonsterId, OpCode.Game_MonsterAttackDodgedBroadcast, dodged.Encode(), alsoToPlayerId: target.PlayerId);
                    break;
            }
        }

        // 예고된 자리에 하수인을 소환한다. 소환 직후 보스의 추적 대상을 바로 쫓게 한다. 클라이언트에는 다음 틱의 시야 갱신이
        // 일반 몬스터 스폰과 똑같이 Game_MonsterSpawnBroadcast로 알린다.
        private void SpawnSummons(MonsterRuntime boss, BossSkillExecution execution)
        {
            BossSkillDefinition skill = execution.Skill;
            MonsterDefinition definition = MonsterDefinitionCatalog.Get(skill.SummonMonsterType);

            foreach ((float x, float z) in execution.SummonPoints)
            {
                // 예고와 발동 사이에 다른 경로로 하수인이 늘었어도 최대 수를 넘지 않는다.
                if (CountSummonsOf(boss.Info.MonsterId) >= skill.MaxAlive)
                {
                    break;
                }

                var info = new MonsterInfo
                {
                    MonsterId = MonsterIdGenerator.Next(),
                    MonsterType = skill.SummonMonsterType,
                    MaxHp = definition.MaxHp,
                    CurrentHp = definition.MaxHp,
                    AttackPower = definition.AttackPower,
                    Defense = definition.Defense,
                    X = x,
                    Y = boss.Info.Y,
                    Z = z,
                    RotationY = boss.Info.RotationY,
                    ExpReward = 0,
                    PointId = boss.Point.PointId
                };

                var summoned = new MonsterRuntime(info, boss.Point, definition, x, z) { OwnerMonsterId = boss.Info.MonsterId };
                if (boss.TargetPlayerId is { } targetId)
                {
                    summoned.AiState = MonsterAiState.Chasing;
                    summoned.TargetPlayerId = targetId;
                }

                _monsters[info.MonsterId] = summoned;
            }
        }

        private int CountSummonsOf(long bossMonsterId)
        {
            int count = 0;
            foreach (MonsterRuntime runtime in _monsters.Values)
            {
                if (runtime.OwnerMonsterId == bossMonsterId && runtime.Info.CurrentHp > 0)
                {
                    count++;
                }
            }

            return count;
        }

        // 시전 중이던 스킬을 취소한다(보스 사망 등). 아직 예고 중이면 클라이언트가 위험 범위 표시를 걷도록 알린다.
        private void CancelBossSkill(MonsterRuntime runtime)
        {
            BossSkillExecution? execution = runtime.ActiveSkill;
            runtime.ActiveSkill = null;

            if (execution is { Stage: BossSkillStage.Telegraph })
            {
                var end = new S2CBossSkillEndBroadcast { MonsterId = runtime.Info.MonsterId, SkillType = (byte)execution.Skill.Type, Executed = false };
                SendToViewersOfMonster(runtime.Info.MonsterId, OpCode.Game_BossSkillEndBroadcast, end.Encode());
            }
        }

        // 보스가 쓰러졌다 - 거느리던 하수인도 함께 사라진다(보상 없이). 사망 연출 알림은 보내고 시야에서 지운다.
        private void DespawnSummonsOf(long bossMonsterId)
        {
            foreach (MonsterRuntime runtime in _monsters.Values.Where(m => m.OwnerMonsterId == bossMonsterId).ToArray())
            {
                long id = runtime.Info.MonsterId;
                runtime.Info.CurrentHp = 0;
                _monsters.TryRemove(id, out _);

                var die = new S2CMonsterDieBroadcast { MonsterId = id, Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                SendToViewersOfMonster(id, OpCode.Game_MonsterDieBroadcast, die.Encode());
                ForgetMonsterInAllViews(id);
            }
        }
        #endregion
    }
}
