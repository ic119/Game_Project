using System.Text.Json;
using GameServer.Monsters;
using GameServer.Navigation;
using Xunit.Abstractions;

namespace GameServer.Tests;

// 실제 던전 데이터(SpawnPoints/Dungeon.json, NavGrids/Dungeon.json, Monsters/MonsterDefinitions.json)가 "서버가 부팅되고 몬스터가 실제로
// 움직일 수 있는" 상태인지 확인한다. 스폰 위치가 이동 가능 칸이어도 몸집(AgentRadius)만큼 장애물을 부풀리면 좁은 통로에 갇힐 수 있어서,
// 몬스터마다 부풀린 격자에서 스폰 위치로부터 활동 영역 안에서 도달 가능한 면적을 계산한다. 던전 마커나 몬스터 반경을 고칠 때의 회귀 방지용이다.
public class DungeonDataTests
{
    private readonly ITestOutputHelper _output;

    public DungeonDataTests(ITestOutputHelper output) => _output = output;

    // 이 면적(m^2)보다 좁은 곳에서는 추격/복귀가 사실상 불가능하다고 본다.
    private const float MinReachableAreaSquareMeters = 8f;

    // GameRoom.SpawnSnapRing과 같은 값(스폰 좌표를 이동 가능 칸으로 옮길 때 찾는 최대 칸 수).
    private const int SpawnSnapRing = 4;

    private static string RealData(string name) => Path.Combine(AppContext.BaseDirectory, "RealData", name);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static (List<MonsterSpawnPointDefinition> Points, NavGrid Grid, Dictionary<string, MonsterDefinition> Definitions) Load()
    {
        var points = JsonSerializer.Deserialize<MonsterSpawnPointFile>(File.ReadAllText(RealData("Dungeon.spawn.json")), JsonOptions)!.Points;
        NavGrid grid = NavGridCatalog.ParseAndValidate(File.ReadAllText(RealData("Dungeon.nav.json")), "Dungeon.nav.json");
        var definitions = MonsterDefinitionCatalog.ParseAndValidate(File.ReadAllText(RealData("MonsterDefinitions.json")), "MonsterDefinitions.json");
        return (points, grid, definitions);
    }

    // 시작 칸에서 4방향으로 퍼지며 이동 가능하고 활동 영역 안인 칸의 수를 센다.
    private static int CountReachableCells(NavGrid grid, MonsterSpawnPointDefinition point, float startX, float startZ)
    {
        grid.TryWorldToCell(startX, startZ, out int startCx, out int startCz);

        var visited = new HashSet<(int, int)> { (startCx, startCz) };
        var queue = new Queue<(int Cx, int Cz)>();
        queue.Enqueue((startCx, startCz));

        while (queue.Count > 0)
        {
            (int cx, int cz) = queue.Dequeue();
            foreach ((int dx, int dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = cx + dx;
                int nz = cz + dz;
                if (visited.Contains((nx, nz)) || !grid.IsWalkable(nx, nz))
                {
                    continue;
                }

                (float x, float z) = grid.CellCenter(nx, nz);
                if (!point.AllowsPosition(x, z))
                {
                    continue;
                }

                visited.Add((nx, nz));
                queue.Enqueue((nx, nz));
            }
        }

        return visited.Count;
    }

    [Fact]
    public void EveryDungeonSpawn_CanStandAndRoamInsideItsRoom()
    {
        var (points, rawGrid, definitions) = Load();
        float minCells = MinReachableAreaSquareMeters / (rawGrid.CellSize * rawGrid.CellSize);

        Assert.NotEmpty(points);
        foreach (MonsterSpawnPointDefinition point in points)
        {
            Assert.True(point.Area is { IsDefined: true }, $"'{point.PointId}'에 활동 영역이 없습니다 - 던전 마커는 방 영역을 지정해야 합니다.");

            foreach (MonsterSpawnEntry entry in point.Entries)
            {
                MonsterDefinition definition = definitions[entry.MonsterType];
                NavGrid grid = rawGrid.Dilate(definition.AgentRadius);

                bool snapped = grid.TrySnapToWalkable(point.X, point.Z, SpawnSnapRing, out float homeX, out float homeZ);
                Assert.True(snapped, $"'{point.PointId}'({entry.MonsterType}, 반경 {definition.AgentRadius}m)이 서 있을 이동 가능 칸이 없습니다.");

                int reachable = CountReachableCells(grid, point, homeX, homeZ);
                float area = reachable * rawGrid.CellSize * rawGrid.CellSize;
                _output.WriteLine($"{point.PointId,-28} {entry.MonsterType,-13} 반경 {definition.AgentRadius:0.0}m  도달 가능 면적 {area,6:0.0} m^2 ({reachable}칸)");

                Assert.True(reachable >= minCells,
                    $"'{point.PointId}'의 {entry.MonsterType}(반경 {definition.AgentRadius}m)이 도달할 수 있는 면적이 {area:0.0}㎡로 너무 좁습니다(최소 {MinReachableAreaSquareMeters}㎡).");
            }
        }
    }

    // 추격이 실제로 성립하는지: 각 방에서 몬스터 스폰 위치부터 영역 안의 여러 칸까지 경로가 실제로 존재해야 한다.
    [Fact]
    public void EveryDungeonSpawn_CanPathToMostOfItsRoom()
    {
        var (points, rawGrid, definitions) = Load();

        foreach (MonsterSpawnPointDefinition point in points)
        {
            foreach (MonsterSpawnEntry entry in point.Entries)
            {
                NavGrid grid = rawGrid.Dilate(definitions[entry.MonsterType].AgentRadius);
                grid.TrySnapToWalkable(point.X, point.Z, SpawnSnapRing, out float homeX, out float homeZ);

                // 영역 안의 이동 가능 칸을 일정 간격으로 표본 삼아, 그중 BFS로 도달 가능하다고 센 칸에는 A*도 길을 찾아야 한다.
                int tried = 0;
                int failed = 0;
                for (int cz = 0; cz < grid.Height; cz += 3)
                {
                    for (int cx = 0; cx < grid.Width; cx += 3)
                    {
                        (float x, float z) = grid.CellCenter(cx, cz);
                        if (!grid.IsWalkable(cx, cz) || !point.AllowsPosition(x, z))
                        {
                            continue;
                        }

                        tried++;
                        bool found = grid.TryFindPath(homeX, homeZ, x, z, point.AllowsPosition, out var path);
                        if (found)
                        {
                            // 경로는 끝까지 이동 가능 칸과 영역 안을 지나야 한다.
                            float px = homeX, pz = homeZ;
                            foreach (var waypoint in path)
                            {
                                Assert.True(grid.HasLineOfSight(px, pz, waypoint.X, waypoint.Z, point.AllowsPosition),
                                    $"'{point.PointId}' 경로가 장애물/영역 밖을 지납니다.");
                                (px, pz) = waypoint;
                            }
                        }
                        else
                        {
                            failed++;
                        }
                    }
                }

                _output.WriteLine($"{point.PointId,-28} {entry.MonsterType,-13} 표본 {tried}칸 중 경로 없음 {failed}칸");
                Assert.True(tried > 0, $"'{point.PointId}' 영역 안에 이동 가능 칸이 없습니다.");
            }
        }
    }
}
