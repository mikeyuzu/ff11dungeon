using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using MapViewer.MapGen;
using MapViewer.Tests.Generators;

namespace MapViewer.Tests.Properties;

/// <summary>
/// PartitionSplitter のプロパティベーステスト。
/// </summary>
public class PartitionProperties
{
    /// <summary>
    /// Property 1: 区画分割によるマップ完全被覆
    /// すべてのPartitionの合計面積がwidth×heightと一致し、
    /// 重複なし・隙間なしを検証する。
    /// **Validates: Requirements 1.1, 1.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PartitionsCoverEntireMap()
    {
        return Prop.ForAll(
            ConfigGenerators.GenValidConfig(),
            config =>
            {
                var splitter = new PartitionSplitter();
                var grid = PartitionSplitter.Split(config.MapWidth, config.MapHeight, config.GridRows, config.GridColumns);

                var partitions = grid.All.ToList();

                // Assert 1: 合計面積 == マップ面積
                var totalArea = partitions.Sum(p => (long)p.Width * p.Height);
                var mapArea = (long)config.MapWidth * config.MapHeight;
                var areaMatches = totalArea == mapArea;

                // Assert 2: 重複なし（任意の2つのPartitionの領域が重複しない）
                var noOverlap = true;
                for (int i = 0; i < partitions.Count && noOverlap; i++)
                {
                    for (int j = i + 1; j < partitions.Count && noOverlap; j++)
                    {
                        if (Overlaps(partitions[i], partitions[j]))
                        {
                            noOverlap = false;
                        }
                    }
                }

                // Assert 3: 隙間なし（パーティションの和集合がマップ全域をカバー）
                var coversAll = CoversEntireMap(partitions, config.MapWidth, config.MapHeight);

                return areaMatches.Label("Total area must equal MapWidth * MapHeight")
                    .And(noOverlap.Label("No two partitions may overlap"))
                    .And(coversAll.Label("Partitions must cover entire map without gaps"));
            });
    }

    /// <summary>
    /// Property 2: 最小Partition寸法保証
    /// すべてのPartitionが幅9以上・高さ9以上であること、
    /// 要求を満たせない場合の自動削減を検証する。
    /// **Validates: Requirements 1.2, 1.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AllPartitionsMeetMinimumSize()
    {
        return Prop.ForAll(
            ConfigGenerators.GenValidConfig(),
            config =>
            {
                var splitter = new PartitionSplitter();
                var grid = PartitionSplitter.Split(config.MapWidth, config.MapHeight, config.GridRows, config.GridColumns);

                // Assert: ALL partitions have Width >= 9 AND Height >= 9
                foreach (var partition in grid.All)
                {
                    if (partition.Width < 9)
                        return false.Label($"Partition[{partition.Row},{partition.Col}] Width={partition.Width} < 9");
                    if (partition.Height < 9)
                        return false.Label($"Partition[{partition.Row},{partition.Col}] Height={partition.Height} < 9");
                }

                // Assert: If mapWidth / cols < 9, actual columns used should be less than requested cols
                if (config.MapWidth / config.GridColumns < 9)
                {
                    if (grid.Columns >= config.GridColumns)
                        return false.Label(
                            $"Columns not reduced: mapWidth={config.MapWidth}, requested cols={config.GridColumns}, actual cols={grid.Columns}");
                }

                // Assert: If mapHeight / rows < 9, actual rows used should be less than requested rows
                if (config.MapHeight / config.GridRows < 9)
                {
                    if (grid.Rows >= config.GridRows)
                        return false.Label(
                            $"Rows not reduced: mapHeight={config.MapHeight}, requested rows={config.GridRows}, actual rows={grid.Rows}");
                }

                return true.Label("All partitions meet minimum size constraints");
            });
    }

    /// <summary>
    /// 2つの Partition が重複するかを判定する。
    /// </summary>
    private static bool Overlaps(Partition a, Partition b)
    {
        bool xOverlap = a.X < b.X + b.Width && b.X < a.X + a.Width;
        bool yOverlap = a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
        return xOverlap && yOverlap;
    }

    /// <summary>
    /// パーティションの和集合がマップ全域をカバーするか検証する。
    /// </summary>
    private static bool CoversEntireMap(List<Partition> partitions, int mapWidth, int mapHeight)
    {
        var xEdges = new SortedSet<int> { 0, mapWidth };
        var yEdges = new SortedSet<int> { 0, mapHeight };

        foreach (var p in partitions)
        {
            xEdges.Add(p.X);
            xEdges.Add(p.X + p.Width);
            yEdges.Add(p.Y);
            yEdges.Add(p.Y + p.Height);
        }

        if (!xEdges.Contains(0) || !yEdges.Contains(0))
            return false;
        if (!xEdges.Contains(mapWidth) || !yEdges.Contains(mapHeight))
            return false;

        var xList = xEdges.ToList();
        var yList = yEdges.ToList();

        for (int xi = 0; xi < xList.Count - 1; xi++)
        {
            for (int yi = 0; yi < yList.Count - 1; yi++)
            {
                int cellX = xList[xi];
                int cellY = yList[yi];

                int count = partitions.Count(p =>
                    cellX >= p.X && cellX < p.X + p.Width &&
                    cellY >= p.Y && cellY < p.Y + p.Height);

                if (count != 1)
                    return false;
            }
        }

        return true;
    }
}
