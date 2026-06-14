namespace MapViewer.MapGen;

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
    public static CorridorResult Connect(
        IReadOnlyList<Room> rooms,
        IReadOnlyList<RoomMetadata> metadata,
        GenerationConfig config,
        MapGrid grid,
        Random rng)
    {
        if (rooms.Count < 2)
        {
            // Room が1つ以下なら接続不要
            return new CorridorResult { Corridors = [] };
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

        // 各Roomの既存出口点を追跡（同じ壁面に複数出口が並ぶのを防ぐ）
        // Key: (roomIndex, direction) → 既に使われた出口点
        var usedExitPoints = new Dictionary<(int roomIndex, Vector2Int dir), Vector2Int>();

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

            // === 出口側の再利用チェック ===
            var srcKey = (edge.From, exitDir);
            bool srcReused = false;
            Vector2Int actualExitPoint = exitPoint;
            Vector2Int actualExitDir = exitDir;

            if (usedExitPoints.TryGetValue(srcKey, out var existingSrcExit))
            {
                actualExitPoint = existingSrcExit;
                srcReused = true;
            }

            // === 入口側の再利用チェック ===
            var dstKey = (edge.To, entryDir);
            bool dstReused = false;
            Vector2Int actualEntryPoint = entryPoint;
            Vector2Int actualEntryDir = entryDir;

            if (usedExitPoints.TryGetValue(dstKey, out var existingDstExit))
            {
                actualEntryPoint = existingDstExit;
                dstReused = true;
            }

            // ルーティングの始点と終点を決定
            Vector2Int routeStart, routeEnd;

            if (srcReused)
            {
                // 既存出口の2マス先から分岐
                routeStart = new Vector2Int(
                    actualExitPoint.X + actualExitDir.X * 2,
                    actualExitPoint.Y + actualExitDir.Y * 2);
            }
            else
            {
                routeStart = actualExitPoint;
            }

            if (dstReused)
            {
                // 既存入口の2マス先を終点とする（既存通路に合流）
                routeEnd = new Vector2Int(
                    actualEntryPoint.X + actualEntryDir.X * 2,
                    actualEntryPoint.Y + actualEntryDir.Y * 2);
            }
            else
            {
                routeEnd = actualEntryPoint;
            }

            // ルーティング
            List<Vector2Int> corridorPath;
            if (srcReused && dstReused)
            {
                // 両端とも分岐点から: シンプルなL字/S字で接続
                corridorPath = RouteSimplePath(routeStart, routeEnd);
            }
            else if (srcReused)
            {
                // src側は分岐点から、dst側は通常の2マス直進入口
                corridorPath = RouteCorridorPath(routeStart, actualExitDir, routeEnd, actualEntryDir);
            }
            else if (dstReused)
            {
                // src側は通常の2マス直進出口、dst側は分岐点への合流
                corridorPath = RouteCorridorPath(routeStart, actualExitDir, routeEnd, actualEntryDir);
            }
            else
            {
                // 両端とも新規
                corridorPath = RouteCorridorPath(routeStart, actualExitDir, routeEnd, actualEntryDir);
            }

            // 接続元・接続先以外のRoom内部や壁面隣接を通過するか確認
            if (PathCrossesAnyRoomExcept(corridorPath, rooms, edge.From, edge.To) ||
                PathRunsParallelToExistingCorridor(corridorPath, grid))
            {
                if (srcReused || dstReused)
                {
                    // 再利用パスがダメなら諦める
                    continue;
                }
                // 代替ルーティングを試行
                corridorPath = RouteCorridorPathAlternative(routeStart, actualExitDir, routeEnd, actualEntryDir);
                if (PathCrossesAnyRoomExcept(corridorPath, rooms, edge.From, edge.To) ||
                    PathRunsParallelToExistingCorridor(corridorPath, grid))
                    continue;
            }

            // 再利用の場合は分岐元への接続パスを先頭に追加
            var fullPath = new List<Vector2Int>();

            if (srcReused)
            {
                // 既存出口から分岐点までの共有セグメント
                var cur = actualExitPoint;
                fullPath.Add(cur);
                for (int i = 0; i < 2; i++)
                {
                    cur = new Vector2Int(cur.X + actualExitDir.X, cur.Y + actualExitDir.Y);
                    fullPath.Add(cur);
                }
            }

            fullPath.AddRange(corridorPath);

            if (dstReused)
            {
                // 合流点から既存入口までの共有セグメント
                var cur = routeEnd;
                var approachDir = new Vector2Int(-actualEntryDir.X, -actualEntryDir.Y);
                for (int i = 0; i < 2; i++)
                {
                    cur = new Vector2Int(cur.X + approachDir.X, cur.Y + approachDir.Y);
                    fullPath.Add(cur);
                }
            }

            // MapGridへの書き込み
            WriteCorridorToGrid(fullPath, grid);

            corridors.Add(new Corridor
            {
                Path = fullPath,
                SourceRoomIndex = edge.From,
                TargetRoomIndex = edge.To,
            });

            // 出口/入口点を記録（初回のみ）
            if (!srcReused && !usedExitPoints.ContainsKey(srcKey))
            {
                usedExitPoints[srcKey] = exitPoint;
            }
            if (!dstReused && !usedExitPoints.ContainsKey(dstKey))
            {
                usedExitPoints[dstKey] = entryPoint;
            }

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
    public static CorridorResult Connect(
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

        public readonly int CompareTo(GraphEdge other) => Weight.CompareTo(other.Weight);
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
    /// Source roomの壁面からTarget roomに向かう最適な出口点を選択する。
    /// 出口方向に2マス進んだ後に目標に向かえる壁面を選ぶ。
    /// 出口方向（壁面の法線方向）も返す。
    /// </summary>
    private static (Vector2Int point, Vector2Int direction)? PickExitPoint(Room src, Room dst, Random rng)
    {
        // 各壁面候補を評価し、出口方向に進んだ先からdstに向かう際に
        // 逆戻り（出口方向と逆方向）が不要な壁面を選ぶ
        var candidates = GetWallCandidates(src, dst, rng);

        foreach (var (point, dir) in candidates)
        {
            if (point.HasValue)
                return (point.Value, dir);
        }

        return null;
    }

    /// <summary>
    /// Target roomの壁面からSource roomに向かう最適な入口点を選択する。
    /// </summary>
    private static (Vector2Int point, Vector2Int direction)? PickEntryPoint(Room dst, Room src, Random rng)
    {
        var candidates = GetWallCandidates(dst, src, rng);

        foreach (var (point, dir) in candidates)
        {
            if (point.HasValue)
                return (point.Value, dir);
        }

        return null;
    }

    /// <summary>
    /// src部屋からdst部屋に向かうために最適な壁面の候補を優先順位付きで返す。
    /// 「出口方向に2マス進んだ先からdstの中心に向かう際に、出口方向と逆方向に移動する必要がない」壁面を優先する。
    /// </summary>
    private static List<(Vector2Int? point, Vector2Int dir)> GetWallCandidates(Room src, Room dst, Random rng)
    {
        var srcCenter = RoomCenter(src);
        var dstCenter = RoomCenter(dst);

        int dx = dstCenter.X - srcCenter.X;
        int dy = dstCenter.Y - srcCenter.Y;

        // 4壁面の候補を生成
        var allWalls = new List<(Vector2Int? point, Vector2Int dir, int score)>
        {
            (PickRandomWallY(src, src.X + src.Width, rng), Vector2Int.Right, 0),   // 右壁
            (PickRandomWallY(src, src.X - 1, rng), Vector2Int.Left, 0),            // 左壁
            (PickRandomWallX(src, src.Y + src.Height, rng), Vector2Int.Down, 0),   // 下壁
            (PickRandomWallX(src, src.Y - 1, rng), Vector2Int.Up, 0),              // 上壁
        };

        // 各壁面のスコアを計算：出口方向成分でdstに近づけるかどうか
        var scored = new List<(Vector2Int? point, Vector2Int dir, int score)>();
        foreach (var (pt, dir, _) in allWalls)
        {
            if (!pt.HasValue) continue;

            // 出口方向に2マス進んだ先の座標
            var afterExit = new Vector2Int(pt.Value.X + dir.X * 2, pt.Value.Y + dir.Y * 2);

            // afterExitからdstCenterへのベクトル
            int toDstX = dstCenter.X - afterExit.X;
            int toDstY = dstCenter.Y - afterExit.Y;

            // スコア: 出口方向と逆方向に進む必要がないほど高い
            // 出口方向成分で逆戻りが必要 = ペナルティ
            int penalty = 0;
            if (dir.X != 0)
            {
                // 水平方向の出口: afterExitからdstに向かうX成分が出口と逆方向ならペナルティ
                if (Math.Sign(toDstX) == -dir.X && Math.Abs(toDstX) > 2)
                    penalty = 100;
            }
            if (dir.Y != 0)
            {
                // 垂直方向の出口: afterExitからdstに向かうY成分が出口と逆方向ならペナルティ
                if (Math.Sign(toDstY) == -dir.Y && Math.Abs(toDstY) > 2)
                    penalty = 100;
            }

            // dstに近い方が良い（マンハッタン距離の逆数的スコア）
            int dist = Math.Abs(toDstX) + Math.Abs(toDstY);
            int score = -dist - penalty;

            scored.Add((pt, dir, score));
        }

        // スコア降順でソート（高い方が良い）
        scored.Sort((a, b) => b.score.CompareTo(a.score));

        return [.. scored.Select(s => (s.point, s.dir))];
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
    /// 2点間をシンプルなL字型で接続する（分岐点同士の接続用）。
    /// 水平 → 垂直 の順で移動。
    /// </summary>
    private static List<Vector2Int> RouteSimplePath(Vector2Int from, Vector2Int to)
    {
        var path = new List<Vector2Int>();
        var current = from;

        // 水平移動
        int dx = Math.Sign(to.X - current.X);
        while (current.X != to.X)
        {
            path.Add(current);
            current = new Vector2Int(current.X + dx, current.Y);
        }

        // 垂直移動
        int dy = Math.Sign(to.Y - current.Y);
        while (current.Y != to.Y)
        {
            path.Add(current);
            current = new Vector2Int(current.X, current.Y + dy);
        }

        path.Add(current);
        return path;
    }

    /// <summary>
    /// 多段折れ曲がりのCorridorパスを生成する。
    /// 出口から壁面法線方向に2タイル直進 → 中間点への移動 → 入口へ壁面法線方向に2タイル直進。
    /// これにより部屋と通路の接触面は必ず1タイル（エントランス）のみになる。
    /// </summary>
    private static List<Vector2Int> RouteCorridorPath(
        Vector2Int exit, Vector2Int exitDir,
        Vector2Int entry, Vector2Int entryDir)
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
        Vector2Int entry, Vector2Int entryDir)
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
    /// パスがいずれかのRoomに近すぎる（内部または壁面に隣接する）かチェックする。
    /// 接続元・接続先のRoomの壁面ポイント（出口/入口）は除外する。
    /// </summary>
    private static bool PathCrossesAnyRoom(List<Vector2Int> path, IReadOnlyList<Room> rooms)
    {
        foreach (var point in path)
        {
            foreach (var room in rooms)
            {
                if (IsInsideOrAdjacentToRoom(point, room))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// パスが指定の2部屋以外のRoomに近すぎるかチェックする。
    /// 接続元・接続先のRoomは壁面ポイントを含むため除外する。
    /// </summary>
    private static bool PathCrossesAnyRoomExcept(
        List<Vector2Int> path, IReadOnlyList<Room> rooms,
        int srcRoomIndex, int dstRoomIndex)
    {
        for (int i = 0; i < path.Count; i++)
        {
            var point = path[i];
            for (int r = 0; r < rooms.Count; r++)
            {
                if (r == srcRoomIndex || r == dstRoomIndex) continue;
                if (IsInsideOrAdjacentToRoom(point, rooms[r]))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// パスが既存の通路タイルと並走する（隣接して太くなる）かチェックする。
    /// パス上のタイルで、隣接4方向に既にCorridorタイルがあり、
    /// かつそのCorridorタイルがパス自身に含まれない場合は「並走」と判定する。
    /// </summary>
    private static bool PathRunsParallelToExistingCorridor(List<Vector2Int> path, MapGrid grid)
    {
        var pathSet = new HashSet<Vector2Int>(path);

        for (int i = 1; i < path.Count - 1; i++) // 始点と終点は除外（分岐点での合流を許可）
        {
            var point = path[i];
            foreach (var dir in Vector2Int.FourDirections)
            {
                var neighbor = point + dir;
                if (pathSet.Contains(neighbor)) continue; // パス自身のタイルは除外

                if (grid.InBounds(neighbor.X, neighbor.Y) &&
                    grid[neighbor.X, neighbor.Y] == TileType.Corridor)
                {
                    // 移動方向を取得
                    Vector2Int moveDir;
                    if (i > 0)
                    {
                        moveDir = new Vector2Int(
                            Math.Sign(path[i].X - path[i - 1].X),
                            Math.Sign(path[i].Y - path[i - 1].Y));
                    }
                    else
                    {
                        moveDir = new Vector2Int(0, 0);
                    }

                    // 隣接Corridorが移動方向と直交する方向にある場合は並走
                    // (移動が水平なら上下にCorridorがあると並走、垂直なら左右)
                    if (moveDir.X != 0 && dir.Y != 0) return true; // 水平移動中に上下にCorridor
                    if (moveDir.Y != 0 && dir.X != 0) return true; // 垂直移動中に左右にCorridor
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 座標がRoom内部またはRoomの壁面に隣接する（1タイル以内）かを判定する。
    /// 通路は部屋から最低1マス離れる必要がある。
    /// </summary>
    private static bool IsInsideOrAdjacentToRoom(Vector2Int point, Room room)
    {
        // 部屋の1タイル外側の矩形に含まれるかチェック
        return point.X >= room.X - 1 && point.X < room.X + room.Width + 1
            && point.Y >= room.Y - 1 && point.Y < room.Y + room.Height + 1;
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
