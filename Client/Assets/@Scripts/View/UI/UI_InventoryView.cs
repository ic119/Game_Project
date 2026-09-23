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

        [Header("Action Buttons")]
        [SerializeField] private Button sortButton;
        [SerializeField] private Button dropButton;
        [SerializeField] private Button useButton;
        [SerializeField] private TextMeshProUGUI useButtonText;

        [Header("Item Detail Section")]
        [SerializeField] private GameObject itemDetailPanel;
        [SerializeField] private TextMeshProUGUI detailItemNameText;
        [SerializeField] private TextMeshProUGUI detailItemTypeText;
        [SerializeField] private TextMeshProUGUI detailItemDescText;

        // RefreshInventory가 마지막으로 받은 아이템 조회 함수를 캐싱해둔다. 슬롯 클릭(UpdateItemDetail)은
        // RefreshInventory 호출과 별개의 시점에 일어나므로, 그때마다 새로 받을 방법이 없어 마지막 값을 재사용한다.
        // 인벤토리가 열려있으려면 이미 최소 한 번 RefreshInventory가 호출된 뒤이므로 항상 최신 값이다.
        private Func<string, ItemData> itemLookup;

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
            // 실제 값은 GameSceneManager.RefreshInventoryDisplay가 스폰 직후 곧바로 덮어쓴다 - 여기서는
            // 인벤토리 열기 전까지 잠깐 보일 자리 표시자일 뿐이다.
            UpdateStatsUI(UserStats.CreateDefault(), 0, 0, 0);
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
            // placeholderIcon 인자를 넘기지 않는다 - 장비 슬롯도 일반 인벤토리 칸과 동일하게, 아무 것도
            // 장착하지 않은 상태에서는 아이콘 없이 라벨 텍스트("무기" 등)만 보이게 한다.
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

            // 인벤토리를 처음 여는 시점에는 선택된 슬롯이 없으므로, 에디터에서 미리보기용으로 채워뒀을 수 있는
            // 상세정보 패널/텍스트를 빈 상태로 초기화한다. 이후 상태는 슬롯 클릭(UpdateItemDetail)이 관리한다.
            if (itemDetailPanel != null) itemDetailPanel.SetActive(false);
            if (detailItemNameText != null) detailItemNameText.text = string.Empty;
            if (detailItemTypeText != null) detailItemTypeText.text = string.Empty;
            if (detailItemDescText != null) detailItemDescText.text = string.Empty;
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
        }

        /// <summary>
        /// 골드 표시와 인벤토리 슬롯 전체를 실제 보유 데이터로 다시 그린다. 서버(CharacterItem)가 슬롯 인덱스를
        /// 따로 저장하지 않는 스택형 인벤토리라(GameSceneManager.localInventoryItems 참고), equipSlot이 비어있는
        /// (미장착) 스택만 순서대로 앞 슬롯부터 채우고 나머지는 비운다 - 드래그로 슬롯 순서를 바꾸는 기능은 아직
        /// 없어 순서가 흔들릴 일이 없다. equipSlot이 있는(장착 중인) 스택은 일반 그리드에는 표시하지 않고
        /// RefreshEquipmentSlots가 좌측 해당 장비 슬롯에 표시한다.
        /// _itemLookup(보통 ItemDatabaseManager.Instance.FindById)이 null을 반환하면(디자이너가 아직 아이콘/설명을
        /// 채우기 전) 아이콘 없이 itemId 텍스트와 수량만으로 최소 표시한다. ItemDatabaseSO를 직접 참조하지 않고
        /// 조회 함수만 받는 이유는 ItemDatabaseManager가 유일한 로드 지점이라는 규칙을 UI 쪽에서도 지키기 위함이다.
        /// </summary>
        public void RefreshInventory(long _gold, IReadOnlyList<InventoryItemStack> _items, Func<string, ItemData> _itemLookup)
        {
            itemLookup = _itemLookup;

            SetCurrency(_gold, 0);

            var unequippedItems = new List<InventoryItemStack>();
            var equippedBySlot = new Dictionary<EquipmentSlotType, InventoryItemStack>();

            if (_items != null)
            {
                foreach (InventoryItemStack stack in _items)
                {
                    if (!string.IsNullOrEmpty(stack.equipSlot) &&
                        Enum.TryParse(stack.equipSlot, out EquipmentSlotType parsedSlot) &&
                        parsedSlot != EquipmentSlotType.None)
                    {
                        equippedBySlot[parsedSlot] = stack;
                    }
                    else
                    {
                        unequippedItems.Add(stack);
                    }
                }
            }

            RefreshEquipmentSlots(equippedBySlot, _itemLookup);

            int itemCount = unequippedItems.Count;

            for (int i = 0; i < inventorySlots.Count; i++)
            {
                if (i >= itemCount)
                {
                    inventorySlots[i].ClearSlot();
                    continue;
                }

                InventoryItemStack stack = unequippedItems[i];
                ItemData itemData = _itemLookup?.Invoke(stack.itemId);

                if (itemData != null)
                {
                    inventorySlots[i].SetItem(stack.itemId, itemData.itemName, itemData.icon, stack.count, itemData.itemGrade);
                }
                else
                {
                    inventorySlots[i].SetItem(stack.itemId, stack.itemId, null, stack.count, ItemGrade.Common);
                }
            }

            SetCapacity(itemCount, inventorySlots.Count);
        }

        /// <summary>
        /// equipSlot별로 정리된 장착 아이템을 좌측 장비 슬롯(weaponSlot 등)에 반영한다. 해당 슬롯에 장착된 아이템이
        /// 없으면 InitEquipmentSlots가 지정해둔 기본 라벨("무기" 등)로 되돌린다.
        /// </summary>
        private void RefreshEquipmentSlots(Dictionary<EquipmentSlotType, InventoryItemStack> _equippedBySlot, Func<string, ItemData> _itemLookup)
        {
            SetEquipmentSlot(weaponSlot, EquipmentSlotType.Weapon, "무기", _equippedBySlot, _itemLookup);
            SetEquipmentSlot(armorSlot, EquipmentSlotType.Armor, "갑옷", _equippedBySlot, _itemLookup);
            SetEquipmentSlot(helmetSlot, EquipmentSlotType.Helmet, "투구", _equippedBySlot, _itemLookup);
            SetEquipmentSlot(bootsSlot, EquipmentSlotType.Boots, "신발", _equippedBySlot, _itemLookup);
            SetEquipmentSlot(accessorySlot, EquipmentSlotType.Accessory, "장신구", _equippedBySlot, _itemLookup);
        }

        private static void SetEquipmentSlot(UI_InventorySlot _slot, EquipmentSlotType _slotType, string _defaultLabel,
            Dictionary<EquipmentSlotType, InventoryItemStack> _equippedBySlot, Func<string, ItemData> _itemLookup)
        {
            if (_slot == null)
            {
                return;
            }

            if (!_equippedBySlot.TryGetValue(_slotType, out InventoryItemStack stack))
            {
                _slot.ClearSlot(_defaultLabel);
                return;
            }

            ItemData itemData = _itemLookup?.Invoke(stack.itemId);
            if (itemData != null)
            {
                _slot.SetItem(stack.itemId, itemData.itemName, itemData.icon, stack.count, itemData.itemGrade);
            }
            else
            {
                _slot.SetItem(stack.itemId, stack.itemId, null, stack.count, ItemGrade.Common);
            }
        }

        public void SetCapacity(int _current, int _max)
        {
            if (capacityText != null)
            {
                capacityText.text = $"{_current} / {_max}";
            }
        }

        /// <summary>
        /// 원본 스탯(str/agi/intel)과 실제 전투 수치(공격력/방어력/최대체력)를 함께 받아 표시한다. 공격력/방어력/
        /// 최대체력을 이 UI가 str/agi로부터 다시 계산하지 않는다 - CombatStatComponent/HealthComponent의 실제
        /// 계산 결과(장비 보너스 포함)를 그대로 받아야, 장착한 장비의 효과가 이 패널에도 정확히 반영된다.
        /// </summary>
        public void UpdateStatsUI(UserStats _stats, int _attackPower, int _defense, int _maxHp)
        {
            if (_stats == null) return;

            if (strValueText != null) strValueText.text = _stats.str.ToString();
            if (agiValueText != null) agiValueText.text = _stats.agi.ToString();
            if (intValueText != null) intValueText.text = _stats.intel.ToString();

            if (atkValueText != null) atkValueText.text = _attackPower.ToString();
            if (defValueText != null) defValueText.text = _defense.ToString();
            if (hpValueText != null) hpValueText.text = _maxHp.ToString();
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

            ItemData itemData = itemLookup?.Invoke(_slot.ItemId);

            if (detailItemNameText != null)
            {
                detailItemNameText.text = itemData != null ? itemData.itemName : _slot.ItemId;
            }

            if (detailItemTypeText != null)
            {
                detailItemTypeText.text = itemData != null ? GetItemTypeLabel(itemData) : string.Empty;
            }

            if (detailItemDescText != null)
            {
                detailItemDescText.text = itemData != null ? itemData.description : string.Empty;
            }
        }

        /// <summary>
        /// 상세정보 패널의 "종류" 텍스트(예: "장비 · 무기", "물약", "기타"). 장비류는 등급별 슬롯 라벨과
        /// 같은 표기(무기/갑옷/투구/신발/장신구)를 재사용해 좌측 장비 슬롯 라벨과 용어가 갈리지 않게 한다.
        /// </summary>
        private static string GetItemTypeLabel(ItemData _itemData)
        {
            switch (_itemData.itemType)
            {
                case ItemType.Eqiupment:
                    return $"장비 · {GetEquipmentSlotLabel(_itemData.equipSlotType)}";
                case ItemType.Potion:
                    return "물약";
                default:
                    return "기타";
            }
        }

        private static string GetEquipmentSlotLabel(EquipmentSlotType _slotType)
        {
            switch (_slotType)
            {
                case EquipmentSlotType.Weapon: return "무기";
                case EquipmentSlotType.Armor: return "갑옷";
                case EquipmentSlotType.Helmet: return "투구";
                case EquipmentSlotType.Boots: return "신발";
                case EquipmentSlotType.Accessory: return "장신구";
                default: return "장비";
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
