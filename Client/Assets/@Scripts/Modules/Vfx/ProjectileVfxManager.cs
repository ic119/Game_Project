using System.Collections.Generic;
using Incheol.Controller;
using Incheol.Models.Define;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Modules
{
    /// <summary>
    /// 원거리 무기(완드)의 공격 연출: 지팡이 끝에서 시전 이펙트를 터뜨리고, 투사체 이펙트를 대상까지 날려 보낸 뒤 도착하면 명중 이펙트를 재생한다.
    /// 순수 연출이다 - 피해 판정은 서버가 공격 요청(Game_MonsterAttackRequest 등)을 받는 즉시 한다. 그래서 투사체가 대상에게 닿는 시각과
    /// 서버 판정 시각이 거의 같도록 빠르게 날린다(BoltSpeed). 투사체는 풀(ObjectPoolManager)에서 대여해 매 프레임 직접 움직이고(물리 없음),
    /// 도착하면 풀에 돌려준다. 로컬 플레이어(PlayerAttackController)와 다른 플레이어(RemoteCharacterController)가 같은 경로를 쓴다.
    /// </summary>
    public class ProjectileVfxManager : SingletonObject<ProjectileVfxManager>
    {
        #region Variable
        // 투사체 속도(m/s). 사거리 4m 기준 약 0.16초 만에 도착한다 - 서버 판정(피해 숫자/피격 모션)이 오는 시점과 비슷해지도록 빠르게 둔다.
        private const float BoltSpeed = 25f;

        // 대상의 발 위치에서 이만큼 위(몸통 높이)를 조준한다.
        private const float AimHeight = 1.0f;

        // 대상이 없을 때(허공에 쏠 때) 투사체가 날아가는 거리(m). 완드 사거리와 같다.
        public const float DefaultFlightDistance = 4f;

        // 지팡이 끝(시전 이펙트/투사체가 나가는 지점)의 캐릭터 기준 로컬 오프셋(x 오른쪽, y 위, z 앞). 로컬 플레이어(PlayerAttackController.wandMuzzleOffset)와
        // 같은 기본값이며, 다른 플레이어 화면에서는 이 값을 쓴다.
        public static readonly Vector3 DefaultMuzzleOffset = new Vector3(0f, 1.3f, 0.6f);

        /// <summary>소유자가 없는 투사체(기본 공격)를 뜻하는 값.</summary>
        public const long NoOwner = long.MinValue;

        private const float MinTravelSeconds = 0.05f;
        private const int MaxActive = 24;

        private sealed class Bolt
        {
            public GameObject Instance;
            public WeaponType WeaponType;
            public Vector3 Start;
            public Vector3 End;
            public Transform Target;

            // 대상을 향해 쏜 투사체인지(도착 때 명중 이펙트를 재생할지). Target은 도중에 사라질 수 있어 따로 기억한다.
            public bool HasTarget;

            // 스킬 투사체를 쏜 시전자 id(취소용). 기본 공격 투사체는 NoOwner다.
            public long OwnerId = NoOwner;
            public float StartTime;
            public float Duration;
        }

        private readonly List<Bolt> active = new();
        #endregion

        #region LifeCycle
        private void LateUpdate()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Bolt bolt = active[i];

                // 대상이 움직이면 도착 지점도 따라간다. 대상이 사라졌으면(사망/이탈) 마지막으로 알던 지점까지 그대로 날아간다.
                if (bolt.Target != null)
                {
                    bolt.End = bolt.Target.position + Vector3.up * AimHeight;
                }

                float t = bolt.Duration > 0f ? (Time.time - bolt.StartTime) / bolt.Duration : 1f;
                if (t >= 1f)
                {
                    Arrive(bolt);
                    active.RemoveAt(i);
                    continue;
                }

                Vector3 position = Vector3.Lerp(bolt.Start, bolt.End, t);
                Vector3 direction = bolt.End - position;
                bolt.Instance.transform.SetPositionAndRotation(position, direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction) : bolt.Instance.transform.rotation);
            }
        }
        #endregion

        #region Method
        /// <summary>
        /// 원거리 공격 한 번을 연출한다. muzzle(지팡이 끝)에서 시전 이펙트를 재생하고, 투사체를 target(없으면 forward 방향 DefaultFlightDistance)까지 날린다.
        /// target이 있으면 도착했을 때 그 위치에 명중 이펙트를 재생한다(없으면 허공이라 명중 이펙트가 없다).
        /// </summary>
        public void FireRanged(WeaponType weaponType, Vector3 muzzle, Vector3 forward, Transform target)
        {
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            WeaponVfxManager.Instance?.PlaySwingEffect(weaponType, muzzle, Quaternion.LookRotation(forward));
            LaunchBolt(weaponType, muzzle, forward.normalized, target);
        }

        /// <summary>
        /// 액티브 스킬의 투사체 하나를 start에서 end까지 travelSeconds 동안 날린다(SkillVfxManager가 부른다). 무기 타입이 아니라 이펙트 키를 직접 받고,
        /// 도착하면 풀에 돌려주기만 한다 - 명중 이펙트는 스킬 설정의 areaHit이 같은 시각에 따로 재생한다.
        /// </summary>
        public void FireSkillProjectile(AddressableAssetKey key, Vector3 start, Vector3 end, float travelSeconds, float scale = 1f, long ownerId = NoOwner)
        {
            if (key == AddressableAssetKey.None || ObjectPoolManager.Instance == null)
            {
                return;
            }

            // 한꺼번에 너무 많이 날아다니면 가장 오래된 것부터 걷는다.
            if (active.Count >= MaxActive)
            {
                ObjectPoolManager.Instance.Release(active[0].Instance);
                active.RemoveAt(0);
            }

            Vector3 direction = end - start;
            GameObject instance = ObjectPoolManager.Instance.Get(key.ToString(), start, direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction) : Quaternion.identity);
            if (instance == null)
            {
                return;
            }

            // 풀에서 돌려받은 인스턴스는 프리팹 원본 크기로 초기화되어 있으므로, 배율은 그 위에 곱한다.
            if (!Mathf.Approximately(scale, 1f))
            {
                instance.transform.localScale *= scale;
            }

            active.Add(new Bolt
            {
                Instance = instance,
                WeaponType = WeaponType.None,
                Start = start,
                End = end,
                Target = null,
                HasTarget = false,
                OwnerId = ownerId,
                StartTime = Time.time,
                Duration = Mathf.Max(MinTravelSeconds, travelSeconds)
            });
        }

        /// <summary>
        /// ownerId가 쏜 스킬 투사체 중 아직 날아가는 것을 걷는다(대쉬로 스킬이 끊겼을 때 - 서버가 남은 타격을 취소하므로 도착해도 아무 일도 없다).
        /// </summary>
        public void CancelSkillProjectiles(long ownerId)
        {
            if (ownerId == NoOwner)
            {
                return;
            }

            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].OwnerId != ownerId)
                {
                    continue;
                }

                if (active[i].Instance != null)
                {
                    ObjectPoolManager.Instance?.Release(active[i].Instance);
                }

                active.RemoveAt(i);
            }
        }

        /// <summary>
        /// 패킷에 실린 목표(종류 + id)를 월드의 Transform으로 바꾼다. 몬스터/다른 플레이어/(다른 플레이어가 나를 노린 경우) 로컬 플레이어를 찾는다.
        /// 대상이 아직 스폰되지 않았거나 이미 사라졌으면 null - 호출측은 허공 발사로 처리한다.
        /// </summary>
        public static Transform ResolveTarget(AttackTargetKind kind, long targetId)
        {
            switch (kind)
            {
                case AttackTargetKind.Monster:
                    return RemoteMonsterManager.Instance != null && RemoteMonsterManager.Instance.TryGetRemoteMonster(targetId, out RemoteMonsterController monster)
                        ? monster.transform
                        : null;

                case AttackTargetKind.Player:
                    if (SaveDataManager.Instance != null && SaveDataManager.Instance.SelectedCharacterId == targetId)
                    {
                        PlayerMoveController local = FindAnyObjectByType<PlayerMoveController>();
                        return local != null ? local.transform : null;
                    }

                    return RemotePlayerManager.Instance != null && RemotePlayerManager.Instance.TryGetRemotePlayer(targetId, out RemoteCharacterController remote)
                        ? remote.transform
                        : null;

                default:
                    return null;
            }
        }

        private void LaunchBolt(WeaponType weaponType, Vector3 start, Vector3 forward, Transform target)
        {
            if (WeaponVfxManager.Instance == null || ObjectPoolManager.Instance == null
                || !WeaponVfxManager.Instance.TryGetProjectileKey(weaponType, out AddressableAssetKey key))
            {
                return;
            }

            // 한꺼번에 너무 많이 날아다니면(여럿이 동시에 연타) 가장 오래된 것부터 걷는다.
            if (active.Count >= MaxActive)
            {
                ObjectPoolManager.Instance.Release(active[0].Instance);
                active.RemoveAt(0);
            }

            Vector3 end = target != null ? target.position + Vector3.up * AimHeight : start + forward * DefaultFlightDistance;
            GameObject instance = ObjectPoolManager.Instance.Get(key.ToString(), start, Quaternion.LookRotation(end - start));
            if (instance == null)
            {
                return;
            }

            float distance = Vector3.Distance(start, end);
            active.Add(new Bolt
            {
                Instance = instance,
                WeaponType = weaponType,
                Start = start,
                End = end,
                Target = target,
                HasTarget = target != null,
                StartTime = Time.time,
                Duration = Mathf.Max(MinTravelSeconds, distance / BoltSpeed)
            });
        }

        // 투사체가 도착했다: 풀에 돌려주고, 대상을 향해 쐈다면 그 자리에 명중 이펙트를 재생한다.
        private void Arrive(Bolt bolt)
        {
            ObjectPoolManager.Instance?.Release(bolt.Instance);

            if (bolt.HasTarget)
            {
                WeaponVfxManager.Instance?.PlayImpactEffect(bolt.WeaponType, bolt.End, Quaternion.identity);
            }
        }

        // 씬이 사라지면 날아가던 투사체를 풀에 돌려준다(풀은 씬 전환에도 남아 있어, 그대로 두면 활성 상태로 허공에 남는다).
        protected override void OnDestroy()
        {
            if (ObjectPoolManager.Instance != null)
            {
                foreach (Bolt bolt in active)
                {
                    if (bolt.Instance != null)
                    {
                        ObjectPoolManager.Instance.Release(bolt.Instance);
                    }
                }
            }

            active.Clear();
            base.OnDestroy();
        }
        #endregion
    }
}
