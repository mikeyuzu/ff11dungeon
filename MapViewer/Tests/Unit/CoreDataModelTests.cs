using MapViewer.MapGen;

namespace MapViewer.Tests.Unit;

/// <summary>
/// コアデータモデル (MapGrid, TileType, Vector2Int) のユニットテスト。
/// Validates: Requirements 5.1, 5.2
/// </summary>
public class CoreDataModelTests
{
    #region MapGrid Tests

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 9)]
    public void InBounds_ValidCoordinates_ReturnsTrue(int x, int y)
    {
        var grid = new MapGrid(10, 10);
        Assert.True(grid.InBounds(x, y));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(10, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 10)]
    public void InBounds_OutOfBoundsCoordinates_ReturnsFalse(int x, int y)
    {
        var grid = new MapGrid(10, 10);
        Assert.False(grid.InBounds(x, y));
    }

    [Fact]
    public void IsPassable_WallTile_ReturnsFalse()
    {
        var grid = new MapGrid(10, 10);
        // Default is Wall
        Assert.False(grid.IsPassable(0, 0));
    }

    [Fact]
    public void IsPassable_OutOfBounds_ReturnsFalse()
    {
        var grid = new MapGrid(10, 10);
        Assert.False(grid.IsPassable(-1, 0));
        Assert.False(grid.IsPassable(10, 0));
    }

    [Theory]
    [InlineData(TileType.Floor)]
    [InlineData(TileType.Corridor)]
    [InlineData(TileType.RoomEntrance)]
    [InlineData(TileType.StairsDown)]
    public void IsPassable_NonWallTiles_ReturnsTrue(TileType tile)
    {
        var grid = new MapGrid(10, 10);
        grid[5, 5] = tile;
        Assert.True(grid.IsPassable(5, 5));
    }

    [Fact]
    public void GetTileOrWall_OutOfBounds_ReturnsWall()
    {
        var grid = new MapGrid(10, 10);
        grid[0, 0] = TileType.Floor;
        Assert.Equal(TileType.Wall, grid.GetTileOrWall(-1, 0));
        Assert.Equal(TileType.Wall, grid.GetTileOrWall(10, 0));
    }

    [Fact]
    public void GetTileOrWall_ValidCoordinates_ReturnsActualTile()
    {
        var grid = new MapGrid(10, 10);
        grid[3, 4] = TileType.Corridor;
        Assert.Equal(TileType.Corridor, grid.GetTileOrWall(3, 4));
    }

    [Fact]
    public void MapGrid_InitialState_AllWalls()
    {
        var grid = new MapGrid(5, 5);
        for (int x = 0; x < 5; x++)
            for (int y = 0; y < 5; y++)
                Assert.Equal(TileType.Wall, grid[x, y]);
    }

    #endregion

    #region TileType Tests

    [Theory]
    [InlineData(TileType.Wall, 0)]
    [InlineData(TileType.Floor, 1)]
    [InlineData(TileType.Corridor, 2)]
    [InlineData(TileType.RoomEntrance, 3)]
    [InlineData(TileType.StairsDown, 4)]
    public void TileType_Values_MatchExpected(TileType tile, byte expected)
    {
        Assert.Equal(expected, (byte)tile);
    }

    #endregion

    #region Vector2Int Tests

    [Fact]
    public void Vector2Int_Addition_ReturnsCorrectResult()
    {
        var a = new Vector2Int(1, 2);
        var b = new Vector2Int(3, 4);
        var result = a + b;
        Assert.Equal(new Vector2Int(4, 6), result);
    }

    [Fact]
    public void Vector2Int_DirectionConstants_AreCorrect()
    {
        Assert.Equal(new Vector2Int(0, -1), Vector2Int.Up);
        Assert.Equal(new Vector2Int(0, 1), Vector2Int.Down);
        Assert.Equal(new Vector2Int(-1, 0), Vector2Int.Left);
        Assert.Equal(new Vector2Int(1, 0), Vector2Int.Right);
    }

    [Fact]
    public void Vector2Int_FourDirections_HasFourElements()
    {
        Assert.Equal(4, Vector2Int.FourDirections.Length);
    }

    [Fact]
    public void Vector2Int_EightDirections_HasEightElements()
    {
        Assert.Equal(8, Vector2Int.EightDirections.Length);
    }

    #endregion
}
