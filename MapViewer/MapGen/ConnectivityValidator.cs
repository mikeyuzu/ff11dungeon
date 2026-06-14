namespace FF11Dungeon.MapGen;

/// <summary>
/// プレイヤースポーン地点からBFSを行い、すべての非隠しRoomへの到達性を検証する。
/// </summary>
public sealed class ConnectivityValidator
{
    /// <summary>
    /// 指定された開始タイルからBFSを実行し、すべての非隠しRoomの少なくとも1タイルに到達可能かを検証する。
    /// </summary>
    /// <param name="grid">マップグリッド</param>
    /// <param name="rooms">Room一覧（隠しRoomを含む）</param>
    /// <param name="startTile">BFS開始地点（プレイヤースポーン位置のFloorタイル）</param>
    /// <returns>すべての非隠しRoomに到達可能であればtrue</returns>
    public bool Validate(MapGrid grid, IReadOnlyList<RoomMetadata> rooms, Vector2Int startTile)
    {
        var visited = new HashSet<Vector2Int>();
        var queue = new Queue<Vector2Int>();

        queue.Enqueue(startTile);
        visited.Add(startTile);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var dir in Vector2Int.FourDirections)
            {
                var next = current + dir;
                if (visited.Contains(next)) continue;
                if (!grid.IsPassable(next.X, next.Y)) continue;

                visited.Add(next);
                queue.Enqueue(next);
            }
        }

        // 各非隠しRoomの少なくとも1タイルに到達可能か検証
        foreach (var meta in rooms)
        {
            if (meta.IsHiddenRoom) continue;

            var room = meta.Room;
            bool reachable = false;
            for (int x = room.X; x < room.X + room.Width && !reachable; x++)
            {
                for (int y = room.Y; y < room.Y + room.Height && !reachable; y++)
                {
                    if (visited.Contains(new Vector2Int(x, y)))
                        reachable = true;
                }
            }

            if (!reachable) return false;
        }

        return true;
    }
}
