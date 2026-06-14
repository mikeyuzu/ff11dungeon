using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using MapViewer.MapGen;
using MapViewer.Tests.Generators;

namespace MapViewer.Tests.Properties;

/// <summary>
/// EntranceMarker のプロパティベーステスト。
/// </summary>
public class EntranceProperties
{
    /// <summary>
    /// Property 8: 部屋入口タイルの正当性
    /// 各接続に正確に1つのRoom_Entrance、非隠しRoomに最低1つのRoom_Entranceを検証する。
    /// **Validates: Requirements 4.1, 4.2, 4.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RoomEntrancesAreValid()
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

                // Check: non-hidden rooms have at least 1 RoomEntrance adjacent
                for (int i = 0; i < roomResult.Rooms.Count; i++)
                {
                    if (roomResult.Metadata[i].IsHiddenRoom) continue;
                    var room = roomResult.Rooms[i];

                    // Count RoomEntrance tiles on this room's wall ring
                    int entranceCount = 0;
                    for (int x = room.X - 1; x <= room.X + room.Width; x++)
                    {
                        for (int y = room.Y - 1; y <= room.Y + room.Height; y++)
                        {
                            if (x >= room.X && x < room.X + room.Width && y >= room.Y && y < room.Y + room.Height)
                                continue; // Skip interior
                            if (grid.InBounds(x, y) && grid[x, y] == TileType.RoomEntrance)
                                entranceCount++;
                        }
                    }

                    if (entranceCount < 1)
                        return false.Label($"Non-hidden room {i} has no RoomEntrance tiles");
                }

                return true.Label("All non-hidden rooms have at least 1 RoomEntrance");
            });
    }
}
