using System;
using System.Collections;
using System.Collections.Generic;
using Incheol.Controller;
using Incheol.Models.Define;
using Incheol.Models.SO;
using Incheol.Utils;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Incheol.Modules
{
    /// <summary>
    /// 액티브 스킬 이펙트의 진입점. 스킬이 시전되면(내 스킬은 PlayerSkillController, 다른 플레이어 스킬은 RemoteCharacterController가 호출한다)
    /// SkillVfxDatabaseSO에서 그 스킬의 이펙트 설정을 찾아 세 시점에 재생한다.
    ///  - cast      : 시전 순간, 시전자 곁(휘두르는 궤적 등)
    ///  - areaStart : 시전 순간, 범위 중심(마법진 등 미리 보이는 연출)
    ///  - areaHit   : 타격 시각마다, 범위 중심(폭발/낙뢰 등) - 연타 스킬은 타격마다 재생한다
    /// 범위 중심과 타격 시각은 SkillTable(서버 SkillDefinitions.json의 복제본)에서 얻는다. 범위 이펙트는 설정에 따라 스킬 범위 크기에 맞춰
    /// 크기가 자동으로 조정된다. 순수한 연출이다 - 피해 판정은 서버가 하고, 이펙트 위치/시각이 서버와 어긋나도 판정에는 영향이 없다.
    /// 실제 스폰/재사용은 ObjectPoolManager + PooledEffect가 담당한다. WeaponVfxManager와 같은 방식으로 SO를 Addressables 고정 이름으로 로드한다.
    /// </summary>
    public class SkillVfxManager : SingletonObject<SkillVfxManager>
    {
        private const string DatabaseAddressableName = "SkillVfxDatabaseSO";

        protected override bool PersistAcrossScenes => true;

        private SkillVfxDatabaseSO database;

        /// <summary>내 시전을 뜻하는 시전자 id(원격 플레이어 id는 모두 양수라 겹치지 않는다).</summary>
        public const long LocalCasterId = 0;

        // 진행 중인 시전 하나의 상태. 시전자마다 가장 최근 시전만 추적한다. 대쉬로 끊기면 cancelled가 켜져 아직 일어나지 않은 이펙트(명중 이펙트, 투사체 출발)가
        // 만들어지지 않고, 이미 만들어 둔 시전/마법진 이펙트는 거둔다 - 서버가 남은 타격을 취소하는 것과 맞춘다.
        private sealed class ActiveCast
        {
            public bool Cancelled;

            // 이 시각(Time.time) 이후에는 남은 타격이 없다(마지막 타격은 시전 잠금 안에 끝난다). 그 뒤의 취소는 무시한다.
            public float EndTime;

            // 시전 순간에 만든 이펙트(시전자 곁/범위 중심)와 그때의 대여 번호. 번호가 달라졌다면 이미 반환돼 다른 곳에 쓰이는 인스턴스라 건드리지 않는다.
            public readonly List<(PooledEffect Effect, int LeaseId)> Spawned = new();
        }

        private readonly Dictionary<long, ActiveCast> activeCasts = new();

        // 내가 쓴 스킬의 바닥 범위 표시(한 번에 하나). 번호는 취소/대체될 때마다 올려, 풀에서 비동기로 빌리는 중인 표시가 뒤늦게 나타나지 않게 한다.
        private BossTelegraphIndicator activeTelegraph;
        private Coroutine telegraphCompletion;
        private int telegraphVersion;

        protected override void Awake()
        {
            base.Awake();
            LoadDatabase();
        }

        /// <summary>
        /// 이 스킬을 쓴 뒤 기본공격을 막는 시간(초). 시전 잠금과 이펙트 설정의 attackLockSeconds 중 긴 쪽이다.
        /// 데이터베이스가 아직 없거나 등록되지 않은 스킬이면 시전 잠금만 쓴다.
        /// </summary>
        public float GetAttackLockSeconds(SkillTable.Entry skill)
        {
            if (database != null && database.TryGetEntry(skill.Id, out SkillVfxEntry entry))
            {
                return Mathf.Max(skill.CastLockSeconds, entry.attackLockSeconds);
            }

            return skill.CastLockSeconds;
        }

        /// <summary>
        /// 스킬 시전 이펙트를 재생한다.
        /// </summary>
        /// <param name="origin">시전자 발 위치(월드).</param>
        /// <param name="rotation">시전 방향(시전자가 바라보는 방향). 수평 성분(yaw)만 쓴다.</param>
        /// <param name="showTelegraph">이 스킬이 바닥 범위 표시 대상이면 그릴지. 내가 쓴 스킬만 true로 부른다.</param>
        /// <param name="casterId">시전자 id(내 시전은 LocalCasterId). CancelSkill로 이 시전자의 진행 중인 이펙트를 거둘 때 쓴다.</param>
        public void PlaySkill(SkillTable.Entry skill, Vector3 origin, Quaternion rotation, bool showTelegraph = false, long casterId = LocalCasterId)
        {
            // database가 아직 로드되지 않았거나 이 스킬에 등록된 이펙트가 없으면 조용히 건너뛴다 - 이펙트는 연출일 뿐이다.
            if (database == null || ObjectPoolManager.Instance == null || !database.TryGetEntry(skill.Id, out SkillVfxEntry entry))
            {
                return;
            }

            Quaternion yaw = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
            SkillTable.Area area = skill.Area;
            Vector3 areaCenter = origin + yaw * (Vector3.forward * area.CenterForwardDistance);

            // 내 새 시전은 이전 시전의 범위 표시를 대체한다(다른 플레이어의 시전은 내 범위 표시를 건드리지 않는다).
            if (showTelegraph)
            {
                CancelTelegraph();
            }

            // 이 시전자의 이전 시전은 새 시전으로 대체된다(서버도 새 시전이 남은 타격을 무효로 만든다).
            var cast = new ActiveCast { EndTime = Time.time + skill.CastLockSeconds };
            activeCasts[casterId] = cast;

            RecordSpawn(cast, SpawnLayer(entry.cast, origin, yaw, area));
            RecordSpawn(cast, SpawnLayer(entry.areaStart, areaCenter, yaw, area));

            if (showTelegraph && entry.showTelegraph)
            {
                ShowTelegraph(area, origin, areaCenter, yaw);
            }

            if (entry.projectile != null && entry.projectile.key != AddressableAssetKey.None)
            {
                StartCoroutine(LaunchProjectile(entry, origin, yaw, area, cast, casterId));
            }

            if (entry.areaHit != null && entry.areaHit.key != AddressableAssetKey.None)
            {
                StartCoroutine(PlayHits(entry.areaHit, areaCenter, yaw, area, cast));
            }
        }

        /// <summary>
        /// casterId의 진행 중인 시전 이펙트를 거둔다: 아직 일어나지 않은 명중 이펙트/투사체 출발을 막고, 날아가는 투사체와 시전 순간에 만든 이펙트를 걷는다.
        /// 대쉬로 시전이 끊겼을 때 부른다(서버도 대쉬가 승인되면 남은 타격을 취소한다). 시전 잠금이 이미 끝난 뒤라면 남은 타격이 없으므로 아무것도 하지 않는다.
        /// 내 바닥 범위 표시(CancelTelegraph)는 따로 거둔다.
        /// </summary>
        public void CancelSkill(long casterId)
        {
            if (!activeCasts.Remove(casterId, out ActiveCast cast) || Time.time >= cast.EndTime)
            {
                return;
            }

            cast.Cancelled = true;
            ProjectileVfxManager.Instance?.CancelSkillProjectiles(casterId);

            foreach ((PooledEffect effect, int leaseId) in cast.Spawned)
            {
                // 이미 스스로 반환됐거나 다른 곳에 다시 대여된 인스턴스(번호가 다름)는 건드리지 않는다.
                if (effect != null && effect.gameObject.activeInHierarchy && effect.LeaseId == leaseId)
                {
                    ObjectPoolManager.Instance?.Release(effect.gameObject);
                }
            }

            cast.Spawned.Clear();
        }

        private static void RecordSpawn(ActiveCast cast, GameObject instance)
        {
            if (instance != null && instance.TryGetComponent(out PooledEffect effect))
            {
                cast.Spawned.Add((effect, effect.LeaseId));
            }
        }

        // 지팡이 끝에서 범위의 끝까지 투사체를 날린다. 첫 타격 시각에 도착하도록 출발 시각을 맞춘다(거리/속도만큼 날아가므로 그만큼 늦게 출발).
        private IEnumerator LaunchProjectile(SkillVfxEntry entry, Vector3 origin, Quaternion yaw, SkillTable.Area area, ActiveCast cast, long casterId)
        {
            SkillVfxLayer layer = entry.projectile;
            Vector3 start = origin + yaw * layer.offset;

            // 도착 지점: 직선은 끝점, 원은 중심. 높이는 발사 지점과 같다.
            float distance = area.Shape == SkillTable.AreaShape.Circle ? area.ForwardOffset : area.Length;
            Vector3 end = origin + yaw * new Vector3(0f, layer.offset.y, distance);

            float travel = Vector3.Distance(start, end) / Mathf.Max(1f, entry.projectileSpeed);
            float hitTime = area.HitDelaySeconds;

            // 타격 시각이 비행 시간보다 짧으면 바로 출발하고 타격 시각에 도착하도록 더 빨리 날린다. 길면 그만큼 늦게 출발한다.
            float launchDelay = Mathf.Max(0f, hitTime - travel);
            travel = hitTime > 0f ? Mathf.Min(travel, hitTime) : travel;

            if (launchDelay > 0f)
            {
                yield return new WaitForSeconds(launchDelay);
            }

            // 출발 전에 대쉬로 시전이 끊겼다면 쏘지 않는다.
            if (cast.Cancelled)
            {
                yield break;
            }

            ProjectileVfxManager.Instance?.FireSkillProjectile(layer.key, start, end, travel, layer.scale, casterId);
        }

        /// <summary>
        /// 진행 중인 바닥 범위 표시를 거둔다(대쉬로 시전이 끊겼거나 서버가 시전을 거부했을 때, 또는 새 시전이 시작될 때).
        /// </summary>
        public void CancelTelegraph()
        {
            telegraphVersion++;

            if (telegraphCompletion != null)
            {
                StopCoroutine(telegraphCompletion);
                telegraphCompletion = null;
            }

            if (activeTelegraph != null)
            {
                activeTelegraph.Complete(false);
                activeTelegraph = null;
            }
        }

        // 첫 타격 시각까지 안쪽이 차오르는 범위 표시를 그리고, 타격 순간 섬광으로 마무리한다. 보스 위험 범위 표시(BossTelegraphIndicator)를 재사용한다.
        private void ShowTelegraph(SkillTable.Area area, Vector3 origin, Vector3 areaCenter, Quaternion yaw)
        {
            if (area.Shape == SkillTable.AreaShape.Cone || area.HitDelaySeconds <= 0f)
            {
                return;
            }

            int version = telegraphVersion;
            float duration = area.HitDelaySeconds;

            // 표시를 풀에서 빌리는 동안(비동기) 대쉬 등으로 취소됐다면 번호가 달라지므로 바로 돌려준다.
            ObjectPoolManager.Instance.GetAsync(AddressableAssetKey.BossTelegraph01.ToString(), spawned =>
            {
                if (spawned == null)
                {
                    return;
                }

                if (version != telegraphVersion || !spawned.TryGetComponent(out BossTelegraphIndicator indicator))
                {
                    ObjectPoolManager.Instance?.Release(spawned);
                    return;
                }

                indicator.SetColors(database.telegraphAreaColor, database.telegraphFillColor, database.telegraphEdgeColor, database.telegraphFlashColor);

                if (area.Shape == SkillTable.AreaShape.Circle)
                {
                    indicator.SetupCircle(areaCenter, area.Radius, duration);
                }
                else
                {
                    indicator.SetupLine(origin, yaw.eulerAngles.y, area.Width, area.Length, duration);
                }

                activeTelegraph = indicator;
                telegraphCompletion = StartCoroutine(CompleteTelegraphAfter(indicator, duration, version));
            });
        }

        private IEnumerator CompleteTelegraphAfter(BossTelegraphIndicator indicator, float seconds, int version)
        {
            yield return new WaitForSeconds(seconds);

            if (version != telegraphVersion || indicator == null)
            {
                yield break;
            }

            indicator.Complete(true);

            if (activeTelegraph == indicator)
            {
                activeTelegraph = null;
            }

            telegraphCompletion = null;
        }

        // 타격 시각(첫 타격은 HitDelaySeconds 뒤, 이후 HitIntervalSeconds 간격)에 맞춰 범위 이펙트를 재생한다.
        private IEnumerator PlayHits(SkillVfxLayer layer, Vector3 center, Quaternion yaw, SkillTable.Area area, ActiveCast cast)
        {
            for (int hit = 0; hit < area.Hits; hit++)
            {
                float delay = hit == 0 ? area.HitDelaySeconds : area.HitIntervalSeconds;
                if (delay > 0f)
                {
                    yield return new WaitForSeconds(delay);
                }

                // 대쉬로 시전이 끊겼다면 남은 타격 이펙트는 만들지 않는다(서버도 남은 타격을 취소한다).
                if (cast.Cancelled)
                {
                    yield break;
                }

                SpawnLayer(layer, center, yaw, area);
            }
        }

        private static GameObject SpawnLayer(SkillVfxLayer layer, Vector3 basePosition, Quaternion yaw, SkillTable.Area area)
        {
            if (layer == null || layer.key == AddressableAssetKey.None || ObjectPoolManager.Instance == null)
            {
                return null;
            }

            Vector3 position = basePosition + yaw * layer.offset;
            Quaternion rotation = yaw * Quaternion.Euler(0f, layer.yawOffset, 0f);

            GameObject instance = ObjectPoolManager.Instance.Get(layer.key.ToString(), position, rotation);
            if (instance == null)
            {
                return null;
            }

            // 풀에서 돌려받은 인스턴스는 프리팹 원본 크기로 초기화되어 있으므로, 배율은 그 위에 곱한다.
            float scale = layer.scale;
            if (layer.referenceSize > 0f && area.EffectSize > 0f)
            {
                scale *= area.EffectSize / layer.referenceSize;
            }

            if (!Mathf.Approximately(scale, 1f))
            {
                instance.transform.localScale *= scale;
            }

            return instance;
        }

        private void LoadDatabase()
        {
            AsyncOperationHandle<SkillVfxDatabaseSO> handle;

            try
            {
                handle = Addressables.LoadAssetAsync<SkillVfxDatabaseSO>(DatabaseAddressableName);
            }
            catch (Exception exception)
            {
                DebugLogManager.GenerateErrorMessage<SkillVfxManager>($"SkillVfxDatabaseSO 로드 실패(잘못된 Key) : {exception}");
                return;
            }

            handle.Completed += result =>
            {
                if (result.Status != AsyncOperationStatus.Succeeded || result.Result == null)
                {
                    DebugLogManager.GenerateErrorMessage<SkillVfxManager>($"SkillVfxDatabaseSO 로드 실패(Status : {result.Status})");
                    return;
                }

                database = result.Result;
            };
        }
    }
}
