using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using MapViewer.MapGen;
using MapViewer.Tests.Generators;

namespace MapViewer.Tests.Properties;

/// <summary>
/// タイルタイプ整合性のプロパティベーステスト。
/// Room内セルがFloor/RoomEntrance/StairsDown、通路セルがCorridor、グリッド寸法一致を検証する。
/// </summary>
public class TileIntegrityProperties
{
    /// <summary>
    /// Property 9: タイルタイプ整合性
    /// Room内セルがFloor/Room_Entrance/StairsDown、通路セルがCorridor、グリッド寸法一致を検証する。
    /// **Validates: Requirements 5.1, 5.3, 5.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TileTypesAreConsistent()
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
                    EmptyPartitionChance = 0.0f,
                    CorridorPruneChance = 0.0f,
                }.Clamp();

                var splitter = new PartitionSplitter();
                var partitions = PartitionSplitter.Split(clamped.MapWidth, clamped.MapHeight, clamped.GridRows, clamped.GridColumns);
                var grid = new MapGrid(clamped.MapWidth, clamped.MapHeight);
                var rng = new Random(seed);
                var roomGen = new RoomGenerator();
                var roomResult = RoomGenerator.GenerateRooms(partitions, clamped, grid, rng);

                var connector = new CorridorConnector();
                var corridorResult = CorridorConnector.Connect(partitions, roomResult.Rooms, roomResult.Metadata, clamped, grid, rng);

                var marker = new EntranceMarker();
                EntranceMarker.MarkEntrances(grid, roomResult.Rooms, corridorResult.Corridors);

                // Check 1: Grid dimensions match config
                if (grid.Width != clamped.MapWidth || grid.Height != clamped.MapHeight)
                    return false.Label("Grid dimensions don't match config");

                // Check 2: Room interior cells are Floor, RoomEntrance, or StairsDown
                foreach (var room in roomResult.Rooms)
                {
                    for (int x = room.X; x < room.X + room.Width; x++)
                    {
                        for (int y = room.Y; y < room.Y + room.Height; y++)
                        {
                            var tile = grid[x, y];
                            if (tile != TileType.Floor && tile != TileType.RoomEntrance && tile != TileType.StairsDown)
                                return false.Label($"Room cell ({x},{y}) has invalid tile type {tile}");
                        }
                    }
                }

                // Check 3: Corridor tiles are Corridor type (not inside rooms)
                foreach (var corridor in corridorResult.Corridors)
                {
                    foreach (var point in corridor.Path)
                    {
                        if (!grid.InBounds(point.X, point.Y)) continue;
                        var tile = grid[point.X, point.Y];
                        // Corridor path points should be Corridor or RoomEntrance (at entrance) or Floor (inside room)
                        if (tile != TileType.Corridor && tile != TileType.RoomEntrance && tile != TileType.Floor)
                            return false.Label($"Corridor point ({point.X},{point.Y}) has unexpected tile {tile}");
                    }
                }

                return true.Label("Tile types are consistent");
            });
    }
}
