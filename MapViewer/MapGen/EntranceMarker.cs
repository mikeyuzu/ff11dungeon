namespace FF11Dungeon.MapGen;

/// <summary>
/// 各Corridor-Room接続について、CorridorがRoom壁面を貫通する位置の
/// タイルをRoomEntranceに置換する。
/// </summary>
public sealed class EntranceMarker
{
    /// <summary>
    /// 各Corridor-Room接続について、CorridorがRoom壁面を貫通する位置の
    /// タイルをRoomEntranceに置換する。
    /// 各Corridor-Room接続ごとに正確に1タイルのRoomEntranceを配置する。
    /// </summary>
    /// <param name="grid">書き込み先のMapGrid</param>
    /// <param name="rooms">生成済みのRoom一覧</param>
    /// <param name="corridors">生成済みのCorridor一覧</param>
    public void MarkEntrances(
        MapGrid grid,
        IReadOnlyList<Room> rooms,
        IReadOnlyList<Corridor> corridors)
    {
        foreach (var corridor in corridors)
        {
            // Source room entrance: corridor path上でsource roomの壁面リングに該当する最初のポイント
            MarkRoomEntrance(grid, rooms[corridor.SourceRoomIndex], corridor.Path, fromStart: true);

            // Target room entrance: corridor path上でtarget roomの壁面リングに該当する最後のポイント
            MarkRoomEntrance(grid, rooms[corridor.TargetRoomIndex], corridor.Path, fromStart: false);
        }
    }

    /// <summary>
    /// Corridor pathを走査し、指定Roomの壁面リング上にある最初（または最後）の
    /// ポイントをRoomEntranceタイルに置換する。
    /// </summary>
    private static void MarkRoomEntrance(
        MapGrid grid,
        Room room,
        IReadOnlyList<Vector2Int> path,
        bool fromStart)
    {
        if (fromStart)
        {
            // パスの先頭から走査し、Roomの壁面リング上にあるポイントを探す
            for (int i = 0; i < path.Count; i++)
            {
                if (IsOnWallRing(path[i], room))
                {
                    SetEntrance(grid, path[i]);
                    return;
                }
            }
        }
        else
        {
            // パスの末尾から走査し、Roomの壁面リング上にあるポイントを探す
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (IsOnWallRing(path[i], room))
                {
                    SetEntrance(grid, path[i]);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 指定座標がRoomの壁面リング（Room Floorに隣接する外側1タイル）上にあるかを判定する。
    /// 壁面リング:
    ///   上壁: y = room.Y - 1, x in [room.X - 1, room.X + room.Width]
    ///   下壁: y = room.Y + room.Height, x in [room.X - 1, room.X + room.Width]
    ///   左壁: x = room.X - 1, y in [room.Y - 1, room.Y + room.Height]
    ///   右壁: x = room.X + room.Width, y in [room.Y - 1, room.Y + room.Height]
    /// CorridorConnectorのPickWallPointは角タイルを除外するため、
    /// ここではCorridorが実際に到達しうる壁面位置（角除外）のみを判定する。
    /// </summary>
    private static bool IsOnWallRing(Vector2Int point, Room room)
    {
        int x = point.X;
        int y = point.Y;

        // 上壁: y = room.Y - 1, x in [room.X, room.X + room.Width - 1]
        if (y == room.Y - 1 && x >= room.X && x < room.X + room.Width)
            return true;

        // 下壁: y = room.Y + room.Height, x in [room.X, room.X + room.Width - 1]
        if (y == room.Y + room.Height && x >= room.X && x < room.X + room.Width)
            return true;

        // 左壁: x = room.X - 1, y in [room.Y, room.Y + room.Height - 1]
        if (x == room.X - 1 && y >= room.Y && y < room.Y + room.Height)
            return true;

        // 右壁: x = room.X + room.Width, y in [room.Y, room.Y + room.Height - 1]
        if (x == room.X + room.Width && y >= room.Y && y < room.Y + room.Height)
            return true;

        return false;
    }

    /// <summary>
    /// 指定座標のタイルをRoomEntranceに設定する。
    /// RoomEntranceはWall, Floor, Corridorを上書きする（優先順位ルール: Requirement 5.5）。
    /// </summary>
    private static void SetEntrance(MapGrid grid, Vector2Int point)
    {
        if (grid.InBounds(point.X, point.Y))
        {
            grid[point.X, point.Y] = TileType.RoomEntrance;
        }
    }
}
