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

        // 장비 슬롯이 비어있을 때 itemIcon 자리에 흐리게 표시할 종류별 아이콘(예: 무기 슬롯엔 검 아이콘).
        // InitSlot으로 주입되며, 일반 인벤토리 칸(placeholderIcon == null)은 지금까지처럼 완전히 빈 채로 남는다.
        private Sprite placeholderIcon;
        private static readonly Color PlaceholderIconColor = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color ItemIconColor = Color.white;

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
        /// <summary>
        /// _placeholderIcon은 장비 슬롯 전용이다(일반 인벤토리 칸은 생략) - 아무 것도 장착하지 않은 상태에서
        /// itemIcon 자리에 흐리게 표시해 "이 슬롯이 어떤 종류의 장비 칸인지" 아이콘으로도 알려준다.
        /// </summary>
        public void InitSlot(InventorySlotType _type, int _index, string _slotLabel = null, Sprite _placeholderIcon = null)
        {
            slotType = _type;
            slotIndex = _index;
            placeholderIcon = _placeholderIcon;

            if (slotLabelText != null && !string.IsNullOrEmpty(_slotLabel))
            {
                slotLabelText.text = _slotLabel;
                slotLabelText.gameObject.SetActive(true);
            }

            if (!hasItem)
            {
                ApplyPlaceholderIcon();
            }
        }

        /// <summary>
        /// _displayName은 아이콘이 없을 때(ItemDatabaseSO가 아직 로드 전이거나 해당 itemId에 아이콘이
        /// 등록되지 않은 경우) slotLabelText에 대신 표시할 이름이다. UI_DropListItemView와 같은 컨벤션으로,
        /// 아이콘이 없다고 슬롯이 완전히 빈 것처럼 보이지 않도록 최소한 이름 텍스트는 항상 보장한다.
        /// </summary>
        public void SetItem(string _id, string _displayName, Sprite _icon, int _count, ItemGrade _itemGrade)
        {
            hasItem = true;
            itemId = _id;
            itemCount = _count;

            if (itemIcon != null)
            {
                itemIcon.sprite = _icon;
                itemIcon.color = ItemIconColor;
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
                if (_icon == null)
                {
                    slotLabelText.text = _displayName;
                    slotLabelText.gameObject.SetActive(!string.IsNullOrEmpty(_displayName));
                }
                else
                {
                    slotLabelText.gameObject.SetActive(false);
                }
            }
        }

        public void ClearSlot(string _defaultLabel = null)
        {
            hasItem = false;
            itemId = null;
            itemCount = 0;

            ApplyPlaceholderIcon();

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

        /// <summary>
        /// placeholderIcon이 있으면(장비 슬롯) itemIcon 자리에 흐리게 표시하고, 없으면(일반 인벤토리 칸)
        /// 지금까지처럼 아이콘을 완전히 숨긴다. SetItem이 실제 아이템으로 덮어쓸 때 색을 다시 불투명으로 되돌린다.
        /// </summary>
        private void ApplyPlaceholderIcon()
        {
            if (itemIcon == null)
            {
                return;
            }

            if (placeholderIcon != null)
            {
                itemIcon.sprite = placeholderIcon;
                itemIcon.color = PlaceholderIconColor;
                itemIcon.gameObject.SetActive(true);
            }
            else
            {
                itemIcon.sprite = null;
                itemIcon.gameObject.SetActive(false);
            }
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
