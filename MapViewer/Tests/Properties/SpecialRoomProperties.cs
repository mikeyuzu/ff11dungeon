using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using MapViewer.MapGen;
using MapViewer.Tests.Generators;

namespace MapViewer.Tests.Properties;

/// <summary>
/// 特殊部屋（モンスターハウス等）に関するプロパティベーステスト。
/// </summary>
public class SpecialRoomProperties
{
    /// <summary>
    /// Property 15: モンスターハウス数の制約
    /// モンスターハウス数が1～3、密度乗数が3.0であることを検証する。
    /// **Validates: Requirements 6.1, 6.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property MonsterHouseCountConstraint()
    {
        return Prop.ForAll(
            ConfigGenerators.GenValidConfig(),
            Arb.From(Gen.Choose(0, int.MaxValue)),
            Arb.From(Gen.Choose(1, 100).Select(i => i / 100.0f)), // MonsterHouseChance 0.01-1.0
            (config, seed, mhChance) =>
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
                    EmptyPartitionChance = 0.0f,
                    CorridorPruneChance = 0.0f,
                    MonsterHouseEnabled = true,
                    MonsterHouseChance = mhChance,
                }.Clamp();

                var splitter = new PartitionSplitter();
                var partitions = PartitionSplitter.Split(clamped.MapWidth, clamped.MapHeight, clamped.GridRows, clamped.GridColumns);
                var grid = new MapGrid(clamped.MapWidth, clamped.MapHeight);
                var rng = new Random(seed);
                var roomGen = new RoomGenerator();
                var roomResult = RoomGenerator.GenerateRooms(partitions, clamped, grid, rng);

                // Generate corridors so IsHiddenRoom is set correctly
                var connector = new CorridorConnector();
                CorridorConnector.Connect(partitions, roomResult.Rooms, roomResult.Metadata, clamped, grid, rng);

                // Assign monster houses
                var assigner = new MonsterHouseAssigner();
                MonsterHouseAssigner.AssignMonsterHouses(roomResult.Metadata, clamped, rng);

                // If there are no non-hidden rooms (all rooms are isolated),
                // monster house assignment has no candidates - skip this case
                var nonHiddenRooms = roomResult.Metadata.Where(m => !m.IsHiddenRoom).ToList();
                if (nonHiddenRooms.Count == 0)
                    return true.Label("No non-hidden rooms available - skipped");

                // Count monster houses
                var monsterHouses = roomResult.Metadata.Where(m => m.IsMonsterHouse).ToList();
                int count = monsterHouses.Count;

                // Must be 1-3
                if (count < 1)
                    return false.Label($"Monster house count {count} < 1 (non-hidden rooms: {nonHiddenRooms.Count})");
                if (count > 3)
                    return false.Label($"Monster house count {count} > 3");

                // All monster houses have density multiplier 3.0
                foreach (var mh in monsterHouses)
                {
                    if (mh.ItemDensityMultiplier != 3.0f)
                        return false.Label($"ItemDensityMultiplier is {mh.ItemDensityMultiplier}, expected 3.0");
                    if (mh.MonsterDensityMultiplier != 3.0f)
                        return false.Label($"MonsterDensityMultiplier is {mh.MonsterDensityMultiplier}, expected 3.0");
                }

                return true.Label("Monster house count within [1,3] with density 3.0");
            });
    }
}
