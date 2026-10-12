using System.Linq;
using Incheol.Presenter.Scene;
using NUnit.Framework;

namespace Incheol.Tests
{
    public class LocalInventoryTests
    {
        private const string Weapon = "Weapon";
        private const string Armor = "Armor";

        private LocalInventory inventory;

        [SetUp]
        public void CreateInventory()
        {
            inventory = new LocalInventory();
        }

        #region Replace / AddOrMerge
        [Test]
        public void Replace_SwapsWholeList()
        {
            inventory.AddOrMerge("old", 1);

            inventory.Replace(new[] { new InventoryItemStack("a", 2), new InventoryItemStack("b", 3) });

            Assert.AreEqual(2, inventory.Items.Count);
            Assert.IsNull(inventory.FindUnequipped("old"));
            Assert.AreEqual(2, inventory.FindUnequipped("a").count);
        }

        [Test]
        public void AddOrMerge_SameItemId_SumsQuantity()
        {
            inventory.AddOrMerge("potion", 2);
            inventory.AddOrMerge("potion", 3);

            Assert.AreEqual(1, inventory.Items.Count);
            Assert.AreEqual(5, inventory.Items[0].count);
        }

        [Test]
        public void AddOrMerge_NewItemId_AddsStack()
        {
            inventory.AddOrMerge("potion", 2);
            inventory.AddOrMerge("sword", 1);

            Assert.AreEqual(2, inventory.Items.Count);
        }
        #endregion

        #region TryEquip / RevertEquip
        [Test]
        public void TryEquip_SetsSlotOnTarget()
        {
            inventory.AddOrMerge("sword", 1);

            bool ok = inventory.TryEquip("sword", Weapon, out var target, out var previous);

            Assert.IsTrue(ok);
            Assert.AreEqual(Weapon, target.equipSlot);
            Assert.IsNull(previous);
        }

        [Test]
        public void TryEquip_SlotOccupied_UnequipsPreviousAndReturnsIt()
        {
            inventory.Replace(new[]
            {
                new InventoryItemStack("sword_a", 1, Weapon),
                new InventoryItemStack("sword_b", 1),
            });

            bool ok = inventory.TryEquip("sword_b", Weapon, out var target, out var previous);

            Assert.IsTrue(ok);
            Assert.AreEqual("sword_b", target.itemId);
            Assert.AreEqual(Weapon, target.equipSlot);
            Assert.AreEqual("sword_a", previous.itemId);
            Assert.IsNull(previous.equipSlot);
        }

        [Test]
        public void TryEquip_NoUnequippedStack_ReturnsFalse()
        {
            bool ok = inventory.TryEquip("sword", Weapon, out var target, out var previous);

            Assert.IsFalse(ok);
            Assert.IsNull(target);
            Assert.IsNull(previous);
        }

        [Test]
        public void TryEquip_OnlyEquippedStackExists_ReturnsFalse()
        {
            inventory.Replace(new[] { new InventoryItemStack("sword", 1, Weapon) });

            bool ok = inventory.TryEquip("sword", Weapon, out _, out _);

            Assert.IsFalse(ok);
            Assert.AreEqual(Weapon, inventory.Items[0].equipSlot);
        }

        [Test]
        public void TryEquip_DifferentSlots_DoNotDisturbEachOther()
        {
            inventory.Replace(new[]
            {
                new InventoryItemStack("armor", 1, Armor),
                new InventoryItemStack("sword", 1),
            });

            inventory.TryEquip("sword", Weapon, out _, out var previous);

            Assert.IsNull(previous);
            Assert.AreEqual(Armor, inventory.Items.First(s => s.itemId == "armor").equipSlot);
        }

        [Test]
        public void RevertEquip_RestoresStateBeforeEquip()
        {
            var equipped = new InventoryItemStack("sword_a", 1, Weapon);
            var candidate = new InventoryItemStack("sword_b", 1);
            inventory.Replace(new[] { equipped, candidate });

            inventory.TryEquip("sword_b", Weapon, out var target, out var previous);
            inventory.RevertEquip(target, previous, Weapon);

            Assert.AreSame(equipped, previous);
            Assert.AreEqual(Weapon, equipped.equipSlot);
            Assert.IsNull(candidate.equipSlot);
        }

        [Test]
        public void RevertEquip_WithoutPrevious_JustUnequipsTarget()
        {
            inventory.AddOrMerge("sword", 1);

            inventory.TryEquip("sword", Weapon, out var target, out var previous);
            inventory.RevertEquip(target, previous, Weapon);

            Assert.IsNull(target.equipSlot);
            Assert.AreEqual(0, inventory.EquippedStacks().Count());
        }
        #endregion

        #region TryUnequip / RevertUnequip
        [Test]
        public void TryUnequip_ClearsSlotAndReturnsStack()
        {
            inventory.Replace(new[] { new InventoryItemStack("sword", 1, Weapon) });

            bool ok = inventory.TryUnequip(Weapon, out var stack);

            Assert.IsTrue(ok);
            Assert.AreEqual("sword", stack.itemId);
            Assert.IsNull(stack.equipSlot);
        }

        [Test]
        public void TryUnequip_EmptySlot_ReturnsFalse()
        {
            inventory.AddOrMerge("sword", 1);

            bool ok = inventory.TryUnequip(Weapon, out var stack);

            Assert.IsFalse(ok);
            Assert.IsNull(stack);
        }

        [Test]
        public void RevertUnequip_ReEquipsStack()
        {
            inventory.Replace(new[] { new InventoryItemStack("sword", 1, Weapon) });

            inventory.TryUnequip(Weapon, out var stack);
            inventory.RevertUnequip(stack, Weapon);

            Assert.AreEqual(Weapon, stack.equipSlot);
        }
        #endregion

        #region RemoveUnequipped / Restore
        [Test]
        public void RemoveUnequipped_RemovesWholeStack()
        {
            inventory.AddOrMerge("stone", 7);

            InventoryItemStack removed = inventory.RemoveUnequipped("stone");

            Assert.AreEqual(7, removed.count);
            Assert.AreEqual(0, inventory.Items.Count);
        }

        [Test]
        public void RemoveUnequipped_MissingItem_ReturnsNull()
        {
            Assert.IsNull(inventory.RemoveUnequipped("stone"));
        }

        [Test]
        public void RemoveUnequipped_LeavesEquippedStackWithSameItemId()
        {
            var equipped = new InventoryItemStack("sword", 1, Weapon);
            var spare = new InventoryItemStack("sword", 1);
            inventory.Replace(new[] { equipped, spare });

            InventoryItemStack removed = inventory.RemoveUnequipped("sword");

            Assert.AreSame(spare, removed);
            Assert.AreEqual(1, inventory.Items.Count);
            Assert.AreSame(equipped, inventory.Items[0]);
        }

        [Test]
        public void Restore_PutsRemovedStackBack()
        {
            inventory.AddOrMerge("stone", 7);

            InventoryItemStack removed = inventory.RemoveUnequipped("stone");
            inventory.Restore(removed);

            Assert.AreSame(removed, inventory.FindUnequipped("stone"));
        }
        #endregion

        #region ConsumeOne
        [Test]
        public void ConsumeOne_DecrementsQuantity()
        {
            inventory.AddOrMerge("potion", 3);

            bool ok = inventory.ConsumeOne("potion");

            Assert.IsTrue(ok);
            Assert.AreEqual(2, inventory.FindUnequipped("potion").count);
        }

        [Test]
        public void ConsumeOne_LastOne_RemovesStack()
        {
            inventory.AddOrMerge("potion", 1);

            bool ok = inventory.ConsumeOne("potion");

            Assert.IsTrue(ok);
            Assert.AreEqual(0, inventory.Items.Count);
        }

        [Test]
        public void ConsumeOne_MissingItem_ReturnsFalse()
        {
            Assert.IsFalse(inventory.ConsumeOne("potion"));
        }

        [Test]
        public void ConsumeOne_IgnoresEquippedStack()
        {
            inventory.Replace(new[] { new InventoryItemStack("sword", 1, Weapon) });

            Assert.IsFalse(inventory.ConsumeOne("sword"));
            Assert.AreEqual(1, inventory.Items[0].count);
        }
        #endregion

        #region FindUnequipped / EquippedStacks
        [Test]
        public void FindUnequipped_TreatsNullAndEmptySlotAsUnequipped()
        {
            inventory.Replace(new[]
            {
                new InventoryItemStack("a", 1, null),
                new InventoryItemStack("b", 1, string.Empty),
            });

            Assert.IsNotNull(inventory.FindUnequipped("a"));
            Assert.IsNotNull(inventory.FindUnequipped("b"));
            Assert.AreEqual(0, inventory.EquippedStacks().Count());
        }

        [Test]
        public void EquippedStacks_ReturnsOnlyEquipped()
        {
            inventory.Replace(new[]
            {
                new InventoryItemStack("sword", 1, Weapon),
                new InventoryItemStack("potion", 5),
                new InventoryItemStack("armor", 1, Armor),
            });

            var ids = inventory.EquippedStacks().Select(s => s.itemId).ToList();

            CollectionAssert.AreEquivalent(new[] { "sword", "armor" }, ids);
        }
        #endregion

        #region SumEquipmentBonus
        [Test]
        public void SumEquipmentBonus_SumsOnlyEquippedStacks()
        {
            inventory.Replace(new[]
            {
                new InventoryItemStack("sword", 1, Weapon),
                new InventoryItemStack("armor", 1, Armor),
                new InventoryItemStack("spare_sword", 1),
            });

            inventory.SumEquipmentBonus(Lookup, out int attack, out int defense);

            Assert.AreEqual(5, attack);
            Assert.AreEqual(3, defense);
        }

        [Test]
        public void SumEquipmentBonus_SkipsStackWithoutItemData()
        {
            inventory.Replace(new[]
            {
                new InventoryItemStack("sword", 1, Weapon),
                new InventoryItemStack("unknown", 1, Armor),
            });

            inventory.SumEquipmentBonus(Lookup, out int attack, out int defense);

            Assert.AreEqual(5, attack);
            Assert.AreEqual(0, defense);
        }

        [Test]
        public void SumEquipmentBonus_NothingEquipped_IsZero()
        {
            inventory.AddOrMerge("sword", 1);

            inventory.SumEquipmentBonus(Lookup, out int attack, out int defense);

            Assert.AreEqual(0, attack);
            Assert.AreEqual(0, defense);
        }

        private static ItemData Lookup(string itemId)
        {
            switch (itemId)
            {
                case "sword":
                case "spare_sword":
                    return new ItemData { itemId = itemId, bonusAttackPower = 5 };
                case "armor":
                    return new ItemData { itemId = itemId, bonusDefense = 3 };
                default:
                    return null;
            }
        }
        #endregion
    }
}
