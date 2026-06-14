namespace FF11Dungeon.MapGen;

/// <summary>
/// マップ領域を rows×cols のグリッドに等分割する。
/// 最小Partition幅/高さ(9)を満たせない場合、行数/列数を自動削減する。
/// 割り切れない余剰はグリッド端部のPartitionに加算する。
/// </summary>
public sealed class PartitionSplitter
{
    private const int MinPartitionSize = 9;

    /// <summary>
    /// マップ領域を rows×cols のグリッドに分割し、PartitionGrid を返す。
    /// </summary>
    /// <param name="mapWidth">マップ幅（タイル数）</param>
    /// <param name="mapHeight">マップ高さ（タイル数）</param>
    /// <param name="rows">要求グリッド行数</param>
    /// <param name="cols">要求グリッド列数</param>
    /// <returns>分割結果の PartitionGrid</returns>
    public PartitionGrid Split(int mapWidth, int mapHeight, int rows, int cols)
    {
        // 1. 最小Partition幅を満たせるまで cols を削減
        cols = ReduceToFit(mapWidth, cols);

        // 2. 最小Partition高さを満たせるまで rows を削減
        rows = ReduceToFit(mapHeight, rows);

        // 3. ベース寸法と余剰を計算
        int baseWidth = mapWidth / cols;
        int baseHeight = mapHeight / rows;
        int remainderWidth = mapWidth % cols;
        int remainderHeight = mapHeight % rows;

        // 4. Partition配列を構築
        var partitions = new Partition[rows, cols];

        int y = 0;
        for (int r = 0; r < rows; r++)
        {
            // 最終行に余剰高さを加算
            int partHeight = (r == rows - 1) ? baseHeight + remainderHeight : baseHeight;

            int x = 0;
            for (int c = 0; c < cols; c++)
            {
                // 最終列に余剰幅を加算
                int partWidth = (c == cols - 1) ? baseWidth + remainderWidth : baseWidth;

                partitions[r, c] = new Partition
                {
                    X = x,
                    Y = y,
                    Width = partWidth,
                    Height = partHeight,
                    Row = r,
                    Col = c
                };

                x += partWidth;
            }

            y += partHeight;
        }

        return new PartitionGrid(partitions);
    }

    /// <summary>
    /// マップ寸法を分割数で割ったとき、各Partitionが最小サイズを満たせるまで分割数を削減する。
    /// </summary>
    private static int ReduceToFit(int mapDimension, int divisions)
    {
        while (divisions > 1 && mapDimension / divisions < MinPartitionSize)
        {
            divisions--;
        }

        return divisions;
    }
}
