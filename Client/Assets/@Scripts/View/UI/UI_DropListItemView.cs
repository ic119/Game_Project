using UnityEngine;
using System;
using UnityEngine.UI;
using TMPro;

namespace Incheol.View.UI
{
    public class UI_DropListItemView : MonoBehaviour
    {
        #region Variable
        [Header("UI 변수")]
        [SerializeField] private Image itemIcon;
        [SerializeField] private TextMeshProUGUI dropItemNameLabel;
        [SerializeField] private TextMeshProUGUI dropItemCountLabel;
        #endregion 

        #region LifeCycle
        #endregion

        #region Method
        /// <summary>
        /// 드롭 아이템 한 줄을 표시한다. _icon이 null이면(ItemDatabaseSO에 아직 아이콘이 등록되지 않은 경우)
        /// UI_InventoryView.RefreshInventory와 동일한 컨벤션으로 아이콘 없이 이름/수량만 표시한다.
        /// </summary>
        public void SetItem(string _itemName, Sprite _icon, int _count)
        {
            SetRow(_itemName, _icon, $"x{_count}");
        }

        /// <summary>
        /// 골드 획득 한 줄을 표시한다. 드롭 아이템과 같은 프리팹을 쓰되 수량 표기만
        /// UI_InventoryView.SetCurrency와 동일한 "1,000 G" 형식으로 맞춘다.
        /// </summary>
        public void SetGold(string _goldName, Sprite _icon, int _amount)
        {
            SetRow(_goldName, _icon, $"{_amount:N0} G");
        }

        private void SetRow(string _name, Sprite _icon, string _countText)
        {
            if (itemIcon != null)
            {
                itemIcon.sprite = _icon;
                itemIcon.gameObject.SetActive(_icon != null);
            }

            if (dropItemNameLabel != null)
            {
                dropItemNameLabel.text = _name;
            }

            if (dropItemCountLabel != null)
            {
                dropItemCountLabel.text = _countText;
            }
        }
        #endregion
    }
}
