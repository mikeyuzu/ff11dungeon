namespace FF11Dungeon.MapGen;

/// <summary>
/// MST (最小全域木) + 追加エッジによるグラフベースのCorridor生成。
/// Room中心間のグラフを構築し、MSTで全Roomを接続した上で
/// ランダムな追加エッジでループ/分岐を作成する。
/// 従来の隣接Partition方式も後方互換のため維持する。
/// </summary>
public sealed class CorridorConnector
{
    /// <summary>
    /// MST + 追加エッジによるグラフベースCorridor生成。
    /// BSP方式のRoom配置と組み合わせて使用する。
    /// </summary>
    /// <param name="rooms">生成済みのRoom一覧</param>
    /// <param name="metadata">各Roomのメタデータ</param>
    /// <param name="config">生成設定</param>
    /// <param name="grid">書き込み先のMapGrid</param>
    /// <param name="rng">乱数生成器</param>
    /// <returns>生成されたCorridor一覧</returns>
    public CorridorResult Connect(
        IReadOnlyList<Room> rooms,
        IReadOnlyList<RoomMetadata> metadata,
        GenerationConfig config,
        MapGrid grid,
        Random rng)
    {
        if (rooms.Count < 2)
        {
            // Room が1つ以下なら接続不要
            return new CorridorResult { Corridors = Array.Empty<Corridor>() };
        }

        var corridors = new List<Corridor>();
        var connectionCount = new int[rooms.Count];

        // 1. 完全グラフのエッジを構築（重み = マンハッタン距離）
        var allEdges = BuildCompleteGraph(rooms);

        // 2. Kruskal法でMSTを計算
        var mstEdges = ComputeMST(allEdges, rooms.Count);

        // 3. 非MSTエッジから追加エッジをランダムに選択（ループ/分岐の生成）
        var mstSet = new HashSet<(int, int)>();
        foreach (var edge in mstEdges)
        {
            mstSet.Add((Math.Min(edge.From, edge.To), Math.Max(edge.From, edge.To)));
        }

        var extraEdges = new List<GraphEdge>();
        foreach (var edge in allEdges)
        {
            var key = (Math.Min(edge.From, edge.To), Math.Max(edge.From, edge.To));
            if (!mstSet.Contains(key))
            {
                // CorridorPruneChance を「追加エッジの除外確率」として逆用
                // (1 - CorridorPruneChance) が追加確率、基本20%追加
                float addChance = 0.2f * (1.0f - config.CorridorPruneChance);
                if (rng.NextDouble() < addChance)
                {
                    extraEdges.Add(edge);
                }
            }
        }

        // 4. MST + 追加エッジの全接続を処理
        var connectEdges = new List<GraphEdge>(mstEdges);
        connectEdges.AddRange(extraEdges);

        foreach (var edge in connectEdges)
        {
            var srcRoom = rooms[edge.From];
            var dstRoom = rooms[edge.To];

            // 出入口点の選択（壁面法線方向も取得）
            var exitResult = PickExitPoint(srcRoom, dstRoom, rng);
            var entryResult = PickEntryPoint(dstRoom, srcRoom, rng);

            if (!exitResult.HasValue || !entryResult.HasValue)
                continue;

            var (exitPoint, exitDir) = exitResult.Value;
            var (entryPoint, entryDir) = entryResult.Value;

            // 多段折れ曲がりルーティング（壁面法線方向で2マス直進を保証）
            var path = RouteCorridorPath(exitPoint, exitDir, entryPoint, entryDir, rng);

            // Room内部を通過するか確認
            if (PathCrossesAnyRoom(path, rooms))
            {
                // 代替ルーティングを試行
                path = RouteCorridorPathAlternative(exitPoint, exitDir, entryPoint, entryDir, rng);
                if (PathCrossesAnyRoom(path, rooms))
                    continue; // 両方ダメなら接続を諦める
            }

            // MapGridへの書き込み
            WriteCorridorToGrid(path, grid);

            corridors.Add(new Corridor
            {
                Path = path,
                SourceRoomIndex = edge.From,
                TargetRoomIndex = edge.To,
            });

            connectionCount[edge.From]++;
            connectionCount[edge.To]++;
        }

        // 5. 孤立Room（接続0）にhidden_roomフラグを設定
        for (int i = 0; i < rooms.Count; i++)
        {
            if (connectionCount[i] == 0)
            {
                metadata[i].IsHiddenRoom = true;
            }
        }

        return new CorridorResult { Corridors = corridors.AsReadOnly() };
    }

    /// <summary>
    /// 従来の隣接Partition間接続方式（後方互換性のため維持）。
    /// </summary>
    public CorridorResult Connect(
        PartitionGrid partitions,
        IReadOnlyList<Room> rooms,
        IReadOnlyList<RoomMetadata> metadata,
        GenerationConfig config,
        MapGrid grid,
        Random rng)
    {
        var partitionToRoom = new Dictionary<(int row, int col), int>();
        for (int i = 0; i < rooms.Count; i++)
        {
            var key = (rooms[i].PartitionRow, rooms[i].PartitionCol);
            if (!partitionToRoom.ContainsKey(key))
            {
                partitionToRoom[key] = i;
            }
        }

        var corridors = new List<Corridor>();
        var connectionCount = new int[rooms.Count];

        for (int row = 0; row < partitions.Rows; row++)
        {
            for (int col = 0; col < partitions.Columns; col++)
            {
                if (col + 1 < partitions.Columns)
                {
                    TryCreateLegacyCorridor(
                        partitionToRoom, rooms, config, grid, rng, corridors, connectionCount,
                        row, col, row, col + 1, LegacyDirection.Horizontal);
                }

                if (row + 1 < partitions.Rows)
                {
                    TryCreateLegacyCorridor(
                        partitionToRoom, rooms, config, grid, rng, corridors, connectionCount,
                        row, col, row + 1, col, LegacyDirection.Vertical);
                }
            }
        }

        for (int i = 0; i < rooms.Count; i++)
        {
            if (connectionCount[i] == 0)
            {
                metadata[i].IsHiddenRoom = true;
            }
        }

        return new CorridorResult { Corridors = corridors.AsReadOnly() };
    }

    #region Graph-based MST Methods

    private struct GraphEdge : IComparable<GraphEdge>
    {
        public int From;
        public int To;
        public int Weight;

        public int CompareTo(GraphEdge other) => Weight.CompareTo(other.Weight);
    }

    /// <summary>
    /// 完全グラフのエッジリストを構築（重み = マンハッタン距離）。
    /// </summary>
    private static List<GraphEdge> BuildCompleteGraph(IReadOnlyList<Room> rooms)
    {
        var edges = new List<GraphEdge>();
        for (int i = 0; i < rooms.Count; i++)
        {
            var ci = RoomCenter(rooms[i]);
            for (int j = i + 1; j < rooms.Count; j++)
            {
                var cj = RoomCenter(rooms[j]);
                int dist = Math.Abs(ci.X - cj.X) + Math.Abs(ci.Y - cj.Y);
                edges.Add(new GraphEdge { From = i, To = j, Weight = dist });
            }
        }
        return edges;
    }

    /// <summary>
    /// Kruskal法によるMST計算。
    /// </summary>
    private static List<GraphEdge> ComputeMST(List<GraphEdge> edges, int nodeCount)
    {
        edges.Sort();
        var parent = new int[nodeCount];
        var rank = new int[nodeCount];
        for (int i = 0; i < nodeCount; i++) parent[i] = i;

        var mst = new List<GraphEdge>();
        foreach (var edge in edges)
        {
            int rootA = Find(parent, edge.From);
            int rootB = Find(parent, edge.To);
            if (rootA != rootB)
            {
                mst.Add(edge);
                Union(parent, rank, rootA, rootB);
                if (mst.Count == nodeCount - 1) break;
            }
        }
        return mst;
    }

    private static int Find(int[] parent, int x)
    {
        while (parent[x] != x)
        {
            parent[x] = parent[parent[x]]; // path compression
            x = parent[x];
        }
        return x;
    }

    private static void Union(int[] parent, int[] rank, int a, int b)
    {
        if (rank[a] < rank[b]) parent[a] = b;
        else if (rank[a] > rank[b]) parent[b] = a;
        else { parent[b] = a; rank[a]++; }
    }

    private static Vector2Int RoomCenter(Room room)
        => new(room.X + room.Width / 2, room.Y + room.Height / 2);

    #endregion

    #region Corridor Routing (Graph-based)

    /// <summary>
    /// Source roomの壁面からTarget方向に面した出口点を選択する。
    /// 出口方向（壁面の法線方向）も返す。
    /// </summary>
    private static (Vector2Int point, Vector2Int direction)? PickExitPoint(Room src, Room dst, Random rng)
    {
        var srcCenter = RoomCenter(src);
        var dstCenter = RoomCenter(dst);

        int dx = dstCenter.X - srcCenter.X;
        int dy = dstCenter.Y - srcCenter.Y;

        // Target方向に面した壁面を選択
        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            if (dx > 0)
            {
                // 右壁から出る → 法線方向は Right(+X)
                int x = src.X + src.Width;
                var pt = PickRandomWallY(src, x, rng);
                return pt.HasValue ? (pt.Value, Vector2Int.Right) : null;
            }
            else
            {
                // 左壁から出る → 法線方向は Left(-X)
                int x = src.X - 1;
                var pt = PickRandomWallY(src, x, rng);
                return pt.HasValue ? (pt.Value, Vector2Int.Left) : null;
            }
        }
        else
        {
            if (dy > 0)
            {
                // 下壁から出る → 法線方向は Down(+Y)
                int y = src.Y + src.Height;
                var pt = PickRandomWallX(src, y, rng);
                return pt.HasValue ? (pt.Value, Vector2Int.Down) : null;
            }
            else
            {
                // 上壁から出る → 法線方向は Up(-Y)
                int y = src.Y - 1;
                var pt = PickRandomWallX(src, y, rng);
                return pt.HasValue ? (pt.Value, Vector2Int.Up) : null;
            }
        }
    }

    /// <summary>
    /// Target roomの壁面からSource方向に面した入口点を選択する。
    /// 入口方向（壁面の法線方向＝部屋から離れる方向）も返す。
    /// </summary>
    private static (Vector2Int point, Vector2Int direction)? PickEntryPoint(Room dst, Room src, Random rng)
    {
        var srcCenter = RoomCenter(src);
        var dstCenter = RoomCenter(dst);

        int dx = srcCenter.X - dstCenter.X;
        int dy = srcCenter.Y - dstCenter.Y;

        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            if (dx > 0)
            {
                int x = dst.X + dst.Width;
                var pt = PickRandomWallY(dst, x, rng);
                return pt.HasValue ? (pt.Value, Vector2Int.Right) : null;
            }
            else
            {
                int x = dst.X - 1;
                var pt = PickRandomWallY(dst, x, rng);
                return pt.HasValue ? (pt.Value, Vector2Int.Left) : null;
            }
        }
        else
        {
            if (dy > 0)
            {
                int y = dst.Y + dst.Height;
                var pt = PickRandomWallX(dst, y, rng);
                return pt.HasValue ? (pt.Value, Vector2Int.Down) : null;
            }
            else
            {
                int y = dst.Y - 1;
                var pt = PickRandomWallX(dst, y, rng);
                return pt.HasValue ? (pt.Value, Vector2Int.Up) : null;
            }
        }
    }

    /// <summary>
    /// 左壁または右壁上のランダムなY座標を選択（角除外）。
    /// </summary>
    private static Vector2Int? PickRandomWallY(Room room, int x, Random rng)
    {
        int yMin = room.Y + 1;
        int yMax = room.Y + room.Height - 2;
        if (yMin > yMax) return null;
        int y = rng.Next(yMin, yMax + 1);
        return new Vector2Int(x, y);
    }

    /// <summary>
    /// 上壁または下壁上のランダムなX座標を選択（角除外）。
    /// </summary>
    private static Vector2Int? PickRandomWallX(Room room, int y, Random rng)
    {
        int xMin = room.X + 1;
        int xMax = room.X + room.Width - 2;
        if (xMin > xMax) return null;
        int x = rng.Next(xMin, xMax + 1);
        return new Vector2Int(x, y);
    }

    /// <summary>
    /// 多段折れ曲がりのCorridorパスを生成する。
    /// 出口から壁面法線方向に2タイル直進 → 中間点への移動 → 入口へ壁面法線方向に2タイル直進。
    /// これにより部屋と通路の接触面は必ず1タイル（エントランス）のみになる。
    /// </summary>
    private static List<Vector2Int> RouteCorridorPath(
        Vector2Int exit, Vector2Int exitDir,
        Vector2Int entry, Vector2Int entryDir,
        Random rng)
    {
        var path = new List<Vector2Int>();

        // Step 1: 出口から壁面法線方向に2タイル直進
        var current = exit;
        path.Add(current);
        for (int i = 0; i < 2; i++)
        {
            current = new Vector2Int(current.X + exitDir.X, current.Y + exitDir.Y);
            path.Add(current);
        }
        var afterExit = current;

        // Step 2: 入口から壁面法線方向に2タイル戻した点を計算（入口の手前）
        var beforeEntry = new Vector2Int(
            entry.X + entryDir.X * 2,
            entry.Y + entryDir.Y * 2);

        // Step 3: afterExit → beforeEntry をS字でルーティング
        // 水平 → 垂直 → 水平 の3セグメント
        var midX = (afterExit.X + beforeEntry.X) / 2;

        current = afterExit;

        // Segment A: 水平移動 → midX
        path.AddRange(WalkHorizontal(ref current, midX));

        // Segment B: 垂直移動 → beforeEntry.Y
        path.AddRange(WalkVertical(ref current, beforeEntry.Y));

        // Segment C: 水平移動 → beforeEntry.X
        path.AddRange(WalkHorizontal(ref current, beforeEntry.X));

        // Step 4: beforeEntry → entry へ壁面法線方向の逆に2タイル直進
        var entryApproach = new Vector2Int(-entryDir.X, -entryDir.Y);
        current = beforeEntry;
        path.Add(current);
        for (int i = 0; i < 2; i++)
        {
            current = new Vector2Int(current.X + entryApproach.X, current.Y + entryApproach.Y);
            path.Add(current);
        }

        return path;
    }

    /// <summary>
    /// 代替ルーティング: 垂直先行のS字型。
    /// </summary>
    private static List<Vector2Int> RouteCorridorPathAlternative(
        Vector2Int exit, Vector2Int exitDir,
        Vector2Int entry, Vector2Int entryDir,
        Random rng)
    {
        var path = new List<Vector2Int>();

        // Step 1: 出口から壁面法線方向に2タイル直進
        var current = exit;
        path.Add(current);
        for (int i = 0; i < 2; i++)
        {
            current = new Vector2Int(current.X + exitDir.X, current.Y + exitDir.Y);
            path.Add(current);
        }
        var afterExit = current;

        // Step 2: 入口の2タイル手前
        var beforeEntry = new Vector2Int(
            entry.X + entryDir.X * 2,
            entry.Y + entryDir.Y * 2);

        // 代替: 垂直 → 水平 → 垂直
        var midY = (afterExit.Y + beforeEntry.Y) / 2;

        current = afterExit;

        // Segment A: 垂直移動 → midY
        path.AddRange(WalkVertical(ref current, midY));

        // Segment B: 水平移動 → beforeEntry.X
        path.AddRange(WalkHorizontal(ref current, beforeEntry.X));

        // Segment C: 垂直移動 → beforeEntry.Y
        path.AddRange(WalkVertical(ref current, beforeEntry.Y));

        // Step 3: beforeEntry → entry
        var entryApproach = new Vector2Int(-entryDir.X, -entryDir.Y);
        current = beforeEntry;
        path.Add(current);
        for (int i = 0; i < 2; i++)
        {
            current = new Vector2Int(current.X + entryApproach.X, current.Y + entryApproach.Y);
            path.Add(current);
        }

        return path;
    }

    /// <summary>
    /// 水平方向に1タイルずつ歩いてパスポイントを生成する。
    /// </summary>
    private static List<Vector2Int> WalkHorizontal(ref Vector2Int current, int targetX)
    {
        var points = new List<Vector2Int>();
        int dx = Math.Sign(targetX - current.X);
        while (current.X != targetX)
        {
            points.Add(current);
            current = new Vector2Int(current.X + dx, current.Y);
        }
        return points;
    }

    /// <summary>
    /// 垂直方向に1タイルずつ歩いてパスポイントを生成する。
    /// </summary>
    private static List<Vector2Int> WalkVertical(ref Vector2Int current, int targetY)
    {
        var points = new List<Vector2Int>();
        int dy = Math.Sign(targetY - current.Y);
        while (current.Y != targetY)
        {
            points.Add(current);
            current = new Vector2Int(current.X, current.Y + dy);
        }
        return points;
    }

    #endregion

    #region Common Helpers

    /// <summary>
    /// パスがいずれかのRoomのFloor領域を横断するかチェックする。
    /// </summary>
    private static bool PathCrossesAnyRoom(List<Vector2Int> path, IReadOnlyList<Room> rooms)
    {
        foreach (var point in path)
        {
            foreach (var room in rooms)
            {
                if (IsInsideRoom(point, room))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// 座標がRoom内部（Floor領域）に含まれるかを判定する。
    /// </summary>
    private static bool IsInsideRoom(Vector2Int point, Room room)
    {
        return point.X >= room.X && point.X < room.X + room.Width
            && point.Y >= room.Y && point.Y < room.Y + room.Height;
    }

    /// <summary>
    /// CorridorパスのタイルをMapGridに書き込む（Wall位置のみ）。
    /// </summary>
    private static void WriteCorridorToGrid(List<Vector2Int> path, MapGrid grid)
    {
        foreach (var point in path)
        {
            if (grid.InBounds(point.X, point.Y) && grid[point.X, point.Y] == TileType.Wall)
            {
                grid[point.X, point.Y] = TileType.Corridor;
            }
        }
    }

    #endregion

    #region Legacy Partition-based Methods

    private enum LegacyDirection
    {
        Horizontal,
        Vertical
    }

    private static void TryCreateLegacyCorridor(
        Dictionary<(int row, int col), int> partitionToRoom,
        IReadOnlyList<Room> rooms,
        GenerationConfig config,
        MapGrid grid,
        Random rng,
        List<Corridor> corridors,
        int[] connectionCount,
        int srcRow, int srcCol,
        int dstRow, int dstCol,
        LegacyDirection direction)
    {
        if (!partitionToRoom.TryGetValue((srcRow, srcCol), out int srcIndex))
            return;
        if (!partitionToRoom.TryGetValue((dstRow, dstCol), out int dstIndex))
            return;

        if (rng.NextDouble() < config.CorridorPruneChance)
            return;

        var srcRoom = rooms[srcIndex];
        var dstRoom = rooms[dstIndex];

        var exitPoint = PickLegacyWallPoint(srcRoom, direction, isSource: true, rng);
        if (!exitPoint.HasValue) return;

        var entryPoint = PickLegacyWallPoint(dstRoom, direction, isSource: false, rng);
        if (!entryPoint.HasValue) return;

        var path = RouteLegacyPath(exitPoint.Value, entryPoint.Value, direction);

        if (PathCrossesAnyRoom(path, rooms))
        {
            path = RouteLegacyPathAlternative(exitPoint.Value, entryPoint.Value, direction);
            if (PathCrossesAnyRoom(path, rooms))
                return;
        }

        WriteCorridorToGrid(path, grid);

        corridors.Add(new Corridor
        {
            Path = path,
            SourceRoomIndex = srcIndex,
            TargetRoomIndex = dstIndex,
        });

        connectionCount[srcIndex]++;
        connectionCount[dstIndex]++;
    }

    private static Vector2Int? PickLegacyWallPoint(Room room, LegacyDirection direction, bool isSource, Random rng)
    {
        if (direction == LegacyDirection.Horizontal)
        {
            if (isSource)
            {
                int x = room.X + room.Width;
                int yMin = room.Y + 1;
                int yMax = room.Y + room.Height - 2;
                if (yMin > yMax) return null;
                int y = rng.Next(yMin, yMax + 1);
                return new Vector2Int(x, y);
            }
            else
            {
                int x = room.X - 1;
                int yMin = room.Y + 1;
                int yMax = room.Y + room.Height - 2;
                if (yMin > yMax) return null;
                int y = rng.Next(yMin, yMax + 1);
                return new Vector2Int(x, y);
            }
        }
        else
        {
            if (isSource)
            {
                int y = room.Y + room.Height;
                int xMin = room.X + 1;
                int xMax = room.X + room.Width - 2;
                if (xMin > xMax) return null;
                int x = rng.Next(xMin, xMax + 1);
                return new Vector2Int(x, y);
            }
            else
            {
                int y = room.Y - 1;
                int xMin = room.X + 1;
                int xMax = room.X + room.Width - 2;
                if (xMin > xMax) return null;
                int x = rng.Next(xMin, xMax + 1);
                return new Vector2Int(x, y);
            }
        }
    }

    private static List<Vector2Int> RouteLegacyPath(Vector2Int from, Vector2Int to, LegacyDirection direction)
    {
        var path = new List<Vector2Int>();
        int x = from.X;
        int y = from.Y;

        if (direction == LegacyDirection.Horizontal)
        {
            int dx = Math.Sign(to.X - x);
            for (int i = 0; i < 2 && x != to.X; i++)
            {
                path.Add(new Vector2Int(x, y));
                x += dx;
            }
            int dy = Math.Sign(to.Y - y);
            while (y != to.Y)
            {
                path.Add(new Vector2Int(x, y));
                y += dy;
            }
            while (x != to.X)
            {
                path.Add(new Vector2Int(x, y));
                x += dx;
            }
        }
        else
        {
            int dy = Math.Sign(to.Y - y);
            for (int i = 0; i < 2 && y != to.Y; i++)
            {
                path.Add(new Vector2Int(x, y));
                y += dy;
            }
            int dx = Math.Sign(to.X - x);
            while (x != to.X)
            {
                path.Add(new Vector2Int(x, y));
                x += dx;
            }
            while (y != to.Y)
            {
                path.Add(new Vector2Int(x, y));
                y += dy;
            }
        }

        path.Add(new Vector2Int(x, y));
        return path;
    }

    private static List<Vector2Int> RouteLegacyPathAlternative(Vector2Int from, Vector2Int to, LegacyDirection direction)
    {
        var reversed = RouteLegacyPath(to, from, direction);
        reversed.Reverse();
        return reversed;
    }

    #endregion
}
