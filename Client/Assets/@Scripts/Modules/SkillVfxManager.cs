using System;
using System.Collections;
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

        protected override void Awake()
        {
            base.Awake();
            LoadDatabase();
        }

        /// <summary>
        /// 스킬 시전 이펙트를 재생한다.
        /// </summary>
        /// <param name="origin">시전자 발 위치(월드).</param>
        /// <param name="rotation">시전 방향(시전자가 바라보는 방향). 수평 성분(yaw)만 쓴다.</param>
        public void PlaySkill(SkillTable.Entry skill, Vector3 origin, Quaternion rotation)
        {
            // database가 아직 로드되지 않았거나 이 스킬에 등록된 이펙트가 없으면 조용히 건너뛴다 - 이펙트는 연출일 뿐이다.
            if (database == null || ObjectPoolManager.Instance == null || !database.TryGetEntry(skill.Id, out SkillVfxEntry entry))
            {
                return;
            }

            Quaternion yaw = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
            SkillTable.Area area = skill.Area;
            Vector3 areaCenter = origin + yaw * (Vector3.forward * area.CenterForwardDistance);

            SpawnLayer(entry.cast, origin, yaw, area);
            SpawnLayer(entry.areaStart, areaCenter, yaw, area);

            if (entry.areaHit != null && entry.areaHit.key != AddressableAssetKey.None)
            {
                StartCoroutine(PlayHits(entry.areaHit, areaCenter, yaw, area));
            }
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
