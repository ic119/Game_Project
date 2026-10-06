using GameServer.Navigation;

namespace GameServer.Tests;

public class NavGridTests
{
    // 칸 크기 1m, 원점 (0, 0)인 격자를 ASCII로 만든다. rows[z][x]: '.'=이동 가능, '#'=이동 불가.
    private static NavGrid Grid(params string[] rows) => NavGrid.FromRows(1f, 0f, 0f, rows);

    // 경로가 장애물을 통과하지 않는지: 시작점부터 경유점들을 잇는 모든 구간에서 시야가 확보돼야 한다.
    private static void AssertPathIsWalkable(NavGrid grid, float startX, float startZ, List<(float X, float Z)> path)
    {
        float x = startX;
        float z = startZ;
        foreach (var waypoint in path)
        {
            Assert.True(grid.HasLineOfSight(x, z, waypoint.X, waypoint.Z), $"({x},{z}) -> ({waypoint.X},{waypoint.Z}) 구간이 장애물을 통과합니다.");
            (x, z) = waypoint;
        }
    }

    [Fact]
    public void FromRows_ParsesCellsAndCoordinates()
    {
        var grid = NavGrid.FromRows(0.5f, 10f, -20f, new[] { "..#", "#.." });

        Assert.Equal(3, grid.Width);
        Assert.Equal(2, grid.Height);
        Assert.True(grid.IsWalkable(0, 0));
        Assert.False(grid.IsWalkable(2, 0));
        Assert.False(grid.IsWalkable(0, 1));

        // 칸(2, 0)은 x 11.0~11.5, z -20.0~-19.5
        Assert.False(grid.IsWalkableAt(11.2f, -19.8f));
        Assert.True(grid.IsWalkableAt(10.2f, -19.8f));
        Assert.False(grid.IsWalkableAt(9f, -20f)); // 격자 밖
        Assert.Equal((10.25f, -19.75f), grid.CellCenter(0, 0));
    }

    [Theory]
    [InlineData("..", ".")]    // 행 길이가 다르다
    [InlineData(".x")]         // 알 수 없는 글자
    public void FromRows_Invalid_Throws(params string[] rows)
    {
        Assert.Throws<ArgumentException>(() => NavGrid.FromRows(1f, 0f, 0f, rows));
    }

    [Fact]
    public void FindPath_OpenGrid_GoesStraightToGoal()
    {
        var grid = Grid(".....", ".....", ".....");

        Assert.True(grid.TryFindPath(0.5f, 0.5f, 4.5f, 2.5f, null, out var path));

        Assert.Single(path);
        Assert.Equal((4.5f, 2.5f), path[0]);
    }

    [Fact]
    public void FindPath_WallWithGap_DetoursThroughGap()
    {
        // 가운데 x=2 열이 벽이고 맨 아래(z=4)만 뚫려 있다.
        var grid = Grid(
            "..#..",
            "..#..",
            "..#..",
            "..#..",
            ".....");

        Assert.True(grid.TryFindPath(0.5f, 0.5f, 4.5f, 0.5f, null, out var path));

        Assert.True(path.Count >= 2, "벽을 돌아가려면 최소 한 번은 꺾여야 한다.");
        Assert.Equal((4.5f, 0.5f), path[^1]);
        Assert.Contains(path, p => p.Z > 3.9f); // 아래쪽 틈을 지난다
        AssertPathIsWalkable(grid, 0.5f, 0.5f, path);
    }

    [Fact]
    public void FindPath_FullyWalledOff_ReturnsFalse()
    {
        var grid = Grid(
            "..#..",
            "..#..",
            "..#..");

        Assert.False(grid.TryFindPath(0.5f, 0.5f, 4.5f, 0.5f, null, out var path));
        Assert.Empty(path);
    }

    [Fact]
    public void FindPath_DoesNotCutDiagonalCorners()
    {
        // 두 장애물이 대각선으로 맞닿아 있다 - 그 사이 모서리로 빠져나갈 수 없다.
        var grid = Grid(
            ".#",
            "#.");

        Assert.False(grid.TryFindPath(0.5f, 0.5f, 1.5f, 1.5f, null, out _));
    }

    [Fact]
    public void FindPath_AllowedPredicate_RestrictsToArea()
    {
        // 열린 격자지만 z < 2 영역으로만 다니게 하면 z=4 목표에는 갈 수 없다.
        var grid = Grid(".....", ".....", ".....", ".....", ".....");

        Assert.False(grid.TryFindPath(0.5f, 0.5f, 4.5f, 4.5f, (_, z) => z < 2f, out _));
        Assert.True(grid.TryFindPath(0.5f, 0.5f, 4.5f, 1.5f, (_, z) => z < 2f, out _));
    }

    [Fact]
    public void FindPath_GoalInsideObstacle_SnapsToNearestWalkableCell()
    {
        var grid = Grid("...", ".#.", "...");

        Assert.True(grid.TryFindPath(0.5f, 0.5f, 1.5f, 1.5f, null, out var path));

        // 목표(장애물 칸 중심)가 아니라 이웃 이동 가능 칸의 중심에서 끝난다.
        Assert.NotEqual((1.5f, 1.5f), path[^1]);
        Assert.True(grid.IsWalkableAt(path[^1].X, path[^1].Z));
    }

    [Fact]
    public void FindPath_StartEqualsGoalCell_ReturnsGoal()
    {
        var grid = Grid("...", "...");

        Assert.True(grid.TryFindPath(0.2f, 0.2f, 0.8f, 0.8f, null, out var path));

        Assert.Single(path);
        Assert.Equal((0.8f, 0.8f), path[0]);
    }

    [Fact]
    public void HasLineOfSight_BlockedByObstacle()
    {
        var grid = Grid(".....", "..#..", ".....");

        Assert.False(grid.HasLineOfSight(0.5f, 1.5f, 4.5f, 1.5f));
        Assert.True(grid.HasLineOfSight(0.5f, 0.5f, 4.5f, 0.5f));
    }

    [Fact]
    public void TrySnapToWalkable_WalkableStaysPut_BlockedMovesToNeighbor_FarFails()
    {
        var grid = Grid("....", ".##.", ".##.", "....");

        Assert.True(grid.TrySnapToWalkable(0.5f, 0.5f, 2, out float x0, out float z0));
        Assert.Equal((0.5f, 0.5f), (x0, z0));

        Assert.True(grid.TrySnapToWalkable(1.5f, 1.5f, 2, out float x1, out float z1));
        Assert.True(grid.IsWalkableAt(x1, z1));

        // 링 1칸 안에 이동 가능 칸이 없으면 실패한다(가운데가 모두 막힌 5x5의 중심).
        var big = Grid(".....", ".###.", ".###.", ".###.", ".....");
        Assert.False(big.TrySnapToWalkable(2.5f, 2.5f, 1, out _, out _));
    }

    [Fact]
    public void Dilate_BlocksCellsWithinRadiusOfObstacles()
    {
        var grid = Grid(".....", ".....", "..#..", ".....", ".....");

        NavGrid dilated = grid.Dilate(1f);

        // 반경 1m(1칸): 장애물의 상하좌우 이웃은 막히고 대각선과 그 너머는 열려 있다.
        Assert.False(dilated.IsWalkable(1, 2));
        Assert.False(dilated.IsWalkable(3, 2));
        Assert.False(dilated.IsWalkable(2, 1));
        Assert.False(dilated.IsWalkable(2, 3));
        Assert.True(dilated.IsWalkable(1, 1));
        Assert.True(dilated.IsWalkable(0, 2));
        // 원본은 바뀌지 않는다.
        Assert.True(grid.IsWalkable(1, 2));
    }

    [Fact]
    public void Dilate_NarrowGap_ClosesForLargeAgentOnly()
    {
        // 폭 1칸짜리 틈 - 작은 몬스터(반경 0)는 지나가고 반경 1칸 몬스터는 못 지나간다.
        var grid = Grid(
            "..#..",
            "..#..",
            ".....",
            "..#..",
            "..#..");

        Assert.True(grid.TryFindPath(0.5f, 2.5f, 4.5f, 2.5f, null, out _));
        Assert.True(grid.Dilate(0f).TryFindPath(0.5f, 2.5f, 4.5f, 2.5f, null, out _));
        Assert.False(grid.Dilate(1f).TryFindPath(0.5f, 2.5f, 4.5f, 2.5f, null, out _));
    }
}
