using GameServer.Maps;

namespace GameServer.Tests;

public class MapChestTests
{
    private static MapChest CreateChest(float radius) => new()
    {
        Id = "chest_test",
        X = 10f,
        Y = 0f,
        Z = 10f,
        Radius = radius,
        LootTableKey = "TestChest"
    };

    [Fact]
    public void IsWithinRange_ExactCenter_IsTrue()
    {
        MapChest chest = CreateChest(radius: 2f);

        Assert.True(chest.IsWithinRange(10f, 10f));
    }

    [Fact]
    public void IsWithinRange_JustInsideRadiusPlusTolerance_IsTrue()
    {
        // Radius 2 + EntryTolerance 1.5 = 3.5 허용 거리. 3.4만큼 떨어진 지점은 안이어야 한다.
        MapChest chest = CreateChest(radius: 2f);

        Assert.True(chest.IsWithinRange(10f + 3.4f, 10f));
    }

    [Fact]
    public void IsWithinRange_FarBeyondRadiusAndTolerance_IsFalse()
    {
        MapChest chest = CreateChest(radius: 2f);

        Assert.False(chest.IsWithinRange(10f + 10f, 10f));
    }
}
