using System;
using Incheol.Models.Define;
using Incheol.Models.SO;
using Incheol.Utils;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Incheol.Modules
{
    /// <summary>
    /// WeaponType별 공격(스윙)/피격(임팩트) 이펙트를 재생하는 진입점.
    /// WeaponVfxDatabaseSO(Addressable, 고정 이름으로 로드)를 내부에서 관리하며, 호출측(PlayerAttackController 등)은
    /// 무기 타입과 월드 위치/회전만 넘기면 된다. 실제 스폰/재사용은 ObjectPoolManager + PooledEffect가 담당한다.
    /// SceneLoadManager.LoadSceneDataModelSO와 같은 패턴으로 Addressables.LoadAssetAsync를 직접 사용한다 -
    /// AddressableAssetModelSO.preloadAddressableKeys 목록에 넣는 대상은 개별 이펙트 프리팹이지, 이 SO 자체는 아니다.
    /// </summary>
    public class WeaponVfxManager : SingletonObject<WeaponVfxManager>
    {
        private const string DatabaseAddressableName = "WeaponVfxDatabaseSO";

        protected override bool PersistAcrossScenes => true;

        private WeaponVfxDatabaseSO database;

        protected override void Awake()
        {
            base.Awake();
            LoadDatabase();
        }

        /// <summary>
        /// 무기를 휘두르는 순간 재생하는 이펙트. 명중 여부와 무관하게 콤보 타수마다 호출해야 한다.
        /// </summary>
        /// <param name="scale">프리팹에 이미 적용된 크기(예: 0.7) 위에 추가로 곱해지는 배율. 기본 1이면 프리팹 크기 그대로.</param>
        public void PlaySwingEffect(WeaponType weaponType, Vector3 position, Quaternion rotation, float scale = 1f)
        {
            PlayEffect(weaponType, position, rotation, scale, useImpactEffect: false);
        }

        /// <summary>
        /// 공격이 실제로 명중했을 때(Game_DamageBroadcast 확인 시점) 대상 위치에 재생하는 이펙트.
        /// </summary>
        /// <param name="scale">프리팹에 이미 적용된 크기(예: 0.7) 위에 추가로 곱해지는 배율. 기본 1이면 프리팹 크기 그대로.</param>
        public void PlayImpactEffect(WeaponType weaponType, Vector3 position, Quaternion rotation, float scale = 1f)
        {
            PlayEffect(weaponType, position, rotation, scale, useImpactEffect: true);
        }

        private void LoadDatabase()
        {
            AsyncOperationHandle<WeaponVfxDatabaseSO> handle;

            try
            {
                handle = Addressables.LoadAssetAsync<WeaponVfxDatabaseSO>(DatabaseAddressableName);
            }
            catch (Exception exception)
            {
                DebugLogManager.GenerateErrorMessage<WeaponVfxManager>($"WeaponVfxDatabaseSO 로드 실패(잘못된 Key) : {exception}");
                return;
            }

            handle.Completed += result =>
            {
                if (result.Status != AsyncOperationStatus.Succeeded || result.Result == null)
                {
                    DebugLogManager.GenerateErrorMessage<WeaponVfxManager>($"WeaponVfxDatabaseSO 로드 실패(Status : {result.Status})");
                    return;
                }

                database = result.Result;
            };
        }

        private void PlayEffect(WeaponType weaponType, Vector3 position, Quaternion rotation, float scale, bool useImpactEffect)
        {
            // database가 아직 로드되지 않았거나(부트스트랩 직후 극초반) 해당 무기 타입에 등록된 이펙트가 없으면
            // 조용히 건너뛴다 - 이펙트는 연출일 뿐이므로 없다고 공격 자체를 막을 이유가 없다.
            if (database == null || ObjectPoolManager.Instance == null)
            {
                return;
            }

            if (!database.TryGetEntry(weaponType, out WeaponVfxEntry entry))
            {
                return;
            }

            AddressableAssetKey key = useImpactEffect ? entry.impactEffectKey : entry.swingEffectKey;
            if (key == AddressableAssetKey.None)
            {
                return;
            }

            GameObject instance = ObjectPoolManager.Instance.Get(key.ToString(), position, rotation);

            // 풀에서 돌려받은 인스턴스는 프리팹 원본 크기(예: 0.7)로 초기화되어 있으므로, scale은 그 위에
            // 곱해지는 배율이다. scale이 1이면(기본값) 굳이 건드리지 않는다.
            if (instance != null && !Mathf.Approximately(scale, 1f))
            {
                instance.transform.localScale *= scale;
            }
        }
    }
}
