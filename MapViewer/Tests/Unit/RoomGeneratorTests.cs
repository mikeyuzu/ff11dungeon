using MapViewer.MapGen;

namespace MapViewer.Tests.Unit;

/// <summary>
/// RoomGenerator のユニットテスト。
/// Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 5.3
/// </summary>
public class RoomGeneratorTests
{
    private static PartitionGrid CreateSimpleGrid(int rows, int cols, int partW, int partH)
    {
        var partitions = new Partition[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                partitions[r, c] = new Partition
                {
                    X = c * partW,
                    Y = r * partH,
                    Width = partW,
                    Height = partH,
                    Row = r,
                    Col = c,
                };
            }
        }
        return new PartitionGrid(partitions);
    }

    #region Req 2.1: 各Partitionに最大1つのRoom

    [Fact]
    public void GenerateRooms_EachPartitionHasAtMostOneRoom()
    {
        var grid = CreateSimpleGrid(3, 3, 20, 20);
        var mapGrid = new MapGrid(60, 60);
        var config = new GenerationConfig
        {
            MinRoomWidth = 5,
            MinRoomHeight = 5,
            MaxRoomWidth = 10,
            MaxRoomHeight = 10,
            EmptyPartitionChance = 0.0f,
        };
        var rng = new Random(42);
        var generator = new RoomGenerator();

        var result = RoomGenerator.GenerateRooms(grid, config, mapGrid, rng);

        // 各Partitionに最大1部屋
        var partitionRoomCounts = result.Rooms
            .GroupBy(r => (r.PartitionRow, r.PartitionCol))
            .Select(g => g.Count());
        Assert.All(partitionRoomCounts, count => Assert.Equal(1, count));
    }

    #endregion

    #region Req 2.2: 部屋サイズ制約

    [Fact]
    public void GenerateRooms_RoomSizeWithinConfigBounds()
    {
        var grid = CreateSimpleGrid(3, 3, 20, 20);
        var mapGrid = new MapGrid(60, 60);
        var config = new GenerationConfig
        {
            MinRoomWidth = 5,
            MinRoomHeight = 5,
            MaxRoomWidth = 10,
            MaxRoomHeight = 10,
            EmptyPartitionChance = 0.0f,
        };
        var rng = new Random(123);
        _ = new RoomGenerator();

        var result = RoomGenerator.GenerateRooms(grid, config, mapGrid, rng);

        foreach (var room in result.Rooms)
        {
            Assert.InRange(room.Width, config.MinRoomWidth, config.MaxRoomWidth);
            Assert.InRange(room.Height, config.MinRoomHeight, config.MaxRoomHeight);
        }
    }

    [Fact]
    public void GenerateRooms_RoomSizeConstrainedByPartitionSize()
    {
        // Small partition: 12x12, max room limited to 12-2=10
        var grid = CreateSimpleGrid(1, 1, 12, 12);
        var mapGrid = new MapGrid(12, 12);
        var config = new GenerationConfig
        {
            MinRoomWidth = 5,
            MinRoomHeight = 5,
            MaxRoomWidth = 15, // Larger than partition allows
            MaxRoomHeight = 15,
            EmptyPartitionChance = 0.0f,
        };
        var rng = new Random(99);
        _ = new RoomGenerator();

        var result = RoomGenerator.GenerateRooms(grid, config, mapGrid, rng);

        foreach (var room in result.Rooms)
        {
            // Max effective size is partition - 2 = 10
            Assert.True(room.Width <= 10);
            Assert.True(room.Height <= 10);
        }
    }

    #endregion

    #region Req 2.3: 1タイル壁マージン

    [Fact]
    public void GenerateRooms_RoomHasOneMarginFromPartitionBoundary()
    {
        var grid = CreateSimpleGrid(3, 3, 20, 20);
        var mapGrid = new MapGrid(60, 60);
        var config = new GenerationConfig
        {
            MinRoomWidth = 5,
            MinRoomHeight = 5,
            MaxRoomWidth = 15,
            MaxRoomHeight = 15,
            EmptyPartitionChance = 0.0f,
        };
        var rng = new Random(77);
        _ = new RoomGenerator();

        var result = RoomGenerator.GenerateRooms(grid, config, mapGrid, rng);

        foreach (var room in result.Rooms)
        {
            int partX = room.PartitionCol * 20;
            int partY = room.PartitionRow * 20;

            // Room must start at least 1 tile inside partition
            Assert.True(room.X >= partX + 1, $"Room X={room.X} must be >= {partX + 1}");
            Assert.True(room.Y >= partY + 1, $"Room Y={room.Y} must be >= {partY + 1}");
            // Room must end at least 1 tile before partition end
            Assert.True(room.X + room.Width <= partX + 20 - 1, $"Room right edge must be <= {partX + 19}");
            Assert.True(room.Y + room.Height <= partY + 20 - 1, $"Room bottom edge must be <= {partY + 19}");
        }
    }

    #endregion

    #region Req 2.5: 空き区画確率によるスキップ

    [Fact]
    public void GenerateRooms_AllEmpty_SkipsRoomGeneration()
    {
        var grid = CreateSimpleGrid(3, 3, 20, 20);
        var mapGrid = new MapGrid(60, 60);
        var config = new GenerationConfig
        {
            MinRoomWidth = 5,
            MinRoomHeight = 5,
            MaxRoomWidth = 10,
            MaxRoomHeight = 10,
            EmptyPartitionChance = 1.0f, // Always empty
        };
        var rng = new Random(42);
        _ = new RoomGenerator();

        var result = RoomGenerator.GenerateRooms(grid, config, mapGrid, rng);

        // Even with 100% empty chance, minimum 2 rooms guaranteed
        Assert.True(result.Rooms.Count >= 2);
    }

    [Fact]
    public void GenerateRooms_ZeroEmptyChance_AllPartitionsGetRooms()
    {
        var grid = CreateSimpleGrid(3, 3, 20, 20);
        var mapGrid = new MapGrid(60, 60);
        var config = new GenerationConfig
        {
            MinRoomWidth = 5,
            MinRoomHeight = 5,
            MaxRoomWidth = 10,
            MaxRoomHeight = 10,
            EmptyPartitionChance = 0.0f,
        };
        var rng = new Random(42);
        _ = new RoomGenerator();

        var result = RoomGenerator.GenerateRooms(grid, config, mapGrid, rng);

        Assert.Equal(9, result.Rooms.Count);
    }

    #endregion

    #region Req 2.6, 2.7: 最低2部屋保証

    [Fact]
    public void GenerateRooms_MinimumTwoRoomsGuaranteed()
    {
        // 2x2 grid with high empty chance
        var grid = CreateSimpleGrid(2, 2, 20, 20);
        _ = new MapGrid(40, 40);
        var config = new GenerationConfig
        {
            MinRoomWidth = 5,
            MinRoomHeight = 5,
            MaxRoomWidth = 10,
            MaxRoomHeight = 10,
            EmptyPartitionChance = 0.9f, // High empty chance
        };

        // Run multiple times to verify guarantee
        for (int seed = 0; seed < 20; seed++)
        {
            var rng = new Random(seed);
            _ = new RoomGenerator();
            var localGrid = new MapGrid(40, 40);
            var result = RoomGenerator.GenerateRooms(grid, config, localGrid, rng);
            Assert.True(result.Rooms.Count >= 2, $"Seed {seed} produced {result.Rooms.Count} rooms");
        }
    }

    #endregion

    #region Req 5.3: FloorタイルをMapGridに書き込む

    [Fact]
    public void GenerateRooms_WritesFloorTilesToGrid()
    {
        var grid = CreateSimpleGrid(1, 1, 20, 20);
        var mapGrid = new MapGrid(20, 20);
        var config = new GenerationConfig
        {
            MinRoomWidth = 5,
            MinRoomHeight = 5,
            MaxRoomWidth = 8,
            MaxRoomHeight = 8,
            EmptyPartitionChance = 0.0f,
        };
        var rng = new Random(42);
        _ = new RoomGenerator();

        var result = RoomGenerator.GenerateRooms(grid, config, mapGrid, rng);

        Assert.Single(result.Rooms);
        var room = result.Rooms[0];

        // Verify floor tiles written
        for (int x = room.X; x < room.X + room.Width; x++)
        {
            for (int y = room.Y; y < room.Y + room.Height; y++)
            {
                Assert.Equal(TileType.Floor, mapGrid[x, y]);
            }
        }

        // Verify surrounding area is still Wall (margin)
        if (room.X > 0)
            Assert.Equal(TileType.Wall, mapGrid[room.X - 1, room.Y]);
    }

    #endregion

    #region RoomGenerationResult

    [Fact]
    public void GenerateRooms_ReturnsMatchingMetadata()
    {
        var grid = CreateSimpleGrid(2, 2, 20, 20);
        var mapGrid = new MapGrid(40, 40);
        var config = new GenerationConfig
        {
            MinRoomWidth = 5,
            MinRoomHeight = 5,
            MaxRoomWidth = 10,
            MaxRoomHeight = 10,
            EmptyPartitionChance = 0.0f,
        };
        var rng = new Random(42);
        _ = new RoomGenerator();

        var result = RoomGenerator.GenerateRooms(grid, config, mapGrid, rng);

        Assert.Equal(result.Rooms.Count, result.Metadata.Count);
        for (int i = 0; i < result.Rooms.Count; i++)
        {
            Assert.Equal(result.Rooms[i], result.Metadata[i].Room);
        }
    }

    #endregion
}
