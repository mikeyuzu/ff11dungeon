using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using MapViewer.MapGen;
using MapViewer.Tests.Generators;

namespace MapViewer.Tests.Properties;

/// <summary>
/// Property 11: シード決定論性
/// 同一Config・同一シードで2回生成した結果が完全一致することを検証する。
/// **Validates: Requirements 8.3**
/// </summary>
public class SeedProperties
{
    [Property(MaxTest = 100)]
    public Property SameSeedProducesSameRooms()
    {
        return Prop.ForAll(
            ConfigGenerators.GenValidConfig(),
            Arb.From(Gen.Choose(0, int.MaxValue).Select(i => (uint)i)),
            (config, seed) =>
            {
                // 同一シードからRNG を2つ生成
                var (_, rng1) = SeedResolver.Resolve(seed);
                var (_, rng2) = SeedResolver.Resolve(seed);

                // 区画分割（決定論的、RNG不使用）
                var splitter = new PartitionSplitter();
                var partitions = PartitionSplitter.Split(config.MapWidth, config.MapHeight, config.GridRows, config.GridColumns);

                // 同一パーティションから同一configで部屋生成を2回実行
                var roomGen = new RoomGenerator();
                var grid1 = new MapGrid(config.MapWidth, config.MapHeight);
                var grid2 = new MapGrid(config.MapWidth, config.MapHeight);

                var result1 = RoomGenerator.GenerateRooms(partitions, config, grid1, rng1);
                var result2 = RoomGenerator.GenerateRooms(partitions, config, grid2, rng2);

                // 部屋の数とその位置・サイズが完全一致することを検証
                var sameCount = result1.Rooms.Count == result2.Rooms.Count;
                var sameRooms = result1.Rooms.SequenceEqual(result2.Rooms);

                return sameCount.Label("Same room count")
                    .And(sameRooms.Label("Same room positions and sizes"));
            });
    }
}
