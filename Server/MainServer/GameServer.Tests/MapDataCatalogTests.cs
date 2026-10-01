using GameServer.Maps;

namespace GameServer.Tests;

// 도착 맵 데이터가 없는 맵 이동 포탈을 찾는 규칙. 목적지 맵이 아직 준비되지 않은 단계에서 포탈을 먼저 내보내도
// 서버가 부팅은 되고, 어떤 포탈이 문제인지 알려 주는지 확인한다.
public class MapDataCatalogTests
{
    private static MapData MapWithPortals(params MapPortal[] portals)
    {
        return new MapData
        {
            RespawnPoint = new MapPoint { X = 0, Y = 0, Z = 0, RotationY = 0 },
            Portals = portals.ToList()
        };
    }

    private static MapPortal MapSwapTo(string targetMapId, float x = 10f, float z = 20f)
    {
        return new MapPortal
        {
            Type = MapPortal.MapSwapType,
            TargetMapId = targetMapId,
            X = x,
            Z = z,
            Radius = 1f,
            Destination = new MapPoint { X = 1, Y = 0, Z = 1 }
        };
    }

    [Fact]
    public void NoPortals_NoProblems()
    {
        var maps = new Dictionary<string, MapData> { ["Floor001"] = MapWithPortals() };

        Assert.Empty(MapDataCatalog.FindPortalsWithMissingTargetMap(maps));
    }

    [Fact]
    public void PortalToLoadedMap_IsFine()
    {
        var maps = new Dictionary<string, MapData>
        {
            ["Floor001"] = MapWithPortals(MapSwapTo("Floor002")),
            ["Floor002"] = MapWithPortals()
        };

        Assert.Empty(MapDataCatalog.FindPortalsWithMissingTargetMap(maps));
    }

    [Fact]
    public void PortalToMapWithoutData_IsReported()
    {
        var maps = new Dictionary<string, MapData>
        {
            ["Floor001"] = MapWithPortals(MapSwapTo("Floor002", x: 12.5f, z: -3f))
        };

        List<string> problems = MapDataCatalog.FindPortalsWithMissingTargetMap(maps);

        string problem = Assert.Single(problems);
        Assert.Contains("Floor001", problem);
        Assert.Contains("Floor002", problem);
    }

    [Fact]
    public void CoordinateTeleportPortals_AreNotChecked()
    {
        // 같은 맵 안 순간이동 포탈은 도착 맵이 없다(TargetMapId가 비어 있음) - 문제로 보지 않는다.
        var teleport = new MapPortal
        {
            Type = MapPortal.CoordinateTeleportType,
            X = 1,
            Z = 1,
            Radius = 1f,
            Destination = new MapPoint { X = 5, Z = 5 }
        };
        var maps = new Dictionary<string, MapData> { ["Floor001"] = MapWithPortals(teleport) };

        Assert.Empty(MapDataCatalog.FindPortalsWithMissingTargetMap(maps));
    }

    [Fact]
    public void OnlyTheMissingPortalsAreReported()
    {
        var maps = new Dictionary<string, MapData>
        {
            ["Floor001"] = MapWithPortals(MapSwapTo("Floor002"), MapSwapTo("Floor003"), MapSwapTo("Floor002")),
            ["Floor002"] = MapWithPortals()
        };

        List<string> problems = MapDataCatalog.FindPortalsWithMissingTargetMap(maps);

        string problem = Assert.Single(problems);
        Assert.Contains("Floor003", problem);
    }
}
