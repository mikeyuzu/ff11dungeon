namespace FF11Dungeon.MapGen.Tests.Unit;

/// <summary>
/// ConnectivityValidator のユニットテスト。
/// Validates: Requirements 10.1
/// </summary>
public class ConnectivityValidatorTests
{
    private readonly ConnectivityValidator _validator = new();

    [Fact]
    public void Validate_SingleRoomFullyConnected_ReturnsTrue()
    {
        // 5x5グリッドに1つのRoomを配置し、スポーン地点から到達可能
        var grid = new MapGrid(5, 5);
        // Room内をFloorで埋める
        for (int x = 1; x <= 3; x++)
            for (int y = 1; y <= 3; y++)
                grid[x, y] = TileType.Floor;

        var rooms = new List<RoomMetadata>
        {
            new() { Room = new Room { X = 1, Y = 1, Width = 3, Height = 3 } }
        };

        var result = _validator.Validate(grid, rooms, new Vector2Int(2, 2));
        Assert.True(result);
    }

    [Fact]
    public void Validate_TwoRoomsConnectedByCorridor_ReturnsTrue()
    {
        // 15x5グリッドに2つのRoomを配置し、Corridorで接続
        var grid = new MapGrid(15, 5);

        // Room1: (1,1)-(3,3)
        for (int x = 1; x <= 3; x++)
            for (int y = 1; y <= 3; y++)
                grid[x, y] = TileType.Floor;

        // Room2: (10,1)-(12,3)
        for (int x = 10; x <= 12; x++)
            for (int y = 1; y <= 3; y++)
                grid[x, y] = TileType.Floor;

        // Corridor connecting them at y=2
        for (int x = 4; x <= 9; x++)
            grid[x, 2] = TileType.Corridor;

        var rooms = new List<RoomMetadata>
        {
            new() { Room = new Room { X = 1, Y = 1, Width = 3, Height = 3 } },
            new() { Room = new Room { X = 10, Y = 1, Width = 3, Height = 3 } }
        };

        var result = _validator.Validate(grid, rooms, new Vector2Int(2, 2));
        Assert.True(result);
    }

    [Fact]
    public void Validate_DisconnectedRoom_ReturnsFalse()
    {
        // 2つのRoomが接続されていない場合
        var grid = new MapGrid(15, 5);

        // Room1: (1,1)-(3,3) - スポーンがある
        for (int x = 1; x <= 3; x++)
            for (int y = 1; y <= 3; y++)
                grid[x, y] = TileType.Floor;

        // Room2: (10,1)-(12,3) - 接続なし（壁で隔離）
        for (int x = 10; x <= 12; x++)
            for (int y = 1; y <= 3; y++)
                grid[x, y] = TileType.Floor;

        var rooms = new List<RoomMetadata>
        {
            new() { Room = new Room { X = 1, Y = 1, Width = 3, Height = 3 } },
            new() { Room = new Room { X = 10, Y = 1, Width = 3, Height = 3 } }
        };

        var result = _validator.Validate(grid, rooms, new Vector2Int(2, 2));
        Assert.False(result);
    }

    [Fact]
    public void Validate_HiddenRoomNotRequired_ReturnsTrue()
    {
        // 隠しRoomは到達不要
        var grid = new MapGrid(15, 5);

        // Room1: (1,1)-(3,3)
        for (int x = 1; x <= 3; x++)
            for (int y = 1; y <= 3; y++)
                grid[x, y] = TileType.Floor;

        // Room2 (隠し): (10,1)-(12,3) - 接続なし
        for (int x = 10; x <= 12; x++)
            for (int y = 1; y <= 3; y++)
                grid[x, y] = TileType.Floor;

        var rooms = new List<RoomMetadata>
        {
            new() { Room = new Room { X = 1, Y = 1, Width = 3, Height = 3 } },
            new() { Room = new Room { X = 10, Y = 1, Width = 3, Height = 3 }, IsHiddenRoom = true }
        };

        var result = _validator.Validate(grid, rooms, new Vector2Int(2, 2));
        Assert.True(result);
    }

    [Fact]
    public void Validate_TraversesAllPassableTileTypes()
    {
        // Floor, Corridor, RoomEntrance, StairsDown すべてを通過できることを確認
        var grid = new MapGrid(10, 3);

        // Room1 at start
        grid[0, 1] = TileType.Floor;
        grid[1, 1] = TileType.RoomEntrance;
        grid[2, 1] = TileType.Corridor;
        grid[3, 1] = TileType.Corridor;
        grid[4, 1] = TileType.RoomEntrance;
        grid[5, 1] = TileType.Floor;
        grid[6, 1] = TileType.StairsDown;
        grid[7, 1] = TileType.Floor;

        var rooms = new List<RoomMetadata>
        {
            new() { Room = new Room { X = 0, Y = 1, Width = 2, Height = 1 } },
            new() { Room = new Room { X = 5, Y = 1, Width = 3, Height = 1 } }
        };

        var result = _validator.Validate(grid, rooms, new Vector2Int(0, 1));
        Assert.True(result);
    }

    [Fact]
    public void Validate_WallBlocksTraversal()
    {
        // Wall(デフォルト)はBFSで通過できない
        var grid = new MapGrid(5, 5);

        // 左側にRoom
        grid[0, 2] = TileType.Floor;
        grid[1, 2] = TileType.Floor;
        // (2, 2) はWall（デフォルト）→ ブロック
        // 右側にRoom
        grid[3, 2] = TileType.Floor;
        grid[4, 2] = TileType.Floor;

        var rooms = new List<RoomMetadata>
        {
            new() { Room = new Room { X = 0, Y = 2, Width = 2, Height = 1 } },
            new() { Room = new Room { X = 3, Y = 2, Width = 2, Height = 1 } }
        };

        var result = _validator.Validate(grid, rooms, new Vector2Int(0, 2));
        Assert.False(result);
    }

    [Fact]
    public void Validate_EmptyRoomList_ReturnsTrue()
    {
        // Roomが0個の場合はtrue
        var grid = new MapGrid(5, 5);
        grid[2, 2] = TileType.Floor;

        var rooms = new List<RoomMetadata>();

        var result = _validator.Validate(grid, rooms, new Vector2Int(2, 2));
        Assert.True(result);
    }

    [Fact]
    public void Validate_OnlyHiddenRooms_ReturnsTrue()
    {
        // すべてのRoomが隠しRoomの場合、検証対象なしでtrue
        var grid = new MapGrid(5, 5);
        grid[2, 2] = TileType.Floor;

        var rooms = new List<RoomMetadata>
        {
            new() { Room = new Room { X = 0, Y = 0, Width = 1, Height = 1 }, IsHiddenRoom = true }
        };

        var result = _validator.Validate(grid, rooms, new Vector2Int(2, 2));
        Assert.True(result);
    }
}
