using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Incheol.View.UI
{
    public enum InventoryTabType
    {
        All = 0,
        Equipment = 1,
        Consumable = 2,
        Etc = 3
    }

    /// <summary>
    /// 인벤토리 및 장비 관리 UI 뷰.
    /// CharacterCreateContainer와 일관된 다크 SF/모던 테마 디자인을 적용하며,
    /// 좌측 장착 장비 및 스탯 요약, 우측 카테고리 탭 및 인벤토리 슬롯 그리드를 제공한다.
    /// </summary>
    public class UI_InventoryView : MonoBehaviour
    {
        #region Variable
        [Header("Containers & Background")]
        [SerializeField] private GameObject dimMaskImage;
        [SerializeField] private RectTransform mainWindowContainer;
        [SerializeField] private Button closeButton;
        [SerializeField] private TextMeshProUGUI titleText;

        [Header("Category Tabs")]
        [SerializeField] private Button tabAllButton;
        [SerializeField] private Button tabEquipButton;
        [SerializeField] private Button tabConsumeButton;
        [SerializeField] private Button tabEtcButton;
        [SerializeField] private TextMeshProUGUI capacityText;

        [Header("Inventory Grid")]
        [SerializeField] private RectTransform inventoryContentRect;
        [SerializeField] private UI_InventorySlot inventorySlotPrefab;

        /// <summary>
        /// inventoryContentRect 아래에 동적으로 생성할 슬롯 개수.
        /// </summary>
        private const int InventorySlotCount = 30;

        // inventoryContentRect 아래에 CreateInventorySlots()가 런타임에 생성한 슬롯 목록.
        // 더 이상 Inspector에서 수동으로 채우지 않으므로 SerializeField가 아니다.
        private readonly List<UI_InventorySlot> inventorySlots = new List<UI_InventorySlot>();

        [Header("Equipment Slots")]
        [SerializeField] private UI_InventorySlot weaponSlot;
        [SerializeField] private UI_InventorySlot armorSlot;
        [SerializeField] private UI_InventorySlot helmetSlot;
        [SerializeField] private UI_InventorySlot bootsSlot;
        [SerializeField] private UI_InventorySlot accessorySlot;

        [Header("Stats Display")]
        [SerializeField] private TextMeshProUGUI strValueText;
        [SerializeField] private TextMeshProUGUI agiValueText;
        [SerializeField] private TextMeshProUGUI intValueText;
        [SerializeField] private TextMeshProUGUI atkValueText;
        [SerializeField] private TextMeshProUGUI defValueText;
        [SerializeField] private TextMeshProUGUI hpValueText;

        [Header("Currency")]
        [SerializeField] private TextMeshProUGUI goldText;
        [SerializeField] private TextMeshProUGUI diamondText;

        [Header("Action Buttons")]
        [SerializeField] private Button sortButton;
        [SerializeField] private Button dropButton;
        [SerializeField] private Button useButton;
        [SerializeField] private TextMeshProUGUI useButtonText;

        [Header("Item Detail Section")]
        [SerializeField] private GameObject itemDetailPanel;
        [SerializeField] private Image detailItemIcon;
        [SerializeField] private TextMeshProUGUI detailItemNameText;
        [SerializeField] private TextMeshProUGUI detailItemTypeText;
        [SerializeField] private TextMeshProUGUI detailItemDescText;

        private InventoryTabType currentTab = InventoryTabType.All;
        private UI_InventorySlot selectedSlot = null;

        public InventoryTabType CurrentTab => currentTab;
        public UI_InventorySlot SelectedSlot => selectedSlot;

        public event Action OnPopupClosed;
        public event Action<InventoryTabType> OnTabChanged;
        public event Action<UI_InventorySlot> OnSlotSelected;
        public event Action<UI_InventorySlot> OnUseItemRequested;
        public event Action<UI_InventorySlot> OnDropItemRequested;
        public event Action OnSortRequested;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            CreateInventorySlots();
            RegisterEvents();
            InitEquipmentSlots();
            InitTabs();
            UpdateStatsUI(UserStats.CreateDefault());
            SetCurrency(1250000, 350);
            SetCapacity(0, InventorySlotCount);
        }

        private void OnDestroy()
        {
            UnregisterEvents();
        }
        #endregion

        #region Method
        /// <summary>
        /// inventoryContentRect 아래에 inventorySlotPrefab을 InventorySlotCount(30)개 인스턴스화해서
        /// 일반 인벤토리 그리드를 채운다. 기존에 남아있는 자식(에디터에서 배치해둔 미리보기용 슬롯 등)이 있다면
        /// 먼저 정리한 뒤 다시 생성한다.
        /// </summary>
        private void CreateInventorySlots()
        {
            if (inventoryContentRect == null || inventorySlotPrefab == null)
            {
                return;
            }

            for (int i = inventoryContentRect.childCount - 1; i >= 0; i--)
            {
                Destroy(inventoryContentRect.GetChild(i).gameObject);
            }

            inventorySlots.Clear();

            for (int i = 0; i < InventorySlotCount; i++)
            {
                UI_InventorySlot slot = Instantiate(inventorySlotPrefab, inventoryContentRect);
                slot.InitSlot(InventorySlotType.Inventory, i);
                slot.OnSlotClicked += HandleSlotClicked;
                inventorySlots.Add(slot);
            }
        }

        private void RegisterEvents()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (tabAllButton != null) tabAllButton.onClick.AddListener(() => SelectTab(InventoryTabType.All));
            if (tabEquipButton != null) tabEquipButton.onClick.AddListener(() => SelectTab(InventoryTabType.Equipment));
            if (tabConsumeButton != null) tabConsumeButton.onClick.AddListener(() => SelectTab(InventoryTabType.Consumable));
            if (tabEtcButton != null) tabEtcButton.onClick.AddListener(() => SelectTab(InventoryTabType.Etc));

            if (sortButton != null) sortButton.onClick.AddListener(OnClickSort);
            if (dropButton != null) dropButton.onClick.AddListener(OnClickDrop);
            if (useButton != null) useButton.onClick.AddListener(OnClickUse);

            // inventorySlots의 각 슬롯 구독은 CreateInventorySlots()가 생성 시점에 바로 처리하므로 여기서는 하지 않는다
            // (중복 구독 방지).
        }

        private void UnregisterEvents()
        {
            if (closeButton != null) closeButton.onClick.RemoveAllListeners();
            if (tabAllButton != null) tabAllButton.onClick.RemoveAllListeners();
            if (tabEquipButton != null) tabEquipButton.onClick.RemoveAllListeners();
            if (tabConsumeButton != null) tabConsumeButton.onClick.RemoveAllListeners();
            if (tabEtcButton != null) tabEtcButton.onClick.RemoveAllListeners();
            if (sortButton != null) sortButton.onClick.RemoveAllListeners();
            if (dropButton != null) dropButton.onClick.RemoveAllListeners();
            if (useButton != null) useButton.onClick.RemoveAllListeners();

            foreach (var slot in inventorySlots)
            {
                if (slot != null) slot.OnSlotClicked -= HandleSlotClicked;
            }
        }

        private void InitEquipmentSlots()
        {
            if (weaponSlot != null)
            {
                weaponSlot.InitSlot(InventorySlotType.EquipmentWeapon, 0, "무기");
                weaponSlot.OnSlotClicked += HandleSlotClicked;
            }
            if (armorSlot != null)
            {
                armorSlot.InitSlot(InventorySlotType.EquipmentArmor, 1, "갑옷");
                armorSlot.OnSlotClicked += HandleSlotClicked;
            }
            if (helmetSlot != null)
            {
                helmetSlot.InitSlot(InventorySlotType.EquipmentHelmet, 2, "투구");
                helmetSlot.OnSlotClicked += HandleSlotClicked;
            }
            if (bootsSlot != null)
            {
                bootsSlot.InitSlot(InventorySlotType.EquipmentBoots, 3, "신발");
                bootsSlot.OnSlotClicked += HandleSlotClicked;
            }
            if (accessorySlot != null)
            {
                accessorySlot.InitSlot(InventorySlotType.EquipmentAccessory, 4, "장신구");
                accessorySlot.OnSlotClicked += HandleSlotClicked;
            }
        }

        private void InitTabs()
        {
            SelectTab(InventoryTabType.All);
        }

        public void Open()
        {
            gameObject.SetActive(true);
            if (dimMaskImage != null) dimMaskImage.SetActive(true);
        }

        public void Close()
        {
            gameObject.SetActive(false);
            OnPopupClosed?.Invoke();
        }

        public void SelectTab(InventoryTabType _tab)
        {
            currentTab = _tab;
            UpdateTabVisuals();
            OnTabChanged?.Invoke(_tab);
        }

        private void UpdateTabVisuals()
        {
            Color activeColor = new Color(0.20f, 0.65f, 1.00f, 1.00f);
            Color inactiveColor = new Color(0.18f, 0.24f, 0.35f, 0.95f);

            SetButtonColor(tabAllButton, currentTab == InventoryTabType.All ? activeColor : inactiveColor);
            SetButtonColor(tabEquipButton, currentTab == InventoryTabType.Equipment ? activeColor : inactiveColor);
            SetButtonColor(tabConsumeButton, currentTab == InventoryTabType.Consumable ? activeColor : inactiveColor);
            SetButtonColor(tabEtcButton, currentTab == InventoryTabType.Etc ? activeColor : inactiveColor);
        }

        private void SetButtonColor(Button _btn, Color _color)
        {
            if (_btn != null && _btn.targetGraphic is Image img)
            {
                img.color = _color;
            }
        }

        public void SetCurrency(long _gold, int _diamond)
        {
            if (goldText != null) goldText.text = $"{_gold:N0} G";
            if (diamondText != null) diamondText.text = $"{_diamond:N0}";
        }

        public void SetCapacity(int _current, int _max)
        {
            if (capacityText != null)
            {
                capacityText.text = $"{_current} / {_max}";
            }
        }

        public void UpdateStatsUI(UserStats _stats)
        {
            if (_stats == null) return;

            if (strValueText != null) strValueText.text = _stats.str.ToString();
            if (agiValueText != null) agiValueText.text = _stats.agi.ToString();
            if (intValueText != null) intValueText.text = _stats.intel.ToString();

            // 유도 스탯 표시
            if (atkValueText != null) atkValueText.text = (_stats.str * 2 + _stats.agi).ToString();
            if (defValueText != null) defValueText.text = (_stats.str + _stats.agi * 2).ToString();
            if (hpValueText != null) hpValueText.text = (_stats.str * 10 + 100).ToString();
        }

        public void HandleSlotClicked(UI_InventorySlot _slot)
        {
            if (selectedSlot != null)
            {
                selectedSlot.SetSelected(false);
            }

            selectedSlot = _slot;

            if (selectedSlot != null)
            {
                selectedSlot.SetSelected(true);
            }

            UpdateItemDetail(_slot);
            OnSlotSelected?.Invoke(_slot);
        }

        private void UpdateItemDetail(UI_InventorySlot _slot)
        {
            if (_slot == null || !_slot.HasItem)
            {
                if (itemDetailPanel != null) itemDetailPanel.SetActive(false);
                if (useButton != null) useButton.interactable = false;
                if (dropButton != null) dropButton.interactable = false;
                return;
            }

            if (itemDetailPanel != null) itemDetailPanel.SetActive(true);
            if (useButton != null) useButton.interactable = true;
            if (dropButton != null) dropButton.interactable = true;

            if (useButtonText != null)
            {
                useButtonText.text = _slot.SlotType == InventorySlotType.Inventory ? "장착 / 사용" : "장착 해제";
            }
        }

        private void OnClickSort()
        {
            OnSortRequested?.Invoke();
        }

        private void OnClickDrop()
        {
            if (selectedSlot != null && selectedSlot.HasItem)
            {
                OnDropItemRequested?.Invoke(selectedSlot);
            }
        }

        private void OnClickUse()
        {
            if (selectedSlot != null && selectedSlot.HasItem)
            {
                OnUseItemRequested?.Invoke(selectedSlot);
            }
        }
        #endregion
    }
}
