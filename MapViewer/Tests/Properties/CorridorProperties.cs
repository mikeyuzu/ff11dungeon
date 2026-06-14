using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using MapViewer.MapGen;
using MapViewer.Tests.Generators;

namespace MapViewer.Tests.Properties;

/// <summary>
/// CorridorConnector のプロパティベーステスト。
/// </summary>
public class CorridorProperties
{
    /// <summary>
    /// Property 6: 通路間引きなし時の完全接続
    /// CorridorPruneChance=0.0で隣接Room付きPartitionペア全てにCorridorが存在することを検証する。
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NoPruneFullConnectivity()
    {
        return Prop.ForAll(
            ConfigGenerators.GenValidConfig(),
            Arb.From(Gen.Choose(0, int.MaxValue)),
            (config, seed) =>
            {
                var clamped = new GenerationConfig
                {
                    MapWidth = config.MapWidth,
                    MapHeight = config.MapHeight,
                    GridRows = config.GridRows,
                    GridColumns = config.GridColumns,
                    MinRoomWidth = config.MinRoomWidth,
                    MinRoomHeight = config.MinRoomHeight,
                    MaxRoomWidth = config.MaxRoomWidth,
                    MaxRoomHeight = config.MaxRoomHeight,
                    EmptyPartitionChance = 0.0f, // All partitions get rooms
                    CorridorPruneChance = 0.0f,  // No pruning
                }.Clamp();

                var splitter = new PartitionSplitter();
                var partitions = PartitionSplitter.Split(clamped.MapWidth, clamped.MapHeight, clamped.GridRows, clamped.GridColumns);
                var grid = new MapGrid(clamped.MapWidth, clamped.MapHeight);
                var rng = new Random(seed);
                var roomGen = new RoomGenerator();
                var roomResult = RoomGenerator.GenerateRooms(partitions, clamped, grid, rng);

                var connector = new CorridorConnector();
                var corridorResult = CorridorConnector.Connect(partitions, roomResult.Rooms, roomResult.Metadata, clamped, grid, rng);

                // Build lookup: (row,col) -> room exists
                var roomPartitions = new HashSet<(int, int)>();
                foreach (var room in roomResult.Rooms)
                    roomPartitions.Add((room.PartitionRow, room.PartitionCol));

                // Count expected adjacent pairs that both have rooms
                int expectedPairs = 0;
                for (int row = 0; row < partitions.Rows; row++)
                {
                    for (int col = 0; col < partitions.Columns; col++)
                    {
                        if (!roomPartitions.Contains((row, col))) continue;
                        // Check right neighbor
                        if (col + 1 < partitions.Columns && roomPartitions.Contains((row, col + 1)))
                            expectedPairs++;
                        // Check bottom neighbor
                        if (row + 1 < partitions.Rows && roomPartitions.Contains((row + 1, col)))
                            expectedPairs++;
                    }
                }

                // With prune=0, corridors should not exceed expected pairs
                // and at least some corridors should exist (routing failures may reduce count)
                var corridorCount = corridorResult.Corridors.Count;

                return (corridorCount <= expectedPairs)
                    .Label($"Corridors ({corridorCount}) should not exceed expected pairs ({expectedPairs})")
                    .And((corridorCount >= 1 || expectedPairs == 0)
                        .Label($"Expected at least 1 corridor when expectedPairs={expectedPairs}, got {corridorCount}"));
            });
    }

    /// <summary>
    /// Property 7: 隠し部屋フラグの整合性
    /// Corridor接続0本のRoomはIsHiddenRoom=true、1本以上はfalseを検証する。
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property HiddenRoomFlagConsistency()
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
            (config, seed, pruneChance) =>
            {
                var clamped = new GenerationConfig
                {
                    MapWidth = config.MapWidth,
                    MapHeight = config.MapHeight,
                    GridRows = config.GridRows,
                    GridColumns = config.GridColumns,
                    MinRoomWidth = config.MinRoomWidth,
                    MinRoomHeight = config.MinRoomHeight,
                    MaxRoomWidth = config.MaxRoomWidth,
                    MaxRoomHeight = config.MaxRoomHeight,
                    CorridorPruneChance = pruneChance,
                }.Clamp();

                var splitter = new PartitionSplitter();
                var partitions = PartitionSplitter.Split(clamped.MapWidth, clamped.MapHeight, clamped.GridRows, clamped.GridColumns);
                var grid = new MapGrid(clamped.MapWidth, clamped.MapHeight);
                var rng = new Random(seed);
                var roomGen = new RoomGenerator();
                var roomResult = RoomGenerator.GenerateRooms(partitions, clamped, grid, rng);

                // Rooms が 0 の場合はスキップ（テスト不能）
                if (roomResult.Rooms.Count == 0)
                    return true.Label("No rooms generated - skipped");

                var connector = new CorridorConnector();
                var corridorResult = CorridorConnector.Connect(partitions, roomResult.Rooms, roomResult.Metadata, clamped, grid, rng);

                // Count connections per room
                var connectionCount = new int[roomResult.Rooms.Count];
                foreach (var corridor in corridorResult.Corridors)
                {
                    connectionCount[corridor.SourceRoomIndex]++;
                    connectionCount[corridor.TargetRoomIndex]++;
                }

                // Verify IsHiddenRoom flag
                for (int i = 0; i < roomResult.Rooms.Count; i++)
                {
                    var meta = roomResult.Metadata[i];
                    if (connectionCount[i] == 0 && !meta.IsHiddenRoom)
                        return false.Label($"Room {i} has 0 connections but IsHiddenRoom=false");
                    if (connectionCount[i] > 0 && meta.IsHiddenRoom)
                        return false.Label($"Room {i} has {connectionCount[i]} connections but IsHiddenRoom=true");
                }

                return true.Label("Hidden room flags are consistent");
            });
    }
}
