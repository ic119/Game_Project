using GameServer.Navigation;

namespace GameServer.Tests;

public class NavGridCatalogTests
{
    [Fact]
    public void ParseAndValidate_ExporterFormat_BuildsGrid()
    {
        const string json = """
            { "cellSize": 0.5, "originX": -2.0, "originZ": 4.0, "rows": [ "..#", "#.." ] }
            """;

        NavGrid grid = NavGridCatalog.ParseAndValidate(json, "test");

        Assert.Equal(0.5f, grid.CellSize);
        Assert.Equal(3, grid.Width);
        Assert.Equal(2, grid.Height);
        Assert.True(grid.IsWalkable(1, 1));
        Assert.False(grid.IsWalkable(2, 0));
        Assert.True(grid.IsWalkableAt(-1.8f, 4.2f));
    }

    [Theory]
    [InlineData("""{ "cellSize": 0, "rows": [ ".." ] }""")]
    [InlineData("""{ "cellSize": 0.5, "rows": [ ] }""")]
    [InlineData("""{ "cellSize": 0.5, "rows": [ "..", "." ] }""")]
    [InlineData("""{ "cellSize": 0.5, "rows": [ ".?" ] }""")]
    [InlineData("{ not json")]
    public void ParseAndValidate_Invalid_Throws(string json)
    {
        Assert.Throws<InvalidOperationException>(() => NavGridCatalog.ParseAndValidate(json, "test"));
    }

    [Fact]
    public void Get_MapWithoutGrid_ReturnsNull()
    {
        Assert.Null(NavGridCatalog.Get("no-such-map", 0.5f));
        Assert.Null(NavGridCatalog.GetRaw("no-such-map"));
    }
}
