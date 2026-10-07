using System.Collections.Concurrent;
using GameServer.Items;
using GameServer.Skills;
using Shared;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // 액티브 스킬: 시전 승인(해금 레벨/쿨다운/마나/시전 잠금), 범위 안 대상 찾기, 시전 모션 중계.
    // 피해 적용(ApplyMonsterAttack)과 킬 보상 처리는 타격 시점에 ClientSession이 한다 - 보상 저장(KillRewardSaver)이 세션 쪽에 있기 때문이다.
    public partial class GameRoom
    {
        #region Method - Skills
        // 시전 모션이 끝나기 전에 다음 스킬 요청이 도착해도 정상 입력으로 보도록 시전 잠금에서 빼 주는 여유(초) - 패킷 지연/시계 오차 흡수용.
        private const double SkillCastLockToleranceSeconds = 0.1;

        public readonly record struct SkillCastResult(
            SkillCastStatus Status,
            SkillDefinition? Skill,
            float CooldownSeconds,
            WeaponKind Weapon,
            float X,
            float Y,
            float Z,
            float RotationY,
            long CastSerial = 0)
        {
            public bool Accepted => Status == SkillCastStatus.Accepted;
        }

        private sealed class SkillState
        {
            // 슬롯 -> 쿨다운이 끝나는 시각(UTC ticks). 무기를 바꿔도 같은 슬롯의 쿨다운은 이어진다 - 무기를 바꿔 쿨다운을 우회할 수 없다.
            public readonly Dictionary<int, long> CooldownUntilUtcTicks = new();
            public long CastLockUntilUtcTicks;

            // 시전마다 늘어난다. 대쉬로 시전이 취소되면 한 번 더 늘려, 아직 일어나지 않은 타격(ClientSession.RunSkillHitsAsync)이 자신의 시전이 아님을 알게 한다.
            public long CastSerial;
        }

        // 플레이어 id -> 스킬 상태. 시전 요청(세션 스레드)이 항목 단위로 lock한다.
        private readonly ConcurrentDictionary<long, SkillState> _skillStates = new();

        // 스킬 시전을 시도한다. 검사 순서: 방에 있음/살아 있음 -> 장착 무기 종류의 해당 슬롯 스킬 존재 -> 해금 레벨 -> 시전 잠금 -> 쿨다운 -> 마나.
        // 모두 통과하면 마나를 차감하고 쿨다운/시전 잠금을 기록한 뒤 Accepted를 돌려준다(거부되면 마나/쿨다운은 그대로).
        // 시전 위치는 서버가 아는 최신 위치이고 방향만 요청값(rotationY)을 쓴다.
        public SkillCastResult TryBeginSkillCast(long playerId, int slot, float rotationY)
        {
            SkillCastResult Rejected(SkillCastStatus status, SkillDefinition? skill = null, float cooldownSeconds = 0f, WeaponKind weapon = WeaponKind.None)
                => new(status, skill, cooldownSeconds, weapon, 0f, 0f, 0f, 0f);

            if (!float.IsFinite(rotationY) || !_players.TryGetValue(playerId, out var entry))
            {
                return Rejected(SkillCastStatus.InvalidRequest);
            }

            PlayerInfo info = entry.Info;
            if (info.CurrentHp <= 0)
            {
                return Rejected(SkillCastStatus.Dead);
            }

            WeaponKind weapon = ItemCatalog.GetWeaponKind(info.WeaponItemId);
            if (!SkillCatalog.TryGet(weapon, slot, out SkillDefinition? skill))
            {
                return Rejected(SkillCastStatus.NoSkill, weapon: weapon);
            }

            if (info.Level < skill.UnlockLevel)
            {
                return Rejected(SkillCastStatus.LevelTooLow, skill, weapon: weapon);
            }

            SkillState state = _skillStates.GetOrAdd(playerId, _ => new SkillState());
            long castSerial;
            long now = DateTime.UtcNow.Ticks;

            lock (state)
            {
                if (now < state.CastLockUntilUtcTicks)
                {
                    return Rejected(SkillCastStatus.Casting, skill, weapon: weapon);
                }

                if (state.CooldownUntilUtcTicks.TryGetValue(slot, out long cooldownUntil) && now < cooldownUntil)
                {
                    return Rejected(SkillCastStatus.OnCooldown, skill, (float)TimeSpan.FromTicks(cooldownUntil - now).TotalSeconds, weapon);
                }

                if (!TrySpendMana(playerId, skill.ManaCost))
                {
                    // 마나 부족과 사망(TrySpendMana가 같이 거른다)을 구분해 알린다.
                    return Rejected(info.CurrentHp <= 0 ? SkillCastStatus.Dead : SkillCastStatus.NotEnoughMana, skill, weapon: weapon);
                }

                state.CooldownUntilUtcTicks[slot] = now + TimeSpan.FromSeconds(skill.CooldownSeconds).Ticks;
                double castLock = Math.Max(0.0, skill.CastLockSeconds - SkillCastLockToleranceSeconds);
                state.CastLockUntilUtcTicks = now + TimeSpan.FromSeconds(castLock).Ticks;
                castSerial = ++state.CastSerial;
            }

            return new SkillCastResult(SkillCastStatus.Accepted, skill, skill.CooldownSeconds, weapon, info.X, info.Y, info.Z, rotationY, castSerial);
        }

        // 진행 중인 시전을 취소한다: 시전 잠금을 풀고, 아직 일어나지 않은 타격을 무효로 만든다(이미 일어난 타격은 그대로다).
        // 대쉬 성공 때(RegisterDash) 호출된다 - 회피 중심 전투라 시전 모션을 대쉬로 끊을 수 있어야 한다. 마나/쿨다운은 돌려주지 않는다.
        public void CancelSkillCast(long playerId)
        {
            if (!_skillStates.TryGetValue(playerId, out SkillState? state))
            {
                return;
            }

            lock (state)
            {
                state.CastLockUntilUtcTicks = 0;
                state.CastSerial++;
            }
        }

        // serial이 이 플레이어의 가장 최근 시전이고 취소되지 않았는지. 타격 직전에 확인한다.
        public bool IsCastCurrent(long playerId, long serial)
        {
            if (!_skillStates.TryGetValue(playerId, out SkillState? state))
            {
                return false;
            }

            lock (state)
            {
                return state.CastSerial == serial;
            }
        }

        // 승인된 시전을 이 플레이어를 보고 있는 사람에게 알린다(본인은 이미 로컬에서 재생했으므로 제외).
        public void BroadcastSkillCast(long playerId, int slot, SkillCastResult cast)
        {
            var broadcast = new S2CSkillCastBroadcast
            {
                PlayerId = playerId,
                Slot = slot,
                WeaponType = (int)cast.Weapon,
                X = cast.X,
                Y = cast.Y,
                Z = cast.Z,
                RotationY = cast.RotationY
            };
            SendToViewersOfPlayer(playerId, OpCode.Game_SkillCastBroadcast, broadcast.Encode(), excludeSelf: true);
        }

        // 스킬 범위 안에 있는 살아 있는 몬스터 id를 시전 위치에서 가까운 순으로 skill.MaxTargets마리까지 돌려준다.
        public List<long> FindSkillTargets(SkillDefinition skill, float originX, float originZ, float rotationY)
        {
            var hits = new List<(long MonsterId, float DistanceSquared)>();

            foreach (var (monsterId, runtime) in _monsters)
            {
                MonsterInfo info = runtime.Info;
                if (info.CurrentHp > 0 && SkillShapeTester.TryHit(skill, originX, originZ, rotationY, info.X, info.Z, out float distanceSquared))
                {
                    hits.Add((monsterId, distanceSquared));
                }
            }

            hits.Sort((a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));
            return hits.Take(skill.MaxTargets).Select(h => h.MonsterId).ToList();
        }

        // 한 번의 타격이 쓰는 공격력: 플레이어 공격력 x 스킬 배율(반올림, 최소 1). 방어력 차감은 ApplyMonsterAttack이 한다.
        public static int CalculateSkillAttackPower(int attackPower, SkillDefinition skill)
        {
            return Math.Max(1, (int)MathF.Round(attackPower * skill.DamageMultiplier));
        }
        #endregion
    }
}
