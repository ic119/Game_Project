using GameServer.Maps;

namespace GameServer.Tests;

public class ChestSpawnSelectorTests
{
    private static List<MapChest> CreateCandidates(string lootTableKey, int count, string idPrefix) =>
        Enumerable.Range(0, count)
            .Select(i => new MapChest { Id = $"{idPrefix}{i}", LootTableKey = lootTableKey })
            .ToList();

    [Fact]
    public void Select_PicksRequestedCountWithoutDuplicates()
    {
        List<MapChest> candidates = CreateCandidates("Basic", 10, "b");
        var counts = new List<MapChestSpawnCount> { new() { LootTableKey = "Basic", Count = 4 } };

        List<MapChest> selected = ChestSpawnSelector.Select(candidates, counts, new Random(1));

        Assert.Equal(4, selected.Count);
        Assert.Equal(4, selected.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Select_OnlyPicksCandidatesOfTheSameLootTableKey()
    {
        List<MapChest> candidates = CreateCandidates("Basic", 5, "b");
        candidates.AddRange(CreateCandidates("Hidden", 5, "h"));
        var counts = new List<MapChestSpawnCount>
        {
            new() { LootTableKey = "Basic", Count = 2 },
            new() { LootTableKey = "Hidden", Count = 3 }
        };

        List<MapChest> selected = ChestSpawnSelector.Select(candidates, counts, new Random(2));

        Assert.Equal(2, selected.Count(c => c.LootTableKey == "Basic"));
        Assert.Equal(3, selected.Count(c => c.LootTableKey == "Hidden"));
    }

    [Fact]
    public void Select_CountLargerThanPool_ClampsToPoolSize()
    {
        List<MapChest> candidates = CreateCandidates("Basic", 3, "b");
        var counts = new List<MapChestSpawnCount> { new() { LootTableKey = "Basic", Count = 10 } };

        List<MapChest> selected = ChestSpawnSelector.Select(candidates, counts, new Random(3));

        Assert.Equal(3, selected.Count);
    }

    [Fact]
    public void Select_DoesNotMutateOriginalCandidateOrder()
    {
        List<MapChest> candidates = CreateCandidates("Basic", 8, "b");
        string[] before = candidates.Select(c => c.Id).ToArray();
        var counts = new List<MapChestSpawnCount> { new() { LootTableKey = "Basic", Count = 3 } };

        ChestSpawnSelector.Select(candidates, counts, new Random(4));

        Assert.Equal(before, candidates.Select(c => c.Id).ToArray());
    }

    [Fact]
    public void Select_SameSeed_IsReproducible()
    {
        List<MapChest> candidates = CreateCandidates("Basic", 10, "b");
        var counts = new List<MapChestSpawnCount> { new() { LootTableKey = "Basic", Count = 4 } };

        string[] first = ChestSpawnSelector.Select(candidates, counts, new Random(7)).Select(c => c.Id).ToArray();
        string[] second = ChestSpawnSelector.Select(candidates, counts, new Random(7)).Select(c => c.Id).ToArray();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Select_EveryCandidateCanBePickedOverManyRuns()
    {
        List<MapChest> candidates = CreateCandidates("Basic", 6, "b");
        var counts = new List<MapChestSpawnCount> { new() { LootTableKey = "Basic", Count = 2 } };
        var random = new Random(11);
        var seen = new HashSet<string>();

        for (int i = 0; i < 200; i++)
        {
            foreach (MapChest chest in ChestSpawnSelector.Select(candidates, counts, random))
            {
                seen.Add(chest.Id);
            }
        }

        Assert.Equal(6, seen.Count);
    }
}
