using GameServer.Logging;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using GameServer.Combat;
using GameServer.Maps;
using GameServer.Monsters;
using GameServer.Navigation;
using Shared;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // 방 틱 루프와 몬스터 AI(인식/추적/공격 선딜/복귀).
    public partial class GameRoom
    {
        #region Method - Room Tick / Monster AI
        // 방 틱 주기(20Hz). 스냅샷 전송과 몬스터 AI 갱신이 이 주기로 돈다. 클라이언트 이동 전송(10Hz)보다 촘촘해 추가 지연이 작다.
        private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);
        private const float ArrivalThreshold = 0.1f;

        // 방 틱 루프(20Hz). 방이 생성되는 시점(첫 입장자)에 시작해 서버가 종료될 때까지 돈다. 틱마다
        // (1) 몬스터 AI를 갱신하고 (2) 받는 사람별 시야를 갱신한 뒤 시야 안에서 움직인 것만 스냅샷 하나로 묶어 보낸다(UpdateViewsAndSendSnapshots).
        // 개별 요청과 무관한 방의 백그라운드 작업이라 리스폰 타이머(RespawnAfterDelayAsync)와 같은 서버 전체 수명 토큰을 쓴다 -
        // 방은 비어도 제거되지 않고(MapRoomRegistry, 맵마다 하나) 이 루프도 멈추지 않지만, 플레이어가 없으면 감지 대상도 받을 사람도
        // 없어 순회 비용만 남는다(몬스터 수가 매우 적은 MVP 규모라 무시할 만하다).
        // PeriodicTimer는 처리 시간과 무관하게 주기를 유지하고(Task.Delay 반복처럼 밀리지 않는다), AI 이동량은 실제 경과 시간으로 계산한다.
        private async Task RunTickLoopAsync(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(TickInterval);
            var elapsed = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                while (await timer.WaitForNextTickAsync(ct))
                {
                    float deltaSeconds = (float)elapsed.Elapsed.TotalSeconds;
                    elapsed.Restart();

                    // 한 틱에서 예상치 못한 예외가 나도 루프 자체는 계속 돈다 - 그렇지 않으면 이 방의 몬스터 AI와 이동 전송이
                    // 서버가 재시작될 때까지 영구히 멈춘다(fire-and-forget이라 예외를 받아줄 호출측도 없다).
                    try
                    {
                        List<EntityTransform> movedMonsters = TickMonsterAi(deltaSeconds);
                        UpdateViewsAndSendSnapshots(CollectMovedPlayers(), movedMonsters);
                        RegenerateMana(deltaSeconds);
                    }
                    catch (Exception ex)
                    {
                        Log.LogError(ex, "방 틱 오류 ({MapId})", _mapId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 서버 종료로 인한 정상 취소.
            }
        }

        // 직전 틱 이후 이동한 플레이어(_movedPlayerIds)의 현재 위치를 모은다. 받는 사람별로 걸러 보내는 건 UpdateViewsAndSendSnapshots가 한다.
        private List<EntityTransform> CollectMovedPlayers()
        {
            var movedPlayers = new List<EntityTransform>();
            foreach (long playerId in _movedPlayerIds.Keys)
            {
                // 이번 틱에 읽은 id만 지운다 - 읽는 사이 새로 들어온 이동 표시는 다음 틱으로 넘어간다.
                _movedPlayerIds.TryRemove(playerId, out _);
                if (_players.TryGetValue(playerId, out var entry))
                {
                    PlayerInfo info = entry.Info;
                    movedPlayers.Add(new EntityTransform(playerId, info.X, info.Y, info.Z, info.RotationY));
                }
            }
            return movedPlayers;
        }

        // 몬스터 종류에 관계없이 공통으로 동작한다 - 몬스터별 분기 없이 MonsterDefinition의
        // DetectionRange/ChaseSpeed/LeashRange 값만으로 감지/추적/복귀를 판단하므로, 새 몬스터 타입을
        // 추가해도 이 로직은 수정할 필요가 없다. 이번 틱에 위치/방향이 바뀐 몬스터 목록을 돌려준다(스냅샷용).
        private List<EntityTransform> TickMonsterAi(float deltaSeconds)
        {
            var moved = new List<EntityTransform>();
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

                MonsterInfo info = runtime.Info;
                moved.Add(new EntityTransform(info.MonsterId, info.X, info.Y, info.Z, info.RotationY));
            }
            return moved;
        }

        // 몬스터 한 마리의 상태를 한 틱만큼 갱신한다. 위치가 실제로 바뀌었으면 true를 반환해
        // 호출측이 브로드캐스트를 보내게 한다(제자리 대기 중인 몬스터까지 매 틱 보낼 필요는 없다).
        private bool UpdateMonster(MonsterRuntime runtime, float deltaSeconds)
        {
            // 보스는 스킬 타이머를 매 틱 흘리고, 시전 중인 스킬이 있으면 그 진행이 일반 AI보다 우선한다(시전 중에는 제자리에서
            // 예고하거나 돌진하며, 추적/일반 공격을 하지 않는다). GameRoom.BossSkills.cs 참고.
            if (runtime.Definition.BossPattern != null)
            {
                TickBossTimers(runtime, deltaSeconds);

                if (runtime.ActiveSkill != null)
                {
                    return TickActiveBossSkill(runtime, deltaSeconds);
                }
            }

            switch (runtime.AiState)
            {
                case MonsterAiState.Idle:
                    // 길이 없어 방금 추적을 포기했다면 잠시 감지를 쉰다(MonsterRuntime.DetectionCooldownRemaining 참고).
                    if (runtime.DetectionCooldownRemaining > 0f)
                    {
                        runtime.DetectionCooldownRemaining -= deltaSeconds;
                        return false;
                    }

                    long? foundTargetId = FindNearestPlayerInRange(runtime.Info, runtime.Definition.DetectionRange, runtime.Point);
                    if (foundTargetId is { } targetId)
                    {
                        runtime.AiState = MonsterAiState.Chasing;
                        runtime.TargetPlayerId = targetId;
                        runtime.ClearPath();
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
        private static float MeleeAttackRange => CombatTuning.Current.MonsterMeleeRange;
        private static float AttackIntervalSeconds => CombatTuning.Current.MonsterAttackIntervalSeconds;

        // 공격 시작(Game_MonsterAttackStartBroadcast)부터 피해 판정까지의 선딜. 클라이언트 공격 모션(약 0.83초)의 중간쯤에 맞고,
        // 플레이어가 모션을 보고 대쉬(0.25초)로 반응할 시간을 준다. 쿨다운(AttackIntervalSeconds)은 선딜 시작 시점부터 센다.
        private static float MonsterAttackWindupSeconds => CombatTuning.Current.MonsterAttackWindupSeconds;

        private bool TickChasing(MonsterRuntime runtime, float deltaSeconds)
        {
            if (runtime.TargetPlayerId is not { } targetId
                || !_players.TryGetValue(targetId, out var targetEntry)
                || targetEntry.Info.CurrentHp <= 0)
            {
                GiveUpChase(runtime);
                return false;
            }

            MonsterInfo info = runtime.Info;
            PlayerInfo target = targetEntry.Info;

            // 대상이 활동 영역(방) 밖으로 나가면 벽 너머까지 쫓지 않고 포기한다(SpawnArea 주석 참고).
            float distanceFromSpawn = Distance(info.X, info.Z, runtime.HomeX, runtime.HomeZ);
            if (distanceFromSpawn > runtime.Definition.LeashRange || !runtime.Point.AllowsPosition(target.X, target.Z))
            {
                GiveUpChase(runtime);
                return false;
            }

            if (runtime.AttackCooldownRemaining > 0f)
            {
                runtime.AttackCooldownRemaining -= deltaSeconds;
            }

            // 보스는 일반 공격 선딜 중이 아닐 때 쓸 수 있는 스킬이 있으면 시전을 시작한다(범위 공격/돌진/소환).
            if (runtime.Definition.BossPattern != null && runtime.AttackWindupRemaining <= 0f && TryStartBossSkill(runtime, target))
            {
                return true;
            }

            // 선딜 중에는 이미 공격을 시작했으므로 도중에 대상이 사거리를 벗어나도 따라가지 않고 그 자리에서 기다렸다가
            // 선딜이 끝나는 틱에 판정한다(빗나가면 헛스윙).
            if (runtime.AttackWindupRemaining > 0f)
            {
                FaceTarget(info, target);
                runtime.AttackWindupRemaining -= deltaSeconds;

                if (runtime.AttackWindupRemaining <= 0f)
                {
                    runtime.AttackWindupRemaining = 0f;
                    ResolveMonsterAttack(runtime, target);
                }

                return true;
            }

            if (Distance(info.X, info.Z, target.X, target.Z) <= MeleeAttackRange)
            {
                // 사거리 안에 들어오면 더 붙지 않고 그 자리에서 대상을 바라보며 공격만 한다.
                FaceTarget(info, target);

                if (runtime.AttackCooldownRemaining <= 0f)
                {
                    runtime.AttackCooldownRemaining = AttackIntervalSeconds;
                    StartMonsterAttack(runtime, target);
                }

                return true;
            }

            bool moved = MoveWithNavigation(runtime, target.X, target.Z, deltaSeconds, out bool unreachable);
            if (unreachable)
            {
                // 대상에게 닿는 길이 없다(가구로 막힌 자리 등) - 벽에 부딪히며 제자리걸음하지 않고 포기한 뒤 잠시 감지를 쉰다.
                runtime.DetectionCooldownRemaining = UnreachableDetectionCooldownSeconds;
                GiveUpChase(runtime);
                return false;
            }

            return moved;
        }

        // 추적을 포기하고 스폰 지점으로 돌아가기 시작한다.
        private static void GiveUpChase(MonsterRuntime runtime)
        {
            runtime.TargetPlayerId = null;
            runtime.AttackWindupRemaining = 0f;
            runtime.ClearPath();
            runtime.AiState = MonsterAiState.Returning;
        }

        private static void FaceTarget(MonsterInfo info, PlayerInfo target)
        {
            info.RotationY = MathF.Atan2(target.X - info.X, target.Z - info.Z) * (180f / MathF.PI);
        }

        // 사거리 안에서 쿨다운이 끝나면 호출된다. 피해는 바로 주지 않고 공격 시작만 알린 뒤 선딜(MonsterAttackWindupSeconds)이
        // 끝나는 틱에 ResolveMonsterAttack이 판정한다 - 그 사이에 플레이어가 대쉬로 피할 수 있다.
        private void StartMonsterAttack(MonsterRuntime runtime, PlayerInfo target)
        {
            runtime.AttackWindupRemaining = MonsterAttackWindupSeconds;
            runtime.AttackWindupStartedAtUtcTicks = DateTime.UtcNow.Ticks;

            var start = new S2CMonsterAttackStartBroadcast
            {
                MonsterId = runtime.Info.MonsterId,
                TargetPlayerId = target.PlayerId
            };

            SendToViewersOfMonster(runtime.Info.MonsterId, OpCode.Game_MonsterAttackStartBroadcast, start.Encode(), alsoToPlayerId: target.PlayerId);
        }

        // 선딜이 끝난 순간의 판정. 대상이 이미 죽었으면 아무 일도 없다. 규칙은 MonsterAttackJudge가 정한다:
        // 대쉬 무적이거나 선딜 도중 대쉬로 사거리를 벗어나면 회피(피해 없음, 회피 연출 알림), 그냥 사거리를 벗어났으면 헛스윙,
        // 사거리 안이고 무적이 아니면 명중(AttackPlayer).
        private void ResolveMonsterAttack(MonsterRuntime runtime, PlayerInfo target)
        {
            if (target.CurrentHp <= 0)
            {
                return;
            }

            MonsterInfo info = runtime.Info;
            bool isInRange = Distance(info.X, info.Z, target.X, target.Z) <= MeleeAttackRange + MonsterAttackJudge.HitRangeTolerance;
            GetDashStatus(target.PlayerId, runtime.AttackWindupStartedAtUtcTicks, out bool isInvulnerable, out bool dashedDuringWindup);

            switch (MonsterAttackJudge.Judge(isInRange, isInvulnerable, dashedDuringWindup))
            {
                case MonsterAttackOutcome.Hit:
                    AttackPlayer(runtime, target);
                    break;

                case MonsterAttackOutcome.Dodged:
                    var dodged = new S2CMonsterAttackDodgedBroadcast
                    {
                        MonsterId = info.MonsterId,
                        TargetPlayerId = target.PlayerId
                    };
                    SendToViewersOfMonster(info.MonsterId, OpCode.Game_MonsterAttackDodgedBroadcast, dodged.Encode(), alsoToPlayerId: target.PlayerId);
                    break;
            }
        }

        // 판정 결과가 명중일 때 호출된다. PvP(ApplyPlayerAttack)와 같은 TryDamagePlayer로
        // 서버가 최종 피해와 남은 체력을 계산하고, 클라이언트에는 그 결과만 보낸다(Player Health 영역 참고).
        // damageMultiplier는 보스 스킬 명중용(공격력 x 배율). 일반 근접 공격은 1이다.
        private void AttackPlayer(MonsterRuntime runtime, PlayerInfo target, float damageMultiplier = 1f)
        {
            int rawDamage = (int)MathF.Round(runtime.Info.AttackPower * damageMultiplier);
            if (!TryDamagePlayer(target, rawDamage, out int finalDamage, out int remainingHp, out bool died))
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

            SendToViewersOfMonster(runtime.Info.MonsterId, OpCode.Game_MonsterAttackBroadcast, broadcast.Encode(), alsoToPlayerId: target.PlayerId);

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

            bool moved = MoveWithNavigation(runtime, runtime.HomeX, runtime.HomeZ, deltaSeconds, out bool unreachable);

            // 집까지 길이 없으면(이동 격자 기준) 영영 못 돌아가 제자리에 갇히므로, 그냥 집으로 옮긴다.
            if (unreachable || Distance(info.X, info.Z, runtime.HomeX, runtime.HomeZ) <= ArrivalThreshold)
            {
                info.X = runtime.HomeX;
                info.Z = runtime.HomeZ;
                info.RotationY = runtime.Point.RotationY;
                runtime.ClearPath();
                runtime.AiState = MonsterAiState.Idle;
                return true;
            }

            return moved;
        }

        // 다음 경로 재계산까지의 간격. 대상(플레이어)이 계속 움직이므로 경로를 주기적으로 다시 찾는다.
        private const float PathRepathIntervalSeconds = 0.4f;

        // 대상에게 가는 길이 없어 추적을 포기한 뒤 감지를 쉬는 시간.
        private const float UnreachableDetectionCooldownSeconds = 2f;

        // (targetX, targetZ)를 향해 ChaseSpeed로 한 틱만큼 이동한다. 이동 격자가 있는 맵(던전)에서는 가구/벽을 돌아가는 경로를
        // 따라가고, 격자가 없는 맵(필드)에서는 기존처럼 직선으로 간다. 대상까지 길이 없으면 unreachable=true를 돌려준다.
        private bool MoveWithNavigation(MonsterRuntime runtime, float targetX, float targetZ, float deltaSeconds, out bool unreachable)
        {
            unreachable = false;
            MonsterInfo info = runtime.Info;
            float speed = runtime.Definition.ChaseSpeed;

            NavGrid? grid = NavGridCatalog.Get(_mapId, runtime.Definition.AgentRadius);
            if (grid == null)
            {
                return MoveToward(info, targetX, targetZ, speed, deltaSeconds);
            }

            Func<float, float, bool> allowed = runtime.Point.AllowsPosition;

            // 장애물 없이 일직선으로 갈 수 있으면 경로 탐색 없이 곧장 간다(대부분의 틱이 여기서 끝난다).
            if (grid.HasLineOfSight(info.X, info.Z, targetX, targetZ, allowed))
            {
                runtime.ClearPath();
                return MoveToward(info, targetX, targetZ, speed, deltaSeconds);
            }

            runtime.RepathCooldown -= deltaSeconds;
            if (runtime.Path == null || runtime.RepathCooldown <= 0f)
            {
                if (!grid.TryFindPath(info.X, info.Z, targetX, targetZ, allowed, out List<(float X, float Z)> path))
                {
                    runtime.ClearPath();
                    unreachable = true;
                    return false;
                }

                runtime.Path = path;
                runtime.PathIndex = 0;
                runtime.RepathCooldown = PathRepathIntervalSeconds;
            }

            // 이미 도착한 경유점은 건너뛰고 다음 경유점으로 향한다.
            while (runtime.PathIndex < runtime.Path.Count
                   && Distance(info.X, info.Z, runtime.Path[runtime.PathIndex].X, runtime.Path[runtime.PathIndex].Z) <= ArrivalThreshold)
            {
                runtime.PathIndex++;
            }

            if (runtime.PathIndex >= runtime.Path.Count)
            {
                // 경로의 끝까지 왔다 - 다음 틱에 직진하거나 새 경로를 찾는다.
                runtime.ClearPath();
                return false;
            }

            (float waypointX, float waypointZ) = runtime.Path[runtime.PathIndex];
            return MoveToward(info, waypointX, waypointZ, speed, deltaSeconds);
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

        // 활동 영역이 있는 포인트의 몬스터는 그 영역 안의 플레이어만 감지한다.
        private long? FindNearestPlayerInRange(MonsterInfo monster, float range, MonsterSpawnPointDefinition point)
        {
            long? nearestId = null;
            float nearestDistanceSquared = range * range;

            foreach (var (info, _) in _players.Values)
            {
                if (info.CurrentHp <= 0 || !point.AllowsPosition(info.X, info.Z))
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
