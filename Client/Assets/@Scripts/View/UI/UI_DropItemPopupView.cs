using System;
using System.Collections.Generic;
using Incheol.Modules.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace Incheol.View.UI
{
    public class UI_DropItemPopupView : MonoBehaviour
    {
        #region Variable
        [Header("UI 변수")]
        [SerializeField] private RectTransform contentRect;
        [SerializeField] private UI_DropListItemView dropListItemViewPrefab;
        [SerializeField] private GameObject dropItemListContainer;
        [SerializeField] private Button closeButton;

        // contentRect 아래에 Show()가 런타임에 생성한 아이템 줄 목록. 다음 Show() 호출(또는 Close())에서 정리한다.
        private readonly List<UI_DropListItemView> spawnedItemViews = new();
        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(Close);
            }

            if (dropItemListContainer != null)
            {
                dropItemListContainer.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
            }
        }
        #endregion

        #region Method
        /// <summary>
        /// 몬스터 처치로 획득한 아이템 목록을 contentRect 아래에 다시 그려 팝업을 연다.
        /// _itemLookup(보통 ItemDatabaseManager.Instance.FindById)이 null을 반환하는 아이템은
        /// UI_InventoryView.RefreshInventory와 동일하게 아이콘 없이 itemId 텍스트로 최소 표시한다.
        /// </summary>
        public void Show(IReadOnlyList<GameLootItemEntry> _items, Func<string, ItemData> _itemLookup)
        {
            if (contentRect == null || dropListItemViewPrefab == null || _items == null || _items.Count == 0)
            {
                return;
            }

            ClearItems();

            foreach (GameLootItemEntry entry in _items)
            {
                ItemData itemData = _itemLookup?.Invoke(entry.ItemId);
                UI_DropListItemView itemView = Instantiate(dropListItemViewPrefab, contentRect);
                itemView.SetItem(itemData != null ? itemData.itemName : entry.ItemId, itemData?.icon, entry.Qty);
                spawnedItemViews.Add(itemView);
            }

            if (dropItemListContainer != null)
            {
                dropItemListContainer.SetActive(true);
            }
        }

        public void Close()
        {
            if (dropItemListContainer != null)
            {
                dropItemListContainer.SetActive(false);
            }
        }

        private void ClearItems()
        {
            foreach (UI_DropListItemView itemView in spawnedItemViews)
            {
                if (itemView != null)
                {
                    Destroy(itemView.gameObject);
                }
            }

            spawnedItemViews.Clear();
        }
        #endregion
    }
}
