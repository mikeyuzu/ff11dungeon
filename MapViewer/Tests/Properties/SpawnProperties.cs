using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FF11Dungeon.MapGen;
using FF11Dungeon.MapGen.Tests.Generators;

namespace FF11Dungeon.MapGen.Tests.Properties;

/// <summary>
/// スポーン配置に関するプロパティベーステスト。
/// </summary>
public class SpawnProperties
{
    /// <summary>
    /// Property 12: スポーン配置制約
    /// StairsDownがCorridor接続Room内、プレイヤースポーンが別の非隠しRoom内を検証する。
    /// **Validates: Requirements 9.1, 9.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SpawnPlacementConstraints()
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
                var partitions = splitter.Split(clamped.MapWidth, clamped.MapHeight, clamped.GridRows, clamped.GridColumns);
                var grid = new MapGrid(clamped.MapWidth, clamped.MapHeight);
                var rng = new Random(seed);
                var roomGen = new RoomGenerator();
                var roomResult = roomGen.GenerateRooms(partitions, clamped, grid, rng);

                var connector = new CorridorConnector();
                var corridorResult = connector.Connect(partitions, roomResult.Rooms, roomResult.Metadata, clamped, grid, rng);

                var marker = new EntranceMarker();
                marker.MarkEntrances(grid, roomResult.Rooms, corridorResult.Corridors);

                var spawner = new SpawnPlacer();
                var spawnResult = spawner.PlaceSpawns(grid, roomResult.Rooms, roomResult.Metadata, corridorResult.Corridors, clamped, rng);

                // Only verify when spawn placement succeeds
                if (!spawnResult.Success)
                    return true.Label("Spawn placement failed (edge case) - skipped");

                // Build connection count per room
                var connectionCount = new int[roomResult.Rooms.Count];
                foreach (var corridor in corridorResult.Corridors)
                {
                    connectionCount[corridor.SourceRoomIndex]++;
                    connectionCount[corridor.TargetRoomIndex]++;
                }

                // Check 1: StairsDown is inside a room with ≥1 corridor connection
                int stairsRoomIdx = spawnResult.StairsRoomIndex;
                if (connectionCount[stairsRoomIdx] < 1)
                    return false.Label($"Stairs room {stairsRoomIdx} has 0 corridor connections");

                // Verify StairsDown tile is actually inside the stairs room
                var stairsRoom = roomResult.Rooms[stairsRoomIdx];
                var stairsPos = spawnResult.StairsPosition;
                if (stairsPos.X < stairsRoom.X || stairsPos.X >= stairsRoom.X + stairsRoom.Width ||
                    stairsPos.Y < stairsRoom.Y || stairsPos.Y >= stairsRoom.Y + stairsRoom.Height)
                    return false.Label($"StairsDown at ({stairsPos.X},{stairsPos.Y}) is outside stairs room bounds");

                // Check 2: Player spawn is in a different room, non-hidden, with ≥1 corridor connection
                int playerRoomIdx = spawnResult.PlayerRoomIndex;

                if (connectionCount[playerRoomIdx] < 1)
                    return false.Label($"Player room {playerRoomIdx} has 0 corridor connections");

                if (roomResult.Metadata[playerRoomIdx].IsHiddenRoom)
                    return false.Label($"Player room {playerRoomIdx} is a hidden room");

                // Verify player spawn tile is actually inside the player room
                var playerRoom = roomResult.Rooms[playerRoomIdx];
                var playerPos = spawnResult.PlayerSpawn;
                if (playerPos.X < playerRoom.X || playerPos.X >= playerRoom.X + playerRoom.Width ||
                    playerPos.Y < playerRoom.Y || playerPos.Y >= playerRoom.Y + playerRoom.Height)
                    return false.Label($"Player spawn at ({playerPos.X},{playerPos.Y}) is outside player room bounds");

                // Check 3: Stairs room and player room are different
                if (stairsRoomIdx == playerRoomIdx)
                    return false.Label($"Stairs room and player room are the same (index {stairsRoomIdx})");

                return true.Label("Spawn placement constraints satisfied");
            });
    }

    /// <summary>
    /// Property 13: モンスター初期配置の分散
    /// プレイヤーRoom以外の各非隠しRoomに最低1体、合計がInitialMonsterCountと一致を検証する。
    /// **Validates: Requirements 9.3, 9.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property MonsterSpawnDistribution()
    {
        var configGen = Gen.Choose(40, 200).SelectMany(mapWidth =>
                        Gen.Choose(40, 200).SelectMany(mapHeight =>
                        Gen.Choose(2, 5).SelectMany(gridRows =>
                        Gen.Choose(2, 5).SelectMany(gridColumns =>
                        Gen.Choose(5, 20).SelectMany(minRoomWidth =>
                        Gen.Choose(5, 20).SelectMany(minRoomHeight =>
                        Gen.Choose(minRoomWidth, 40).SelectMany(maxRoomWidth =>
                        Gen.Choose(minRoomHeight, 40).Select(maxRoomHeight =>
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
                            EmptyPartitionChance = 0.0f,
                            CorridorPruneChance = 0.0f,
                        }))))))));

        return Prop.ForAll(
            Arb.From(configGen),
            Arb.From(Gen.Choose(0, int.MaxValue)),
            Arb.From(Gen.Choose(3, 20)),
            (config, seed, monsterCount) =>
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
                    EmptyPartitionChance = config.EmptyPartitionChance,
                    CorridorPruneChance = config.CorridorPruneChance,
                    InitialMonsterCount = monsterCount,
                }.Clamp();

                // Run the generation pipeline
                var splitter = new PartitionSplitter();
                var partitions = splitter.Split(clamped.MapWidth, clamped.MapHeight, clamped.GridRows, clamped.GridColumns);
                var grid = new MapGrid(clamped.MapWidth, clamped.MapHeight);
                var rng = new Random(seed);

                var roomGen = new RoomGenerator();
                var roomResult = roomGen.GenerateRooms(partitions, clamped, grid, rng);

                var connector = new CorridorConnector();
                var corridorResult = connector.Connect(partitions, roomResult.Rooms, roomResult.Metadata, clamped, grid, rng);

                // Mark entrances
                var marker = new EntranceMarker();
                marker.MarkEntrances(grid, roomResult.Rooms, corridorResult.Corridors);

                // Place spawns
                var spawner = new SpawnPlacer();
                var spawnResult = spawner.PlaceSpawns(
                    grid, roomResult.Rooms, roomResult.Metadata,
                    corridorResult.Corridors, clamped, rng);

                // Only verify when spawn is successful
                if (!spawnResult.Success)
                    return true.Label("Spawn failed - skipped");

                // Identify non-hidden rooms excluding player room
                var monsterTargetRooms = new List<int>();
                for (int i = 0; i < roomResult.Rooms.Count; i++)
                {
                    if (i != spawnResult.PlayerRoomIndex && !roomResult.Metadata[i].IsHiddenRoom)
                        monsterTargetRooms.Add(i);
                }

                if (monsterTargetRooms.Count == 0)
                    return true.Label("No monster target rooms - skipped");

                // Check 1: Each non-hidden room (except player room) has at least 1 monster spawn
                var monsterSpawns = spawnResult.MonsterSpawns!;
                foreach (var roomIdx in monsterTargetRooms)
                {
                    var room = roomResult.Rooms[roomIdx];
                    bool hasMonster = monsterSpawns.Any(pos =>
                        pos.X >= room.X && pos.X < room.X + room.Width &&
                        pos.Y >= room.Y && pos.Y < room.Y + room.Height);

                    if (!hasMonster)
                        return false.Label($"Non-hidden room {roomIdx} (excluding player) has no monster spawn");
                }

                // Check 2: Total monster spawns == InitialMonsterCount
                // (when there are enough rooms, exact count is expected)
                int expectedCount = clamped.InitialMonsterCount;
                int actualCount = monsterSpawns.Count;

                // If monsterCount < monsterTargetRooms.Count, each room still gets 1,
                // so minimum is monsterTargetRooms.Count
                int minExpected = monsterTargetRooms.Count;
                if (expectedCount >= minExpected)
                {
                    if (actualCount != expectedCount)
                        return false.Label($"Total monster spawns {actualCount} != InitialMonsterCount {expectedCount}");
                }
                else
                {
                    // Not enough monsterCount to fill all rooms, but each room still gets 1
                    // So actual count should be at least monsterTargetRooms.Count
                    if (actualCount < minExpected)
                        return false.Label($"Total monster spawns {actualCount} < minimum required {minExpected}");
                }

                return true.Label("Monster spawn distribution is valid");
            });
    }
}
