using GameServer.Items;

namespace GameServer.Tests;

// ItemCatalog는 static 클래스라 테스트 프로세스 전체에서 한 번만 로드된다(EnsureLoaded가 첫 호출 이후 캐시).
// Items/ItemDefinitions.json 픽스처에 Rare 2종(test_weapon_regression, test_armor_regression),
// Common 1종(test_weapon_common)을 등록해뒀고 Epic/Legendary는 없다.
public class ItemCatalogTests
{
    [Fact]
    public void TryGetRandomByGrade_ReturnsOnlyItemsOfThatGrade()
    {
        var rareIds = new HashSet<string> { "test_weapon_regression", "test_armor_regression" };

        for (int i = 0; i < 20; i++)
        {
            bool found = ItemCatalog.TryGetRandomByGrade("Rare", out string itemId);

            Assert.True(found);
            Assert.Contains(itemId, rareIds);
        }
    }

    [Fact]
    public void TryGetRandomByGrade_SingleItemGrade_AlwaysReturnsThatItem()
    {
        bool found = ItemCatalog.TryGetRandomByGrade("Common", out string itemId);

        Assert.True(found);
        Assert.Equal("test_weapon_common", itemId);
    }

    [Fact]
    public void TryGetRandomByGrade_UnknownGrade_ReturnsFalse()
    {
        bool found = ItemCatalog.TryGetRandomByGrade("Legendary", out string itemId);

        Assert.False(found);
        Assert.Equal(string.Empty, itemId);
    }

    [Fact]
    public void HasAnyOfGrade_MatchesTryGetRandomByGrade()
    {
        Assert.True(ItemCatalog.HasAnyOfGrade("Rare"));
        Assert.True(ItemCatalog.HasAnyOfGrade("Common"));
        Assert.False(ItemCatalog.HasAnyOfGrade("Legendary"));
    }
}

// 물약 재사용 대기시간(UseCooldownSeconds) 정의가 서버 아이템 정의에서 읽히는지. 픽스처의 Epic 물약 1종(6.5초)을 쓴다.
public class ItemCooldownDefinitionTests
{
    [Fact]
    public void TryGet_Potion_ReadsUseCooldownSeconds()
    {
        Assert.True(ItemCatalog.TryGet("test_potion_cooldown", out ItemDefinition definition));

        Assert.Equal(30, definition.HealPercent);
        Assert.Equal(6.5f, definition.UseCooldownSeconds);
    }

    [Fact]
    public void TryGet_ItemWithoutCooldown_DefaultsToZero()
    {
        Assert.True(ItemCatalog.TryGet("test_weapon_common", out ItemDefinition definition));

        Assert.Equal(0f, definition.UseCooldownSeconds);
    }
}
