namespace FF11Dungeon.MapGen.Tests.Unit;

/// <summary>
/// BigRoomGenerator のユニットテスト。
/// Validates: Requirements 6.2, 6.4, 6.5
/// </summary>
public class BigRoomGeneratorTests
{
    private static GenerationConfig CreateBigRoomConfig(
        int width = 60, int height = 40, bool monsterHouseEnabled = false)
    {
        return new GenerationConfig
        {
            MapWidth = width,
            MapHeight = height,
            BigRoomMode = true,
            MonsterHouseEnabled = monsterHouseEnabled,
        };
    }

    #region Req 6.2: 区画分割スキップと単一Room生成

    [Fact]
    public void Generate_CreatesRoomCoveringEntireMapMinusBorder()
    {
        var config = CreateBigRoomConfig(60, 40);
        var rng = new Random(42);
        var generator = new BigRoomGenerator();

        var result = generator.Generate(config, rng);

        Assert.Equal(1, result.Room.X);
        Assert.Equal(1, result.Room.Y);
        Assert.Equal(58, result.Room.Width);   // 60 - 2
        Assert.Equal(38, result.Room.Height);  // 40 - 2
    }

    [Fact]
    public void Generate_GridHasCorrectDimensions()
    {
        var config = CreateBigRoomConfig(60, 40);
        var rng = new Random(42);
        var generator = new BigRoomGenerator();

        var result = generator.Generate(config, rng);

        Assert.Equal(60, result.Grid.Width);
        Assert.Equal(40, result.Grid.Height);
    }

    #endregion

    #region Req 6.2: 1タイル壁ボーダー + 全域Floor

    [Fact]
    public void Generate_BorderIsWall()
    {
        var config = CreateBigRoomConfig(20, 20);
        var rng = new Random(42);
        var generator = new BigRoomGenerator();

        var result = generator.Generate(config, rng);

        // Top and bottom rows
        for (int x = 0; x < 20; x++)
        {
            Assert.Equal(TileType.Wall, result.Grid[x, 0]);
            Assert.Equal(TileType.Wall, result.Grid[x, 19]);
        }
        // Left and right columns
        for (int y = 0; y < 20; y++)
        {
            Assert.Equal(TileType.Wall, result.Grid[0, y]);
            Assert.Equal(TileType.Wall, result.Grid[19, y]);
        }
    }

    [Fact]
    public void Generate_InteriorIsFloorOrStairsDown()
    {
        var config = CreateBigRoomConfig(20, 20);
        var rng = new Random(42);
        var generator = new BigRoomGenerator();

        var result = generator.Generate(config, rng);

        for (int x = 1; x < 19; x++)
        {
            for (int y = 1; y < 19; y++)
            {
                var tile = result.Grid[x, y];
                Assert.True(
                    tile == TileType.Floor || tile == TileType.StairsDown,
                    $"Tile at ({x},{y}) is {tile}, expected Floor or StairsDown");
            }
        }
    }

    #endregion

    #region Req 6.5: Corridor / RoomEntrance 非生成

    [Fact]
    public void Generate_NoCorridorTiles()
    {
        var config = CreateBigRoomConfig(60, 40);
        var rng = new Random(42);
        var generator = new BigRoomGenerator();

        var result = generator.Generate(config, rng);

        for (int x = 0; x < result.Grid.Width; x++)
        {
            for (int y = 0; y < result.Grid.Height; y++)
            {
                Assert.NotEqual(TileType.Corridor, result.Grid[x, y]);
            }
        }
    }

    [Fact]
    public void Generate_NoRoomEntranceTiles()
    {
        var config = CreateBigRoomConfig(60, 40);
        var rng = new Random(42);
        var generator = new BigRoomGenerator();

        var result = generator.Generate(config, rng);

        for (int x = 0; x < result.Grid.Width; x++)
        {
            for (int y = 0; y < result.Grid.Height; y++)
            {
                Assert.NotEqual(TileType.RoomEntrance, result.Grid[x, y]);
            }
        }
    }

    #endregion

    #region Req 6.2: StairsDown配置

    [Fact]
    public void Generate_PlacesExactlyOneStairsDown()
    {
        var config = CreateBigRoomConfig(60, 40);
        var rng = new Random(42);
        var generator = new BigRoomGenerator();

        var result = generator.Generate(config, rng);

        int stairsCount = 0;
        for (int x = 0; x < result.Grid.Width; x++)
            for (int y = 0; y < result.Grid.Height; y++)
                if (result.Grid[x, y] == TileType.StairsDown)
                    stairsCount++;

        Assert.Equal(1, stairsCount);
    }

    [Fact]
    public void Generate_StairsDownIsInsideRoom()
    {
        var config = CreateBigRoomConfig(60, 40);
        var rng = new Random(42);
        var generator = new BigRoomGenerator();

        var result = generator.Generate(config, rng);

        var pos = result.StairsPosition;
        Assert.InRange(pos.X, result.Room.X, result.Room.X + result.Room.Width - 1);
        Assert.InRange(pos.Y, result.Room.Y, result.Room.Y + result.Room.Height - 1);
    }

    #endregion

    #region Req 6.4: BigRoomMode + MonsterHouseEnabled

    [Fact]
    public void Generate_WithMonsterHouseEnabled_SetsMonsterHouseFlag()
    {
        var config = CreateBigRoomConfig(60, 40, monsterHouseEnabled: true);
        var rng = new Random(42);
        var generator = new BigRoomGenerator();

        var result = generator.Generate(config, rng);

        Assert.True(result.Metadata.IsMonsterHouse);
        Assert.Equal(3.0f, result.Metadata.ItemDensityMultiplier);
        Assert.Equal(3.0f, result.Metadata.MonsterDensityMultiplier);
    }

    [Fact]
    public void Generate_WithoutMonsterHouseEnabled_DoesNotSetFlag()
    {
        var config = CreateBigRoomConfig(60, 40, monsterHouseEnabled: false);
        var rng = new Random(42);
        var generator = new BigRoomGenerator();

        var result = generator.Generate(config, rng);

        Assert.False(result.Metadata.IsMonsterHouse);
        Assert.Equal(1.0f, result.Metadata.ItemDensityMultiplier);
        Assert.Equal(1.0f, result.Metadata.MonsterDensityMultiplier);
    }

    #endregion

    #region Determinism

    [Fact]
    public void Generate_SameSeeds_ProduceSameResult()
    {
        var config = CreateBigRoomConfig(60, 40);
        var generator = new BigRoomGenerator();

        var result1 = generator.Generate(config, new Random(42));
        var result2 = generator.Generate(config, new Random(42));

        Assert.Equal(result1.StairsPosition, result2.StairsPosition);
        Assert.Equal(result1.Room, result2.Room);
    }

    #endregion
}
