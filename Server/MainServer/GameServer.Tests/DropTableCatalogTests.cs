using GameServer.Monsters;

namespace GameServer.Tests;

// Drops/DropTables.json 픽스처에 "TestChest" 하나만 등록해뒀다(등급 Rare, dropRate 1.0 - 항상 드롭).
public class DropTableCatalogTests
{
    [Fact]
    public void HasTable_KnownKey_ReturnsTrue()
    {
        Assert.True(DropTableCatalog.HasTable("TestChest"));
    }

    [Fact]
    public void HasTable_UnknownKey_ReturnsFalse()
    {
        Assert.False(DropTableCatalog.HasTable("NoSuchTable"));
    }

    [Fact]
    public void Roll_AlwaysDropRate_ReturnsGradeItem()
    {
        (int gold, List<(string ItemId, int Qty)> items) = DropTableCatalog.Roll("TestChest");

        Assert.Equal(1, gold);
        Assert.Single(items);
        // ItemCatalogTests 픽스처 기준 Rare 등급은 test_weapon_regression/test_armor_regression 둘뿐이다.
        Assert.Contains(items[0].ItemId, new[] { "test_weapon_regression", "test_armor_regression" });
    }
}
