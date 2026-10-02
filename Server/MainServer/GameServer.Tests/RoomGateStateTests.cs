using GameServer.Maps;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Tests;

public class RoomGateStateTests
{
    private static List<MapGateCandidate> CreateCandidates(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new MapGateCandidate { Id = $"DungeonGateSpawn{i:000}", X = i * 10f, Y = 0f, Z = i * -5f, Radius = 2.5f })
            .ToList();

    private static MapData MapWithGate(int candidateCount) => new()
    {
        RespawnPoint = new MapPoint(),
        GateCandidates = CreateCandidates(candidateCount),
        GatePlan = new MapGatePlan
        {
            TargetMapId = "Dungeon",
            Destination = new MapPoint { X = 1f, Y = 2f, Z = 3f, RotationY = 90f }
        }
    };

    [Fact]
    public void Select_ReturnsNullWhenThereAreNoCandidates()
    {
        Assert.Null(RoomGateState.Select(new List<MapGateCandidate>(), new Random(1)));
    }

    [Fact]
    public void Select_PicksExactlyOneOfTheCandidates()
    {
        List<MapGateCandidate> candidates = CreateCandidates(4);

        for (int seed = 0; seed < 50; seed++)
        {
            MapGateCandidate? selected = RoomGateState.Select(candidates, new Random(seed));

            Assert.NotNull(selected);
            Assert.Contains(selected, candidates);
        }
    }

    [Fact]
    public void Select_EventuallyReachesEveryCandidate()
    {
        List<MapGateCandidate> candidates = CreateCandidates(4);
        var random = new Random(7);

        var seen = Enumerable.Range(0, 200)
            .Select(_ => RoomGateState.Select(candidates, random)!.Id)
            .ToHashSet();

        Assert.Equal(candidates.Select(c => c.Id).ToHashSet(), seen);
    }

    [Fact]
    public void Constructor_WithoutMapData_HasNoGate()
    {
        var state = new RoomGateState(null, new Random(1));

        Assert.Null(state.ActiveGate);
        Assert.Null(state.ActivePortal);
    }

    [Fact]
    public void Constructor_WithoutCandidates_HasNoGate()
    {
        var state = new RoomGateState(MapWithGate(0), new Random(1));

        Assert.Null(state.ActiveGate);
        Assert.Null(state.ActivePortal);
    }

    [Fact]
    public void ActivePortal_IsAMapSwapPortalAtTheChosenCandidate()
    {
        var state = new RoomGateState(MapWithGate(4), new Random(3));

        MapGateCandidate gate = Assert.IsType<MapGateCandidate>(state.ActiveGate);
        MapPortal portal = Assert.IsType<MapPortal>(state.ActivePortal);

        Assert.Equal(MapPortal.MapSwapType, portal.Type);
        Assert.Equal("Dungeon", portal.TargetMapId);
        Assert.Equal((gate.X, gate.Y, gate.Z, gate.Radius), (portal.X, portal.Y, portal.Z, portal.Radius));
        Assert.Equal(1f, portal.Destination!.X);
        Assert.Equal(90f, portal.Destination.RotationY);
    }

    [Fact]
    public void ActivePortal_AcceptsOnlyThePlayersNearTheChosenGate()
    {
        var state = new RoomGateState(MapWithGate(4), new Random(3));
        MapGateCandidate chosen = state.ActiveGate!;
        MapGateCandidate other = CreateCandidates(4).First(c => c.Id != chosen.Id);

        Assert.True(state.ActivePortal!.IsWithinRange(chosen.X, chosen.Z));
        Assert.False(state.ActivePortal.IsWithinRange(other.X, other.Z));
    }

    [Fact]
    public void SendState_SendsTheChosenGate()
    {
        var state = new RoomGateState(MapWithGate(4), new Random(3));
        var sent = new List<(OpCode OpCode, byte[] Body)>();

        state.SendState((opCode, body) => sent.Add((opCode, body)));

        var (opCode, body) = Assert.Single(sent);
        Assert.Equal(OpCode.Game_ActiveGateNotify, opCode);
        S2CActiveGate packet = S2CActiveGate.Decode(body);
        Assert.True(packet.HasGate);
        Assert.Equal(state.ActiveGate!.Id, packet.Id);
        Assert.Equal(state.ActiveGate.X, packet.X);
        Assert.Equal(state.ActiveGate.Z, packet.Z);
    }

    [Fact]
    public void SendState_WithoutAGate_StillSendsAnEmptyNotice()
    {
        var state = new RoomGateState(MapWithGate(0), new Random(1));
        var sent = new List<(OpCode OpCode, byte[] Body)>();

        state.SendState((opCode, body) => sent.Add((opCode, body)));

        var (opCode, body) = Assert.Single(sent);
        Assert.Equal(OpCode.Game_ActiveGateNotify, opCode);
        Assert.False(S2CActiveGate.Decode(body).HasGate);
    }

    [Fact]
    public void GateToMapWithoutData_IsReported()
    {
        var maps = new Dictionary<string, MapData> { ["Floor001"] = MapWithGate(3) };

        string problem = Assert.Single(MapDataCatalog.FindPortalsWithMissingTargetMap(maps));

        Assert.Contains("Floor001", problem);
        Assert.Contains("Dungeon", problem);
    }

    [Fact]
    public void GateToLoadedMap_IsFine()
    {
        var maps = new Dictionary<string, MapData>
        {
            ["Floor001"] = MapWithGate(3),
            ["Dungeon"] = new MapData { RespawnPoint = new MapPoint() }
        };

        Assert.Empty(MapDataCatalog.FindPortalsWithMissingTargetMap(maps));
    }
}
