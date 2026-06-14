using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using MapViewer.MapGen;
using MapViewer.Tests.Generators;

namespace MapViewer.Tests.Properties;

/// <summary>
/// RoomGenerator のプロパティベーステスト。
/// </summary>
public class RoomProperties
{
    /// <summary>
    /// Property 4: 最低部屋数保証
    /// 空き区画確率0.0～1.0のいずれでも最低2部屋が存在することを検証する。
    /// **Validates: Requirements 2.6, 2.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property MinimumTwoRoomsGuaranteed()
    {
        // GridRows * GridColumns >= 2 を保証するジェネレーター
        var configGen = Gen.Choose(20, 200).SelectMany(mapWidth =>
                        Gen.Choose(20, 200).SelectMany(mapHeight =>
                        Gen.Choose(1, 10).SelectMany(gridRows =>
                        Gen.Choose(1, 10).Where(gridColumns => gridRows * gridColumns >= 2).SelectMany(gridColumns =>
                        Gen.Choose(5, 50).SelectMany(minRoomWidth =>
                        Gen.Choose(5, 50).SelectMany(minRoomHeight =>
                        Gen.Choose(minRoomWidth, 100).SelectMany(maxRoomWidth =>
                        Gen.Choose(minRoomHeight, 100).Select(maxRoomHeight =>
                        new GenerationConfig
                        {
                            MapWidth = mapWidth,
                            MapHeight = mapHeight,
                            GridRows = gridRows,
                            GridColumns = gridColumns,
                            MinRoomWidth = minRoomWidth,
                            MinRoomHeight = minRoomHeight,
                            MaxRoomWidth = maxRoomWidth,
                            MaxRoomHeight = maxRoomHeight,
                        }))))))));

        return Prop.ForAll(
            Arb.From(configGen),
            Arb.From(Gen.Choose(0, int.MaxValue)),
            Arb.From(Gen.Choose(0, 100).Select(i => i / 100.0f)),
            (config, seed, emptyChance) =>
            {
                var testConfig = new GenerationConfig
                {
                    MapWidth = config.MapWidth,
                    MapHeight = config.MapHeight,
                    GridRows = config.GridRows,
                    GridColumns = config.GridColumns,
                    MinRoomWidth = config.MinRoomWidth,
                    MinRoomHeight = config.MinRoomHeight,
                    MaxRoomWidth = config.MaxRoomWidth,
                    MaxRoomHeight = config.MaxRoomHeight,
                    EmptyPartitionChance = emptyChance,
                }.Clamp();

                var splitter = new PartitionSplitter();
                var partitions = PartitionSplitter.Split(testConfig.MapWidth, testConfig.MapHeight, testConfig.GridRows, testConfig.GridColumns);
                var grid = new MapGrid(testConfig.MapWidth, testConfig.MapHeight);
                var rng = new Random(seed);
                var roomGen = new RoomGenerator();
                var result = RoomGenerator.GenerateRooms(partitions, testConfig, grid, rng);

                return (result.Rooms.Count >= 2)
                    .Label($"Expected >= 2 rooms, got {result.Rooms.Count} with EmptyPartitionChance={emptyChance}");
            });
    }
}
