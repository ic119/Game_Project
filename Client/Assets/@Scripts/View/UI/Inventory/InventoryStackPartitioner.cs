using System;
using System.Collections.Generic;

namespace Incheol.View.UI
{
    /// <summary>
    /// 보유 아이템 스택을 "일반 칸에 표시할 미장착 스택"과 "장비 슬롯별 장착 스택"으로 나눈다. 화면 요소를 모르는 순수 함수라
    /// UI 없이 결과를 확인할 수 있다. equipSlot이 비었거나 알 수 없는 이름이거나 None이면 미장착으로 본다.
    /// </summary>
    public static class InventoryStackPartitioner
    {
        public static void Partition(
            IReadOnlyList<InventoryItemStack> items,
            out List<InventoryItemStack> unequipped,
            out Dictionary<EquipmentSlotType, InventoryItemStack> equippedBySlot)
        {
            unequipped = new List<InventoryItemStack>();
            equippedBySlot = new Dictionary<EquipmentSlotType, InventoryItemStack>();

            if (items == null)
            {
                return;
            }

            foreach (InventoryItemStack stack in items)
            {
                if (!string.IsNullOrEmpty(stack.equipSlot) &&
                    Enum.TryParse(stack.equipSlot, out EquipmentSlotType parsedSlot) &&
                    parsedSlot != EquipmentSlotType.None)
                {
                    equippedBySlot[parsedSlot] = stack;
                }
                else
                {
                    unequipped.Add(stack);
                }
            }
        }
    }
}
