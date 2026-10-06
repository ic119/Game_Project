using System.Text.Json;
using GameServer.Monsters;

namespace GameServer.Tests;

public class SpawnAreaTests
{
    private static SpawnArea Room() => new() { CenterX = 10f, CenterZ = -20f, SizeX = 8f, SizeZ = 6f };

    [Theory]
    [InlineData(10f, -20f, true)]   // 중심
    [InlineData(14f, -17f, true)]   // 모서리(경계 포함)
    [InlineData(14.1f, -20f, false)]
    [InlineData(10f, -23.1f, false)]
    [InlineData(5.9f, -20f, false)]
    public void Contains_UsesCenterAndFullSize(float x, float z, bool expected)
    {
        Assert.Equal(expected, Room().Contains(x, z));
    }

    [Fact]
    public void Clamp_PullsOutsidePointToNearestEdge()
    {
        (float x, float z) = Room().Clamp(100f, -100f);

        Assert.Equal(14f, x);
        Assert.Equal(-23f, z);
    }

    [Fact]
    public void Clamp_InsidePoint_IsUnchanged()
    {
        (float x, float z) = Room().Clamp(11f, -19f);

        Assert.Equal(11f, x);
        Assert.Equal(-19f, z);
    }

    [Theory]
    [InlineData(0f, 5f)]
    [InlineData(5f, 0f)]
    [InlineData(-1f, 5f)]
    public void IsDefined_RequiresPositiveSize(float sizeX, float sizeZ)
    {
        Assert.False(new SpawnArea { SizeX = sizeX, SizeZ = sizeZ }.IsDefined);
    }

    [Fact]
    public void AllowsPosition_NoAreaOrZeroSizeArea_AllowsEverywhere()
    {
        var noArea = new MonsterSpawnPointDefinition();
        var zeroArea = new MonsterSpawnPointDefinition { Area = new SpawnArea() };

        Assert.True(noArea.AllowsPosition(9999f, -9999f));
        Assert.True(zeroArea.AllowsPosition(9999f, -9999f));
        Assert.Equal((9999f, -9999f), noArea.ConstrainToArea(9999f, -9999f));
        Assert.Equal((9999f, -9999f), zeroArea.ConstrainToArea(9999f, -9999f));
    }

    [Fact]
    public void AllowsPosition_WithArea_RestrictsToArea()
    {
        var point = new MonsterSpawnPointDefinition { Area = Room() };

        Assert.True(point.AllowsPosition(10f, -20f));
        Assert.False(point.AllowsPosition(30f, -20f));
        Assert.Equal((14f, -20f), point.ConstrainToArea(30f, -20f));
    }

    // Unity MonsterSpawnPointExporter가 내보내는 camelCase JSON 형식이 서버 모델로 그대로 읽히는지 확인한다.
    [Fact]
    public void Deserialize_ExporterFormat_ReadsAreaAndEntries()
    {
        const string json = """
            { "points": [ {
                "pointId": "Dungeon_Room1", "x": 1.0, "y": 0.0, "z": 2.0, "rotationY": 0.0,
                "maxAlive": 2, "respawnSeconds": 60.0,
                "entries": [ { "monsterType": "Spider", "weight": 3 } ],
                "area": { "centerX": 1.0, "centerZ": 2.0, "sizeX": 8.0, "sizeZ": 6.0 }
            } ] }
            """;

        MonsterSpawnPointFile file = JsonSerializer.Deserialize<MonsterSpawnPointFile>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        MonsterSpawnPointDefinition point = file.Points.Single();
        Assert.Equal("Spider", point.Entries.Single().MonsterType);
        Assert.Equal(3, point.Entries.Single().Weight);
        Assert.NotNull(point.Area);
        Assert.True(point.Area!.IsDefined);
        Assert.True(point.AllowsPosition(1f, 2f));
        Assert.False(point.AllowsPosition(20f, 2f));
    }
}
