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

        /// <summary>
        /// LoadDatabase()의 Addressables 로드가 완료되었는지. 씬 진입 직후처럼 로드가 아직 안 끝난 상태에서
        /// FindById가 계속 null을 반환할 수 있으므로, 호출측(GameSceneManager)이 "아직 로딩 중이라 못 찾은 것"과
        /// "로드는 끝났는데 등록 안 된 itemId"를 구분해 재조회 타이밍을 잡을 때 쓴다.
        /// </summary>
        public bool IsLoaded { get; private set; }

        /// <summary>
        /// 로드가 끝나는 시점에 1회 발생한다. 인벤토리가 database 로드 완료 전에 먼저 열려 아이콘/이름 없이
        /// 표시된 경우, 구독자(GameSceneManager)가 이 이벤트를 받아 인벤토리를 다시 그려 뒤늦게라도 정상
        /// 표시되게 한다.
        /// </summary>
        public event Action OnDatabaseLoaded;

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
                IsLoaded = true;
                OnDatabaseLoaded?.Invoke();
            };
        }
    }
}
