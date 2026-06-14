namespace MapViewer.MapGen;

/// <summary>
/// BigRoomMode=true 時に使用される生成器。
/// 区画分割をスキップし、1タイル壁ボーダーを除くマップ全域に単一Roomを生成する。
/// Corridor / RoomEntrance タイルは一切生成しない。
/// </summary>
public sealed class BigRoomGenerator
{
    /// <summary>
    /// BigRoomMode=true時: 区画分割をスキップし、1タイル壁ボーダーを除く
    /// マップ全域に単一Roomを生成する。
    /// </summary>
    public static BigRoomResult Generate(GenerationConfig config, Random rng)
    {
        var grid = new MapGrid(config.MapWidth, config.MapHeight);

        // Single room: 1-tile wall border on all sides
        var room = new Room
        {
            X = 1,
            Y = 1,
            Width = config.MapWidth - 2,
            Height = config.MapHeight - 2,
            PartitionRow = 0,
            PartitionCol = 0,
        };

        // Write Floor tiles for the entire room area
        for (int x = room.X; x < room.X + room.Width; x++)
            for (int y = room.Y; y < room.Y + room.Height; y++)
                grid[x, y] = TileType.Floor;

        // Create metadata
        var metadata = new RoomMetadata { Room = room };

        // If MonsterHouseEnabled, set flag and density multipliers
        if (config.MonsterHouseEnabled)
        {
            metadata.IsMonsterHouse = true;
            metadata.ItemDensityMultiplier = 3.0f;
            metadata.MonsterDensityMultiplier = 3.0f;
        }

        // Place StairsDown at a random floor tile within the room
        int stairsX = rng.Next(room.X, room.X + room.Width);
        int stairsY = rng.Next(room.Y, room.Y + room.Height);
        grid[stairsX, stairsY] = TileType.StairsDown;

        return new BigRoomResult
        {
            Grid = grid,
            Room = room,
            Metadata = metadata,
            StairsPosition = new Vector2Int(stairsX, stairsY),
        };
    }
}

/// <summary>
/// BigRoomGenerator の生成結果。
/// </summary>
public sealed class BigRoomResult
{
    public required MapGrid Grid { get; init; }
    public required Room Room { get; init; }
    public required RoomMetadata Metadata { get; init; }
    public required Vector2Int StairsPosition { get; init; }
}
