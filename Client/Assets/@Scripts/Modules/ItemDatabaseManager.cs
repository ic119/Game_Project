using System;
using Incheol.Models.SO;
using Incheol.Utils;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Incheol.Modules
{
    /// <summary>
    /// ItemDatabaseSO(Addressable, 고정 이름으로 로드)를 관리하며 itemId로 ItemData를 조회하는 진입점.
    /// WeaponVfxManager/RemoteMonsterManager와 같은 패턴 - 호출측(UI_InventoryView 등)은 이 매니저를 통해서만
    /// 아이템 정적 데이터(이름/아이콘/등급 등)에 접근하고, ItemDatabaseSO 자체를 직접 로드하지 않는다.
    /// </summary>
    public class ItemDatabaseManager : SingletonObject<ItemDatabaseManager>
    {
        private const string DatabaseAddressableName = "ItemDatabaseSO";

        protected override bool PersistAcrossScenes => true;

        private ItemDatabaseSO database;

        protected override void Awake()
        {
            base.Awake();
            LoadDatabase();
        }

        /// <summary>
        /// itemId로 ItemData를 조회한다. 데이터베이스가 아직 로드되지 않았거나 등록되지 않은 itemId면 null을 반환한다 -
        /// 호출측(UI_InventorySlot)은 null일 때 아이콘 없이 itemId/수량만 표시하는 식으로 대응해야 한다.
        /// </summary>
        public ItemData FindById(string itemId)
        {
            return database != null ? database.FindById(itemId) : null;
        }

        private void LoadDatabase()
        {
            AsyncOperationHandle<ItemDatabaseSO> handle;

            try
            {
                handle = Addressables.LoadAssetAsync<ItemDatabaseSO>(DatabaseAddressableName);
            }
            catch (Exception exception)
            {
                DebugLogManager.GenerateErrorMessage<ItemDatabaseManager>($"ItemDatabaseSO 로드 실패(잘못된 Key) : {exception}");
                return;
            }

            handle.Completed += result =>
            {
                if (result.Status != AsyncOperationStatus.Succeeded || result.Result == null)
                {
                    DebugLogManager.GenerateErrorMessage<ItemDatabaseManager>($"ItemDatabaseSO 로드 실패(Status : {result.Status})");
                    return;
                }

                database = result.Result;
            };
        }
    }
}
