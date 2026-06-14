namespace FF11Dungeon.MapGen;

public sealed class SpawnPlacer
{
    /// <summary>
    /// スポーン配置を実行する。
    /// StairsDown、プレイヤースポーン、モンスタースポーンを配置し、結果を返す。
    /// 配置不能な場合は失敗結果を返し、再生成トリガーとする。
    /// </summary>
    public SpawnResult PlaceSpawns(
        MapGrid grid,
        IReadOnlyList<Room> rooms,
        IReadOnlyList<RoomMetadata> metadata,
        IReadOnlyList<Corridor> corridors,
        GenerationConfig config,
        Random rng)
    {
        // 各Roomの通路接続数をカウント
        var connectionCount = new int[rooms.Count];
        foreach (var corridor in corridors)
        {
            connectionCount[corridor.SourceRoomIndex]++;
            connectionCount[corridor.TargetRoomIndex]++;
        }

        // 階段配置候補: 非隠しRoom かつ Corridor接続≥1
        var connectedRooms = new List<int>();
        for (int i = 0; i < rooms.Count; i++)
        {
            if (!metadata[i].IsHiddenRoom && connectionCount[i] >= 1)
                connectedRooms.Add(i);
        }

        if (connectedRooms.Count < 2)
        {
            // 階段とプレイヤーを別々のRoomに配置できない
            return SpawnResult.Failed("Not enough connected rooms for stairs and player spawn");
        }

        // 階段をランダムな接続Roomに配置
        int stairsRoomIndex = connectedRooms[rng.Next(connectedRooms.Count)];
        var stairsRoom = rooms[stairsRoomIndex];

        // 階段Room内のランダムなFloorタイルを選択
        var stairsPos = PickRandomFloorInRoom(grid, stairsRoom, rng);
        if (!stairsPos.HasValue)
            return SpawnResult.Failed("No floor tile in stairs room");

        grid[stairsPos.Value.X, stairsPos.Value.Y] = TileType.StairsDown;

        // プレイヤースポーンを階段Room以外の接続Roomに配置
        var playerCandidates = connectedRooms.Where(i => i != stairsRoomIndex).ToList();
        if (playerCandidates.Count == 0)
            return SpawnResult.Failed("No valid player spawn room");

        int playerRoomIndex = playerCandidates[rng.Next(playerCandidates.Count)];
        var playerRoom = rooms[playerRoomIndex];
        var playerPos = PickRandomFloorInRoom(grid, playerRoom, rng);
        if (!playerPos.HasValue)
            return SpawnResult.Failed("No floor tile in player room");

        // モンスタースポーン配置: プレイヤーRoom以外の各非隠しRoomに最低1体
        var monsterTargetRooms = connectedRooms
            .Where(i => i != playerRoomIndex && !metadata[i].IsHiddenRoom)
            .ToList();

        var monsterSpawns = new List<Vector2Int>();

        if (monsterTargetRooms.Count > 0)
        {
            // 各対象Roomに最低1体ずつ配置
            foreach (var roomIdx in monsterTargetRooms)
            {
                var pos = PickRandomFloorInRoom(grid, rooms[roomIdx], rng);
                if (pos.HasValue)
                    monsterSpawns.Add(pos.Value);
            }

            // 残りをランダムに分配してInitialMonsterCountに到達させる
            int remaining = config.InitialMonsterCount - monsterSpawns.Count;
            for (int i = 0; i < remaining && monsterTargetRooms.Count > 0; i++)
            {
                var roomIdx = monsterTargetRooms[rng.Next(monsterTargetRooms.Count)];
                var pos = PickRandomFloorInRoom(grid, rooms[roomIdx], rng);
                if (pos.HasValue)
                    monsterSpawns.Add(pos.Value);
            }
        }

        return new SpawnResult
        {
            Success = true,
            StairsPosition = stairsPos.Value,
            PlayerSpawn = playerPos.Value,
            MonsterSpawns = monsterSpawns.AsReadOnly(),
            StairsRoomIndex = stairsRoomIndex,
            PlayerRoomIndex = playerRoomIndex,
        };
    }

    /// <summary>
    /// Room内のFloorタイルからランダムに1つ選択する。
    /// Floorタイルがない場合はnullを返す。
    /// </summary>
    private static Vector2Int? PickRandomFloorInRoom(MapGrid grid, Room room, Random rng)
    {
        var floorTiles = new List<Vector2Int>();
        for (int x = room.X; x < room.X + room.Width; x++)
        {
            for (int y = room.Y; y < room.Y + room.Height; y++)
            {
                if (grid[x, y] == TileType.Floor)
                    floorTiles.Add(new Vector2Int(x, y));
            }
        }

        if (floorTiles.Count == 0) return null;
        return floorTiles[rng.Next(floorTiles.Count)];
    }
}
