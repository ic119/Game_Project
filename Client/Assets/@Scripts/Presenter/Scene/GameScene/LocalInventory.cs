using System;
using System.Collections.Generic;

namespace Incheol.Presenter.Scene
{
    /// <summary>
    /// 로컬 플레이어가 보유한 아이템 스택의 런타임 상태. itemId별 수량과 장착 슬롯(equipSlot)을 들고 있으며 서버(CharacterItem)와
    /// 1:1로 대응한다. 장착/해제/버리기는 서버 저장이 실패하면 되돌려야 하므로, 상태를 바꾸는 메서드마다 되돌리는 짝(Revert*)을 둔다.
    /// 화면, 캐릭터 모델, 네트워크를 전혀 모르는 순수 로직이라 UI 없이 확인할 수 있다.
    /// </summary>
    public class LocalInventory
    {
        private readonly List<InventoryItemStack> items = new List<InventoryItemStack>();

        public IReadOnlyList<InventoryItemStack> Items => items;

        /// <summary>서버에서 받은 보유 목록으로 통째로 바꾼다(로그인 복원).</summary>
        public void Replace(IEnumerable<InventoryItemStack> newItems)
        {
            items.Clear();
            items.AddRange(newItems);
        }

        /// <summary>같은 itemId 스택이 있으면 수량만 더하고, 없으면 새 스택을 추가한다.</summary>
        public void AddOrMerge(string itemId, int qty)
        {
            InventoryItemStack existing = items.Find(stack => stack.itemId == itemId);
            if (existing != null)
            {
                existing.count += qty;
                return;
            }

            items.Add(new InventoryItemStack(itemId, qty));
        }

        /// <summary>itemId의 미장착 스택. 없으면 null.</summary>
        public InventoryItemStack FindUnequipped(string itemId)
        {
            return items.Find(stack => stack.itemId == itemId && string.IsNullOrEmpty(stack.equipSlot));
        }

        /// <summary>장착 중인(equipSlot이 설정된) 스택들.</summary>
        public IEnumerable<InventoryItemStack> EquippedStacks()
        {
            foreach (InventoryItemStack stack in items)
            {
                if (!string.IsNullOrEmpty(stack.equipSlot))
                {
                    yield return stack;
                }
            }
        }

        /// <summary>
        /// 장착 중인 스택들의 공격력/방어력 보너스를 합산한다. 아이템 정보를 찾을 수 없는 스택(데이터베이스 미로드/미등록)은 건너뛴다.
        /// </summary>
        public void SumEquipmentBonus(Func<string, ItemData> itemLookup, out int attackBonus, out int defenseBonus)
        {
            attackBonus = 0;
            defenseBonus = 0;

            foreach (InventoryItemStack stack in EquippedStacks())
            {
                ItemData itemData = itemLookup(stack.itemId);
                if (itemData == null)
                {
                    continue;
                }

                attackBonus += itemData.bonusAttackPower;
                defenseBonus += itemData.bonusDefense;
            }
        }

        /// <summary>
        /// itemId의 미장착 스택을 slotKey 슬롯에 장착한다. 그 슬롯에 이미 장착 중이던 스택은 자동으로 해제된다(previous).
        /// 미장착 스택이 없으면 false.
        /// </summary>
        public bool TryEquip(string itemId, string slotKey, out InventoryItemStack target, out InventoryItemStack previous)
        {
            previous = null;
            target = FindUnequipped(itemId);
            if (target == null)
            {
                return false;
            }

            previous = items.Find(stack => stack.equipSlot == slotKey);
            if (previous == target)
            {
                return false; // 이미 장착 중
            }

            if (previous != null)
            {
                previous.equipSlot = null;
            }

            target.equipSlot = slotKey;
            return true;
        }

        /// <summary>TryEquip을 되돌린다(서버 저장 실패).</summary>
        public void RevertEquip(InventoryItemStack target, InventoryItemStack previous, string slotKey)
        {
            target.equipSlot = null;

            if (previous != null)
            {
                previous.equipSlot = slotKey;
            }
        }

        /// <summary>slotKey 슬롯에 장착 중인 스택을 해제한다. 장착 중인 것이 없으면 false.</summary>
        public bool TryUnequip(string slotKey, out InventoryItemStack stack)
        {
            stack = items.Find(s => s.equipSlot == slotKey);
            if (stack == null)
            {
                return false;
            }

            stack.equipSlot = null;
            return true;
        }

        /// <summary>TryUnequip을 되돌린다(서버 저장 실패).</summary>
        public void RevertUnequip(InventoryItemStack stack, string slotKey)
        {
            stack.equipSlot = slotKey;
        }

        /// <summary>itemId의 미장착 스택 전체를 제거한다(슬롯 단위로 통째로 버린다). 없으면 null.</summary>
        public InventoryItemStack RemoveUnequipped(string itemId)
        {
            InventoryItemStack stack = FindUnequipped(itemId);
            if (stack != null)
            {
                items.Remove(stack);
            }

            return stack;
        }

        /// <summary>RemoveUnequipped를 되돌린다(서버 저장 실패).</summary>
        public void Restore(InventoryItemStack stack)
        {
            items.Add(stack);
        }

        /// <summary>itemId의 미장착 스택 수량을 1 줄이고, 0이 되면 제거한다. 해당 스택이 없으면 false.</summary>
        public bool ConsumeOne(string itemId)
        {
            InventoryItemStack stack = FindUnequipped(itemId);
            if (stack == null)
            {
                return false;
            }

            stack.count--;
            if (stack.count <= 0)
            {
                items.Remove(stack);
            }

            return true;
        }
    }
}
