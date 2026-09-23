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

        [Header("골드 표시")]
        [SerializeField] private string goldDisplayName = "골드";
        // 아직 골드용 2D 스프라이트가 없으면 비워둬도 된다(아이콘 없이 이름/수량만 표시된다).
        [SerializeField] private Sprite goldIcon;

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
        /// 몬스터 처치로 획득한 골드/아이템을 contentRect 아래에 다시 그려 팝업을 연다.
        /// 골드만 떨어지고 아이템이 하나도 없어도(_items가 비어 있어도) 골드 줄만으로 팝업을 연다.
        /// _itemLookup(보통 ItemDatabaseManager.Instance.FindById)이 null을 반환하는 아이템은
        /// UI_InventoryView.RefreshInventory와 동일하게 아이콘 없이 itemId 텍스트로 최소 표시한다.
        /// </summary>
        public void Show(int _goldGained, IReadOnlyList<GameLootItemEntry> _items, Func<string, ItemData> _itemLookup)
        {
            int itemCount = _items != null ? _items.Count : 0;
            if (contentRect == null || dropListItemViewPrefab == null || (_goldGained <= 0 && itemCount == 0))
            {
                return;
            }

            ClearItems();

            if (_goldGained > 0)
            {
                UI_DropListItemView goldView = Instantiate(dropListItemViewPrefab, contentRect);
                goldView.SetGold(goldDisplayName, goldIcon, _goldGained);
                spawnedItemViews.Add(goldView);
            }

            for (int i = 0; i < itemCount; i++)
            {
                GameLootItemEntry entry = _items[i];
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
