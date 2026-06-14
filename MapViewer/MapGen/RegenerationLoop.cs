namespace FF11Dungeon.MapGen;

/// <summary>
/// 生成パイプラインを実行し、接続性検証またはスポーン配置に失敗した場合は
/// 新しいランダムステートで再生成する（最大10回）。
/// 通常モードではBSP + MSTによるグラフベース生成を使用する。
/// </summary>
public sealed class RegenerationLoop
{
    private const int MaxRetries = 10;

    /// <summary>
    /// マップ生成を実行する。接続性検証またはスポーン配置に失敗した場合は
    /// 新しいランダムステートで再生成を試行し、最大10回を超過した場合は
    /// GenerationResult.Success=false を返す。
    /// </summary>
    public GenerationResult Execute(GenerationConfig config)
    {
        var clamped = config.Clamp();
        var (baseSeed, _) = SeedResolver.Resolve(clamped.Seed);

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            // Each retry uses a different seed derived from the base
            uint currentSeed = (uint)(baseSeed + attempt);
            var rng = new Random((int)currentSeed);

            // 1. Grid init (all Wall)
            var grid = new MapGrid(clamped.MapWidth, clamped.MapHeight);

            // 2. Room generation via BSP
            var roomGen = new RoomGenerator();
            var roomResult = roomGen.GenerateRoomsBSP(clamped, grid, rng);

            // 3. Corridor connection via MST + extra edges
            var connector = new CorridorConnector();
            var corridorResult = connector.Connect(roomResult.Rooms, roomResult.Metadata, clamped, grid, rng);

            // 4. Entrance marking
            var marker = new EntranceMarker();
            marker.MarkEntrances(grid, roomResult.Rooms, corridorResult.Corridors);

            // 5. Spawn placement
            var spawner = new SpawnPlacer();
            var spawnResult = spawner.PlaceSpawns(grid, roomResult.Rooms, roomResult.Metadata, corridorResult.Corridors, clamped, rng);

            if (!spawnResult.Success)
                continue; // Retry with new seed

            // 6. Connectivity validation
            var validator = new ConnectivityValidator();
            bool connected = validator.Validate(grid, roomResult.Metadata, spawnResult.PlayerSpawn);

            if (!connected)
                continue; // Retry with new seed

            // Success
            return new GenerationResult
            {
                Success = true,
                Grid = grid,
                Rooms = roomResult.Metadata,
                UsedSeed = currentSeed,
                PlayerSpawn = spawnResult.PlayerSpawn,
                StairsPosition = spawnResult.StairsPosition,
                MonsterSpawns = spawnResult.MonsterSpawns,
            };
        }

        // All retries exhausted
        return new GenerationResult
        {
            Success = false,
            FailureReason = $"Generation failed after {MaxRetries + 1} attempts",
            UsedSeed = baseSeed,
        };
    }
}
