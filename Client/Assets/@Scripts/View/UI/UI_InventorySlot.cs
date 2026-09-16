using System;
using Incheol.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Incheol.View.UI
{
    public enum InventorySlotType
    {
        Inventory,
        EquipmentWeapon,
        EquipmentArmor,
        EquipmentHelmet,
        EquipmentBoots,
        EquipmentAccessory
    }

    /// <summary>
    /// 인벤토리 및 장비 슬롯 UI 컴포넌트.
    /// 아이템 아이콘, 수량, 등급 테두리, 선택 상태 등을 표시한다.
    /// </summary>
    public class UI_InventorySlot : MonoBehaviour
    {
        #region Variable
        [Header("Slot Settings")]
        [SerializeField] private InventorySlotType slotType = InventorySlotType.Inventory;
        [SerializeField] private int slotIndex = -1;

        [Header("UI Elements")]
        [SerializeField] private Button slotButton;
        [SerializeField] private Image slotBackground;
        [SerializeField] private Image itemIcon;
        [SerializeField] private Image gradeBorder;
        [SerializeField] private Image selectedHighlight;
        [SerializeField] private TextMeshProUGUI countText;
        [SerializeField] private TextMeshProUGUI slotLabelText;

        private bool hasItem = false;
        private string itemId;
        private int itemCount = 0;

        public InventorySlotType SlotType => slotType;
        public int SlotIndex => slotIndex;
        public bool HasItem => hasItem;
        public string ItemId => itemId;
        public int ItemCount => itemCount;

        public event Action<UI_InventorySlot> OnSlotClicked;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (slotButton != null)
            {
                slotButton.onClick.AddListener(HandleClick);
            }
        }

        private void OnDestroy()
        {
            if (slotButton != null)
            {
                slotButton.onClick.RemoveListener(HandleClick);
            }
        }
        #endregion

        #region Method
        public void InitSlot(InventorySlotType _type, int _index, string _slotLabel = null)
        {
            slotType = _type;
            slotIndex = _index;

            if (slotLabelText != null && !string.IsNullOrEmpty(_slotLabel))
            {
                slotLabelText.text = _slotLabel;
                slotLabelText.gameObject.SetActive(true);
            }
        }

        public void SetItem(string _id, Sprite _icon, int _count, ItemGrade _itemGrade)
        {
            hasItem = true;
            itemId = _id;
            itemCount = _count;

            if (itemIcon != null)
            {
                itemIcon.sprite = _icon;
                itemIcon.gameObject.SetActive(_icon != null);
            }

            if (countText != null)
            {
                if (_count > 1)
                {
                    countText.text = $"x{_count}";
                    countText.gameObject.SetActive(true);
                }
                else
                {
                    countText.gameObject.SetActive(false);
                }
            }

            if (gradeBorder != null)
            {
                gradeBorder.color = _itemGrade.GetGradeColor();
                gradeBorder.gameObject.SetActive(true);
            }

            if (slotLabelText != null)
            {
                slotLabelText.gameObject.SetActive(false);
            }
        }

        public void ClearSlot(string _defaultLabel = null)
        {
            hasItem = false;
            itemId = null;
            itemCount = 0;

            if (itemIcon != null)
            {
                itemIcon.sprite = null;
                itemIcon.gameObject.SetActive(false);
            }

            if (countText != null)
            {
                countText.gameObject.SetActive(false);
            }

            if (gradeBorder != null)
            {
                gradeBorder.gameObject.SetActive(false);
            }

            if (slotLabelText != null)
            {
                if (!string.IsNullOrEmpty(_defaultLabel))
                {
                    slotLabelText.text = _defaultLabel;
                }
                slotLabelText.gameObject.SetActive(!string.IsNullOrEmpty(slotLabelText.text));
            }

            SetSelected(false);
        }

        public void SetSelected(bool _isSelected)
        {
            if (selectedHighlight != null)
            {
                selectedHighlight.gameObject.SetActive(_isSelected);
            }
        }

        private void HandleClick()
        {
            OnSlotClicked?.Invoke(this);
        }
        #endregion
    }
}
