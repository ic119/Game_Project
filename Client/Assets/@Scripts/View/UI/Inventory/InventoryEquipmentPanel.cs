using System;
using System.Collections.Generic;

namespace Incheol.View.UI
{
    /// <summary>
    /// 인벤토리 좌측의 장비 슬롯 4개(무기/갑옷/투구/장신구). 슬롯 라벨 초기화와 장착 아이템 표시를 맡는다.
    /// 슬롯 오브젝트 자체는 UI_InventoryView의 인스펙터에서 연결된 것을 받아 쓴다(프리팹 직렬화 값은 뷰에 그대로 둔다).
    /// 연결되지 않은 슬롯(null)은 건너뛴다.
    /// </summary>
    public class InventoryEquipmentPanel
    {
        private readonly UI_InventorySlot weaponSlot;
        private readonly UI_InventorySlot armorSlot;
        private readonly UI_InventorySlot helmetSlot;
        private readonly UI_InventorySlot accessorySlot;

        public InventoryEquipmentPanel(UI_InventorySlot weaponSlot, UI_InventorySlot armorSlot, UI_InventorySlot helmetSlot, UI_InventorySlot accessorySlot)
        {
            this.weaponSlot = weaponSlot;
            this.armorSlot = armorSlot;
            this.helmetSlot = helmetSlot;
            this.accessorySlot = accessorySlot;
        }

        /// <summary>
        /// 슬롯 종류/라벨을 지정하고 클릭 이벤트를 연결한다. placeholderIcon을 넘기지 않는다 - 장비 슬롯도 일반 인벤토리 칸과 동일하게,
        /// 아무 것도 장착하지 않은 상태에서는 아이콘 없이 라벨 텍스트("무기" 등)만 보이게 한다.
        /// </summary>
        public void Init(Action<UI_InventorySlot> onSlotClicked)
        {
            InitSlot(weaponSlot, InventorySlotType.EquipmentWeapon, 0, "무기", onSlotClicked);
            InitSlot(armorSlot, InventorySlotType.EquipmentArmor, 1, "갑옷", onSlotClicked);
            InitSlot(helmetSlot, InventorySlotType.EquipmentHelmet, 2, "투구", onSlotClicked);
            InitSlot(accessorySlot, InventorySlotType.EquipmentAccessory, 4, "장신구", onSlotClicked);
        }

        /// <summary>
        /// equipSlot별로 정리된 장착 아이템을 슬롯에 반영한다. 장착된 아이템이 없는 슬롯은 Init이 지정한 기본 라벨로 되돌린다.
        /// </summary>
        public void Refresh(Dictionary<EquipmentSlotType, InventoryItemStack> equippedBySlot, Func<string, ItemData> itemLookup)
        {
            SetSlot(weaponSlot, EquipmentSlotType.Weapon, "무기", equippedBySlot, itemLookup);
            SetSlot(armorSlot, EquipmentSlotType.Armor, "갑옷", equippedBySlot, itemLookup);
            SetSlot(helmetSlot, EquipmentSlotType.Helmet, "투구", equippedBySlot, itemLookup);
            SetSlot(accessorySlot, EquipmentSlotType.Accessory, "장신구", equippedBySlot, itemLookup);
        }

        private static void InitSlot(UI_InventorySlot slot, InventorySlotType slotType, int index, string label, Action<UI_InventorySlot> onSlotClicked)
        {
            if (slot == null)
            {
                return;
            }

            slot.InitSlot(slotType, index, label);
            slot.OnSlotClicked += onSlotClicked;
        }

        private static void SetSlot(UI_InventorySlot slot, EquipmentSlotType slotType, string defaultLabel,
            Dictionary<EquipmentSlotType, InventoryItemStack> equippedBySlot, Func<string, ItemData> itemLookup)
        {
            if (slot == null)
            {
                return;
            }

            if (!equippedBySlot.TryGetValue(slotType, out InventoryItemStack stack))
            {
                slot.ClearSlot(defaultLabel);
                return;
            }

            ItemData itemData = itemLookup?.Invoke(stack.itemId);
            if (itemData != null)
            {
                slot.SetItem(stack.itemId, itemData.itemName, itemData.icon, stack.count, itemData.itemGrade);
            }
            else
            {
                slot.SetItem(stack.itemId, stack.itemId, null, stack.count, ItemGrade.Common);
            }
        }
    }
}
