using GameServer.Maps;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Tests;

public class RoomChestStateTests
{
    private static MapChest Candidate(string id, string key, float x = 0f) =>
        new() { Id = id, X = x, Y = 0f, Z = 0f, Radius = 5f, LootTableKey = key };

    private static MapData CreateMapData(int candidateCount, int pick, float respawnSeconds, float despawnDelaySeconds) => new()
    {
        ChestCandidates = Enumerable.Range(0, candidateCount).Select(i => Candidate($"c{i}", "Basic", i)).ToList(),
        ChestSpawnCounts =
        {
            new MapChestSpawnCount { LootTableKey = "Basic", Count = pick, RespawnSeconds = respawnSeconds, DespawnDelaySeconds = despawnDelaySeconds }
        }
    };

    private sealed class Broadcasts
    {
        private readonly object _gate = new();
        private readonly List<(OpCode Op, byte[] Body)> _items = new();

        public void Add(OpCode op, byte[] body)
        {
            lock (_gate) _items.Add((op, body));
        }

        public List<(OpCode Op, byte[] Body)> Snapshot()
        {
            lock (_gate) return _items.ToList();
        }
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        int waited = 0;
        while (!condition() && waited < timeoutMs)
        {
            await Task.Delay(10);
            waited += 10;
        }

        Assert.True(condition(), "조건이 제한 시간 안에 만족되지 않았습니다.");
    }

    [Fact]
    public void Constructor_PicksRequestedCountOfCandidates()
    {
        var state = new RoomChestState(CreateMapData(6, 3, 0f, 0f), new Random(1), (_, _) => { }, CancellationToken.None);

        Assert.Equal(3, state.GetActiveSnapshot().Count);
    }

    [Fact]
    public void TryOpen_OutOfRange_Fails()
    {
        var state = new RoomChestState(CreateMapData(1, 1, 0f, 0f), new Random(1), (_, _) => { }, CancellationToken.None);

        Assert.False(state.TryOpen("c0", 1000f, 1000f, out _));
    }

    [Fact]
    public void TryOpen_UnknownOrNotActiveChest_Fails()
    {
        // 후보 3개 중 1개만 활성화 - 나머지 후보 id를 위조해 열려 해도 거부돼야 한다.
        var state = new RoomChestState(CreateMapData(3, 1, 0f, 0f), new Random(2), (_, _) => { }, CancellationToken.None);
        string activeId = state.GetActiveSnapshot()[0].Id;
        string inactiveId = new[] { "c0", "c1", "c2" }.First(id => id != activeId);

        Assert.False(state.TryOpen(inactiveId, 0f, 0f, out _));
        Assert.False(state.TryOpen("does_not_exist", 0f, 0f, out _));
    }

    [Fact]
    public void TryOpen_SecondOpenOfSameChest_Fails()
    {
        var state = new RoomChestState(CreateMapData(1, 1, 0f, 0f), new Random(3), (_, _) => { }, CancellationToken.None);

        Assert.True(state.TryOpen("c0", 0f, 0f, out _));
        Assert.False(state.TryOpen("c0", 0f, 0f, out _));
    }

    [Fact]
    public void SendState_ListsActiveChestsBeforeOpenedOnes()
    {
        var state = new RoomChestState(CreateMapData(1, 1, 0f, 0f), new Random(4), (_, _) => { }, CancellationToken.None);
        state.TryOpen("c0", 0f, 0f, out _);

        var sent = new List<OpCode>();
        state.SendState((op, _) => sent.Add(op));

        Assert.Equal(new[] { OpCode.Game_ActiveChestsNotify, OpCode.Game_ChestOpenBroadcast }, sent);
    }

    [Fact]
    public async Task Respawn_DespawnsOpenedChestThenSpawnsAnotherCandidateOfSameKey()
    {
        var broadcasts = new Broadcasts();
        var state = new RoomChestState(CreateMapData(4, 1, 0.3f, 0.1f), new Random(5), broadcasts.Add, CancellationToken.None);
        string openedId = state.GetActiveSnapshot()[0].Id;

        Assert.True(state.TryOpen(openedId, 0f, 0f, out _));

        await WaitUntil(() => broadcasts.Snapshot().Any(b => b.Op == OpCode.Game_ChestSpawnBroadcast));

        List<(OpCode Op, byte[] Body)> sent = broadcasts.Snapshot();
        Assert.Equal(new[] { OpCode.Game_ChestDespawnBroadcast, OpCode.Game_ChestSpawnBroadcast }, sent.Select(b => b.Op).ToArray());

        Assert.Equal(openedId, S2CChestDespawnBroadcast.Decode(sent[0].Body).ChestId);

        // 등급별 개수는 유지되고(1개), 새 상자는 방금 열린 자리가 아니다(다른 빈 후보가 있으므로).
        IReadOnlyList<MapChest> active = state.GetActiveSnapshot();
        Assert.Single(active);
        Assert.NotEqual(openedId, active[0].Id);
        Assert.Equal(active[0].Id, S2CChestSpawnBroadcast.Decode(sent[1].Body).Chest.Id);
    }

    [Fact]
    public async Task Respawn_OnlyOneCandidate_RespawnsAtSamePositionClosed()
    {
        var broadcasts = new Broadcasts();
        var state = new RoomChestState(CreateMapData(1, 1, 0.3f, 0.1f), new Random(6), broadcasts.Add, CancellationToken.None);

        Assert.True(state.TryOpen("c0", 0f, 0f, out _));
        await WaitUntil(() => broadcasts.Snapshot().Any(b => b.Op == OpCode.Game_ChestSpawnBroadcast));

        // 같은 id가 다시 서 있고, "열림" 기록은 지워져 다시 열 수 있어야 한다.
        Assert.Equal("c0", state.GetActiveSnapshot().Single().Id);
        Assert.True(state.TryOpen("c0", 0f, 0f, out _));
    }

    [Fact]
    public async Task Respawn_ClearsOpenedRecordSoNewJoinerDoesNotSeeItOpen()
    {
        var broadcasts = new Broadcasts();
        var state = new RoomChestState(CreateMapData(1, 1, 0.3f, 0.1f), new Random(7), broadcasts.Add, CancellationToken.None);

        state.TryOpen("c0", 0f, 0f, out _);
        await WaitUntil(() => broadcasts.Snapshot().Any(b => b.Op == OpCode.Game_ChestSpawnBroadcast));

        var sent = new List<OpCode>();
        state.SendState((op, _) => sent.Add(op));

        Assert.Equal(new[] { OpCode.Game_ActiveChestsNotify }, sent);
    }

    [Fact]
    public async Task Respawn_Disabled_KeepsOpenedChestForever()
    {
        var broadcasts = new Broadcasts();
        var state = new RoomChestState(CreateMapData(2, 1, 0f, 0f), new Random(8), broadcasts.Add, CancellationToken.None);
        string openedId = state.GetActiveSnapshot()[0].Id;

        state.TryOpen(openedId, 0f, 0f, out _);
        await Task.Delay(200);

        Assert.Empty(broadcasts.Snapshot());
        Assert.Equal(openedId, state.GetActiveSnapshot().Single().Id);
    }

    [Fact]
    public async Task Respawn_FixedChestIsNeverRespawned()
    {
        var broadcasts = new Broadcasts();
        MapData data = CreateMapData(2, 1, 0.3f, 0.1f);
        data.Chests.Add(Candidate("fixed", "Basic", 100f));
        var state = new RoomChestState(data, new Random(9), broadcasts.Add, CancellationToken.None);

        Assert.True(state.TryOpen("fixed", 100f, 0f, out _));
        await Task.Delay(500);

        Assert.Empty(broadcasts.Snapshot());
        Assert.Contains(state.GetActiveSnapshot(), c => c.Id == "fixed");
    }

    [Fact]
    public async Task Respawn_CancellationStopsPendingRespawn()
    {
        using var cts = new CancellationTokenSource();
        var broadcasts = new Broadcasts();
        var state = new RoomChestState(CreateMapData(3, 1, 0.5f, 0.2f), new Random(10), broadcasts.Add, cts.Token);
        string openedId = state.GetActiveSnapshot()[0].Id;

        state.TryOpen(openedId, 0f, 0f, out _);
        cts.Cancel();
        await Task.Delay(700);

        Assert.Empty(broadcasts.Snapshot());
    }

    [Fact]
    public async Task Respawn_ManyOpensKeepPerKeyCountConstant()
    {
        var broadcasts = new Broadcasts();
        var state = new RoomChestState(CreateMapData(8, 3, 0.2f, 0.1f), new Random(11), broadcasts.Add, CancellationToken.None);

        for (int round = 0; round < 3; round++)
        {
            foreach (MapChest chest in state.GetActiveSnapshot())
            {
                state.TryOpen(chest.Id, chest.X, 0f, out _);
            }

            await WaitUntil(() => state.GetActiveSnapshot().Count == 3 && state.GetActiveSnapshot().All(c => c.Id != null)
                && broadcasts.Snapshot().Count(b => b.Op == OpCode.Game_ChestSpawnBroadcast) >= (round + 1) * 3);

            IReadOnlyList<MapChest> active = state.GetActiveSnapshot();
            Assert.Equal(3, active.Count);
            Assert.Equal(3, active.Select(c => c.Id).Distinct().Count());
        }
    }
}

public class PickReplacementTests
{
    private static List<MapChest> Candidates() => new()
    {
        new MapChest { Id = "a", LootTableKey = "Basic" },
        new MapChest { Id = "b", LootTableKey = "Basic" },
        new MapChest { Id = "c", LootTableKey = "Rare" },
    };

    [Fact]
    public void PickReplacement_SkipsActiveAndOtherKeys()
    {
        var active = new HashSet<string> { "a" };

        MapChest? picked = ChestSpawnSelector.PickReplacement(Candidates(), active, "Basic", null, new Random(1));

        Assert.Equal("b", picked!.Id);
    }

    [Fact]
    public void PickReplacement_AvoidsPreferredNotIdWhenOthersAreFree()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            MapChest? picked = ChestSpawnSelector.PickReplacement(Candidates(), new HashSet<string>(), "Basic", "a", new Random(seed));
            Assert.Equal("b", picked!.Id);
        }
    }

    [Fact]
    public void PickReplacement_FallsBackToPreferredNotIdWhenItIsTheOnlyFree()
    {
        var active = new HashSet<string> { "b" };

        MapChest? picked = ChestSpawnSelector.PickReplacement(Candidates(), active, "Basic", "a", new Random(1));

        Assert.Equal("a", picked!.Id);
    }

    [Fact]
    public void PickReplacement_NoFreeCandidate_ReturnsNull()
    {
        var active = new HashSet<string> { "a", "b" };

        Assert.Null(ChestSpawnSelector.PickReplacement(Candidates(), active, "Basic", null, new Random(1)));
    }
}

public class ChestSpawnPacketTests
{
    [Fact]
    public void SpawnBroadcast_RoundTrips()
    {
        var packet = new S2CChestSpawnBroadcast
        {
            Chest = new ActiveChestInfo { Id = "TreasureChestSpawn007", X = 1f, Y = 2f, Z = 3f, LootTableKey = "TreasureChestHidden" }
        };

        S2CChestSpawnBroadcast decoded = S2CChestSpawnBroadcast.Decode(packet.Encode());

        Assert.Equal("TreasureChestSpawn007", decoded.Chest.Id);
        Assert.Equal(3f, decoded.Chest.Z);
        Assert.Equal("TreasureChestHidden", decoded.Chest.LootTableKey);
    }

    [Fact]
    public void DespawnBroadcast_RoundTrips()
    {
        S2CChestDespawnBroadcast decoded = S2CChestDespawnBroadcast.Decode(new S2CChestDespawnBroadcast { ChestId = "c1" }.Encode());

        Assert.Equal("c1", decoded.ChestId);
    }
}
