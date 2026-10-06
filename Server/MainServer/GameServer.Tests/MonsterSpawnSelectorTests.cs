using GameServer.Monsters;

namespace GameServer.Tests;

public class MonsterSpawnSelectorTests
{
    private static MonsterSpawnEntry Entry(string type, int weight) => new() { MonsterType = type, Weight = weight };

    [Fact]
    public void Pick_SingleEntry_AlwaysReturnsIt()
    {
        var entries = new List<MonsterSpawnEntry> { Entry("Orc", 1) };

        for (int i = 0; i < 20; i++)
        {
            Assert.Equal("Orc", MonsterSpawnSelector.Pick(entries, new Random(i)).MonsterType);
        }
    }

    [Fact]
    public void Pick_WeightedEntries_FollowsWeightRatio()
    {
        var entries = new List<MonsterSpawnEntry> { Entry("Common", 3), Entry("Rare", 1) };
        var random = new Random(12345);

        int commonCount = 0;
        const int trials = 20000;
        for (int i = 0; i < trials; i++)
        {
            if (MonsterSpawnSelector.Pick(entries, random).MonsterType == "Common")
            {
                commonCount++;
            }
        }

        // 기대 75% - 고정 시드라 결과는 결정적이며, 허용 오차는 넉넉하게 둔다.
        double ratio = (double)commonCount / trials;
        Assert.InRange(ratio, 0.72, 0.78);
    }

    [Fact]
    public void Pick_ZeroTotalWeight_Throws()
    {
        var entries = new List<MonsterSpawnEntry> { Entry("Orc", 0) };

        Assert.ThrowsAny<Exception>(() => MonsterSpawnSelector.Pick(entries, new Random(1)));
    }
}
