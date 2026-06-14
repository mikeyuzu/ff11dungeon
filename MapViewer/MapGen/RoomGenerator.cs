namespace MapViewer.MapGen;

/// <summary>
/// BSP (Binary Space Partitioning) による自由配置型のRoom生成と、
/// 従来のPartitionGrid型Room生成の両方をサポートする。
/// </summary>
public sealed class RoomGenerator
{
    private const int LeafMargin = 3; // BSP leafの境界からのマージン（通路ルーティング用）

    /// <summary>
    /// BSPを使用してマップ全域にRoomを自由配置し、MapGridへFloorタイルを書き込む。
    /// GridRows * GridColumns を目標Room数として使用する。
    /// </summary>
    /// <param name="config">生成設定（クランプ済み推奨）</param>
    /// <param name="grid">書き込み先のMapGrid</param>
    /// <param name="rng">乱数生成器</param>
    /// <returns>生成されたRoomとメタデータのリスト</returns>
    public static RoomGenerationResult GenerateRoomsBSP(
        GenerationConfig config,
        MapGrid grid,
        Random rng)
    {
        int targetRoomCount = config.GridRows * config.GridColumns;

        // 1. BSPでマップ領域を分割（1タイルボーダーを除く）
        var rootLeaf = new BspLeaf(1, 1, config.MapWidth - 2, config.MapHeight - 2);
        var leaves = new List<BspLeaf>();
        SplitBsp(rootLeaf, leaves, targetRoomCount, rng);

        // 2. 各leafにRoomを配置
        var rooms = new List<Room>();
        var metadata = new List<RoomMetadata>();
        var skippedLeaves = new List<BspLeaf>();
        int leafIndex = 0;

        foreach (var leaf in leaves)
        {
            // 空き区画確率によるスキップ
            if (rng.NextDouble() < config.EmptyPartitionChance)
            {
                skippedLeaves.Add(leaf);
                leafIndex++;
                continue;
            }

            var room = TryPlaceRoomInLeaf(leaf, config, rng, leafIndex);
            if (room.HasValue)
            {
                rooms.Add(room.Value);
                metadata.Add(new RoomMetadata { Room = room.Value });
            }
            leafIndex++;
        }

        // 3. 最低2部屋保証: スキップしたleafから強制生成
        int skipIdx = 0;
        while (rooms.Count < 2 && skipIdx < skippedLeaves.Count)
        {
            var leaf = skippedLeaves[skipIdx];
            var room = TryPlaceRoomInLeaf(leaf, config, rng, skipIdx);
            if (room.HasValue)
            {
                rooms.Add(room.Value);
                metadata.Add(new RoomMetadata { Room = room.Value });
            }
            skipIdx++;
        }

        // 4. MapGridへのFloorタイル書き込み
        foreach (var room in rooms)
        {
            WriteRoomToGrid(room, grid);
        }

        return new RoomGenerationResult
        {
            Rooms = rooms.AsReadOnly(),
            Metadata = metadata.AsReadOnly(),
        };
    }

    /// <summary>
    /// PartitionGridの各区画にRoomを生成し、MapGridへFloorタイルを書き込む。
    /// （従来方式 — 後方互換性のため維持）
    /// </summary>
    public static RoomGenerationResult GenerateRooms(
        PartitionGrid partitions,
        GenerationConfig config,
        MapGrid grid,
        Random rng)
    {
        var rooms = new List<Room>();
        var metadata = new List<RoomMetadata>();
        var emptyPartitions = new List<(int row, int col)>();

        for (int row = 0; row < partitions.Rows; row++)
        {
            for (int col = 0; col < partitions.Columns; col++)
            {
                var partition = partitions[row, col];

                if (rng.NextDouble() < config.EmptyPartitionChance)
                {
                    emptyPartitions.Add((row, col));
                    continue;
                }

                var room = TryGenerateRoom(partition, config, rng);
                if (room.HasValue)
                {
                    rooms.Add(room.Value);
                    metadata.Add(new RoomMetadata { Room = room.Value });
                }
            }
        }

        int emptyIndex = 0;
        while (rooms.Count < 2 && emptyIndex < emptyPartitions.Count)
        {
            var (row, col) = emptyPartitions[emptyIndex];
            var partition = partitions[row, col];

            var room = TryGenerateRoom(partition, config, rng);
            if (room.HasValue)
            {
                rooms.Add(room.Value);
                metadata.Add(new RoomMetadata { Room = room.Value });
            }
            emptyIndex++;
        }

        foreach (var room in rooms)
        {
            WriteRoomToGrid(room, grid);
        }

        return new RoomGenerationResult
        {
            Rooms = rooms.AsReadOnly(),
            Metadata = metadata.AsReadOnly(),
        };
    }

    #region BSP Methods

    /// <summary>
    /// BSPによる再帰分割。目標leaf数に達するまで分割を続ける。
    /// </summary>
    private static void SplitBsp(BspLeaf leaf, List<BspLeaf> leaves, int targetCount, Random rng)
    {
        // 現在のleaf数 + 分割可能な残りを考慮
        var queue = new Queue<BspLeaf>();
        queue.Enqueue(leaf);

        while (queue.Count + leaves.Count < targetCount && queue.Count > 0)
        {
            var current = queue.Dequeue();

            // 分割を試行
            var (left, right) = TrySplitLeaf(current, rng);
            if (left != null && right != null)
            {
                queue.Enqueue(left);
                queue.Enqueue(right);
            }
            else
            {
                // これ以上分割不可 → 確定leaf
                leaves.Add(current);
            }
        }

        // キューに残ったleafはすべて確定
        while (queue.Count > 0)
        {
            leaves.Add(queue.Dequeue());
        }
    }

    /// <summary>
    /// leafを水平または垂直に分割する。分割後のサイズが小さすぎる場合はnullを返す。
    /// </summary>
    private static (BspLeaf? left, BspLeaf? right) TrySplitLeaf(BspLeaf leaf, Random rng)
    {
        // 部屋 + マージン*2 に必要な最小leafサイズ
        const int MinLeafSize = 5 + LeafMargin * 2; // 5(最小部屋) + 6(マージン両側) = 11

        bool canSplitH = leaf.Height >= MinLeafSize * 2;
        bool canSplitV = leaf.Width >= MinLeafSize * 2;

        if (!canSplitH && !canSplitV)
            return (null, null);

        bool splitHorizontally;
        if (canSplitH && canSplitV)
        {
            // アスペクト比に基づいて分割方向を決定（細長い方向に分割しやすくする）
            if (leaf.Width > leaf.Height * 1.25f)
                splitHorizontally = false; // 幅が広い → 垂直分割
            else if (leaf.Height > leaf.Width * 1.25f)
                splitHorizontally = true; // 高さが高い → 水平分割
            else
                splitHorizontally = rng.Next(2) == 0;
        }
        else
        {
            splitHorizontally = canSplitH;
        }

        if (splitHorizontally)
        {
            // 水平分割: Y軸で分ける
            int minSplit = leaf.Y + MinLeafSize;
            int maxSplit = leaf.Y + leaf.Height - MinLeafSize;
            if (minSplit > maxSplit) return (null, null);

            int splitY = rng.Next(minSplit, maxSplit + 1);
            var top = new BspLeaf(leaf.X, leaf.Y, leaf.Width, splitY - leaf.Y);
            var bottom = new BspLeaf(leaf.X, splitY, leaf.Width, leaf.Y + leaf.Height - splitY);
            return (top, bottom);
        }
        else
        {
            // 垂直分割: X軸で分ける
            int minSplit = leaf.X + MinLeafSize;
            int maxSplit = leaf.X + leaf.Width - MinLeafSize;
            if (minSplit > maxSplit) return (null, null);

            int splitX = rng.Next(minSplit, maxSplit + 1);
            var left = new BspLeaf(leaf.X, leaf.Y, splitX - leaf.X, leaf.Height);
            var right = new BspLeaf(splitX, leaf.Y, leaf.X + leaf.Width - splitX, leaf.Height);
            return (left, right);
        }
    }

    /// <summary>
    /// BSP leafの中にRoomを配置する。leafの境界から3タイルのマージンを確保する。
    /// </summary>
    private static Room? TryPlaceRoomInLeaf(BspLeaf leaf, GenerationConfig config, Random rng, int leafIndex)
    {
        // マージンを考慮した部屋配置可能領域
        int availableWidth = leaf.Width - LeafMargin * 2;
        int availableHeight = leaf.Height - LeafMargin * 2;

        if (availableWidth < config.MinRoomWidth || availableHeight < config.MinRoomHeight)
            return null;

        // 部屋サイズをランダムに決定
        int maxW = Math.Min(config.MaxRoomWidth, availableWidth);
        int maxH = Math.Min(config.MaxRoomHeight, availableHeight);
        int minW = Math.Min(config.MinRoomWidth, maxW);
        int minH = Math.Min(config.MinRoomHeight, maxH);

        if (minW <= 0 || minH <= 0)
            return null;

        int roomWidth = rng.Next(minW, maxW + 1);
        int roomHeight = rng.Next(minH, maxH + 1);

        // leaf内のランダム位置（マージン確保）
        int minX = leaf.X + LeafMargin;
        int maxX = leaf.X + leaf.Width - LeafMargin - roomWidth;
        int minY = leaf.Y + LeafMargin;
        int maxY = leaf.Y + leaf.Height - LeafMargin - roomHeight;

        if (minX > maxX || minY > maxY)
            return null;

        int roomX = rng.Next(minX, maxX + 1);
        int roomY = rng.Next(minY, maxY + 1);

        return new Room
        {
            X = roomX,
            Y = roomY,
            Width = roomWidth,
            Height = roomHeight,
            PartitionRow = leafIndex / 10, // BSPではgrid位置は意味を持たないが互換性のため設定
            PartitionCol = leafIndex % 10,
        };
    }

    #endregion

    #region Legacy Partition Methods

    private static Room? TryGenerateRoom(Partition partition, GenerationConfig config, Random rng)
    {
        const int Margin = 2;

        int maxW = Math.Min(config.MaxRoomWidth, partition.Width - Margin * 2);
        int maxH = Math.Min(config.MaxRoomHeight, partition.Height - Margin * 2);
        int minW = config.MinRoomWidth;
        int minH = config.MinRoomHeight;

        if (minW > maxW) minW = maxW;
        if (minH > maxH) minH = maxH;

        if (minW <= 0 || minH <= 0 || maxW <= 0 || maxH <= 0)
            return null;

        int roomWidth = rng.Next(minW, maxW + 1);
        int roomHeight = rng.Next(minH, maxH + 1);

        int minX = partition.X + Margin;
        int maxX = partition.X + partition.Width - roomWidth - Margin;
        int minY = partition.Y + Margin;
        int maxY = partition.Y + partition.Height - roomHeight - Margin;

        if (minX > maxX || minY > maxY)
            return null;

        int roomX = rng.Next(minX, maxX + 1);
        int roomY = rng.Next(minY, maxY + 1);

        return new Room
        {
            X = roomX,
            Y = roomY,
            Width = roomWidth,
            Height = roomHeight,
            PartitionRow = partition.Row,
            PartitionCol = partition.Col,
        };
    }

    #endregion

    /// <summary>
    /// RoomのセルをMapGridにFloorタイルとして書き込む。
    /// </summary>
    private static void WriteRoomToGrid(Room room, MapGrid grid)
    {
        for (int x = room.X; x < room.X + room.Width; x++)
        {
            for (int y = room.Y; y < room.Y + room.Height; y++)
            {
                if (grid.InBounds(x, y))
                {
                    grid[x, y] = TileType.Floor;
                }
            }
        }
    }

    /// <summary>
    /// BSP分割に使用する内部リーフ構造体。
    /// </summary>
    private sealed class BspLeaf(int x, int y, int width, int height)
    {
        public int X { get; } = x;
        public int Y { get; } = y;
        public int Width { get; } = width;
        public int Height { get; } = height;
    }
}
