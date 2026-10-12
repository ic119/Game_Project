using System.Collections.Generic;
using Incheol.View.UI;
using NUnit.Framework;

namespace Incheol.Tests
{
    public class InventoryStackPartitionerTests
    {
        private static void Run(IReadOnlyList<InventoryItemStack> items,
            out List<InventoryItemStack> unequipped,
            out Dictionary<EquipmentSlotType, InventoryItemStack> equipped)
        {
            InventoryStackPartitioner.Partition(items, out unequipped, out equipped);
        }

        [Test]
        public void NullItems_ReturnsEmptyResults()
        {
            Run(null, out var unequipped, out var equipped);

            Assert.IsNotNull(unequipped);
            Assert.IsNotNull(equipped);
            Assert.AreEqual(0, unequipped.Count);
            Assert.AreEqual(0, equipped.Count);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Boots")] // 제거된 슬롯 이름 = 알 수 없는 이름
        [TestCase("NotASlot")]
        [TestCase("None")]
        public void UnusableEquipSlot_IsTreatedAsUnequipped(string equipSlot)
        {
            var stack = new InventoryItemStack("item", 1, equipSlot);

            Run(new[] { stack }, out var unequipped, out var equipped);

            Assert.AreEqual(1, unequipped.Count);
            Assert.AreSame(stack, unequipped[0]);
            Assert.AreEqual(0, equipped.Count);
        }

        [TestCase("Weapon", EquipmentSlotType.Weapon)]
        [TestCase("Armor", EquipmentSlotType.Armor)]
        [TestCase("Helmet", EquipmentSlotType.Helmet)]
        [TestCase("Accessory", EquipmentSlotType.Accessory)]
        public void ValidEquipSlot_GoesToThatSlot(string equipSlot, EquipmentSlotType expected)
        {
            var stack = new InventoryItemStack("item", 1, equipSlot);

            Run(new[] { stack }, out var unequipped, out var equipped);

            Assert.AreEqual(0, unequipped.Count);
            Assert.AreSame(stack, equipped[expected]);
        }

        [Test]
        public void DuplicateSlot_LastStackWins()
        {
            var first = new InventoryItemStack("sword_a", 1, "Weapon");
            var second = new InventoryItemStack("sword_b", 1, "Weapon");

            Run(new[] { first, second }, out var unequipped, out var equipped);

            Assert.AreEqual(1, equipped.Count);
            Assert.AreSame(second, equipped[EquipmentSlotType.Weapon]);
            Assert.AreEqual(0, unequipped.Count);
        }

        [Test]
        public void MixedItems_AreSplitAndKeepOrder()
        {
            var potion = new InventoryItemStack("potion", 3);
            var sword = new InventoryItemStack("sword", 1, "Weapon");
            var stone = new InventoryItemStack("stone", 9);
            var armor = new InventoryItemStack("armor", 1, "Armor");

            Run(new[] { potion, sword, stone, armor }, out var unequipped, out var equipped);

            CollectionAssert.AreEqual(new[] { potion, stone }, unequipped);
            Assert.AreEqual(2, equipped.Count);
            Assert.AreSame(sword, equipped[EquipmentSlotType.Weapon]);
            Assert.AreSame(armor, equipped[EquipmentSlotType.Armor]);
        }
    }
}
