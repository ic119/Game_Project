using System;
using System.Collections;
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
        public void PlaySkill(SkillTable.Entry skill, Vector3 origin, Quaternion rotation, bool showTelegraph = false)
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

            SpawnLayer(entry.cast, origin, yaw, area);
            SpawnLayer(entry.areaStart, areaCenter, yaw, area);

            if (showTelegraph && entry.showTelegraph)
            {
                ShowTelegraph(area, origin, areaCenter, yaw);
            }

            if (entry.projectile != null && entry.projectile.key != AddressableAssetKey.None)
            {
                StartCoroutine(LaunchProjectile(entry, origin, yaw, area));
            }

            if (entry.areaHit != null && entry.areaHit.key != AddressableAssetKey.None)
            {
                StartCoroutine(PlayHits(entry.areaHit, areaCenter, yaw, area));
            }
        }

        // 지팡이 끝에서 범위의 끝까지 투사체를 날린다. 첫 타격 시각에 도착하도록 출발 시각을 맞춘다(거리/속도만큼 날아가므로 그만큼 늦게 출발).
        private IEnumerator LaunchProjectile(SkillVfxEntry entry, Vector3 origin, Quaternion yaw, SkillTable.Area area)
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

            ProjectileVfxManager.Instance?.FireSkillProjectile(layer.key, start, end, travel, layer.scale);
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
        private IEnumerator PlayHits(SkillVfxLayer layer, Vector3 center, Quaternion yaw, SkillTable.Area area)
        {
            for (int hit = 0; hit < area.Hits; hit++)
            {
                float delay = hit == 0 ? area.HitDelaySeconds : area.HitIntervalSeconds;
                if (delay > 0f)
                {
                    yield return new WaitForSeconds(delay);
                }

                SpawnLayer(layer, center, yaw, area);
            }
        }

        private static void SpawnLayer(SkillVfxLayer layer, Vector3 basePosition, Quaternion yaw, SkillTable.Area area)
        {
            if (layer == null || layer.key == AddressableAssetKey.None || ObjectPoolManager.Instance == null)
            {
                return;
            }

            Vector3 position = basePosition + yaw * layer.offset;
            Quaternion rotation = yaw * Quaternion.Euler(0f, layer.yawOffset, 0f);

            GameObject instance = ObjectPoolManager.Instance.Get(layer.key.ToString(), position, rotation);
            if (instance == null)
            {
                return;
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
