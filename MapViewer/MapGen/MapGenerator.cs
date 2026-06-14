namespace MapViewer.MapGen;

/// <summary>
/// マップ生成のファサード。
/// Config検証→生成パイプライン実行→結果構築の全体フローを統合する。
/// BigRoomMode時は専用パスを使用し、通常モードではRegenerationLoopに委譲する。
/// </summary>
public sealed class MapGenerator
{
    /// <summary>
    /// 指定された設定に基づきマップを生成する。
    /// パイプライン: Config検証→グリッド初期化→区画分割→部屋生成→通路接続→入口マーキング→スポーン配置→接続性検証
    /// </summary>
    public static GenerationResult Generate(GenerationConfig config)
    {
        var clamped = config.Clamp();

        if (clamped.BigRoomMode)
        {
            return GenerateBigRoom(clamped);
        }

        _ = new RegenerationLoop();
        var result = RegenerationLoop.Execute(clamped);

        if (result.Success && clamped.MonsterHouseEnabled && result.Rooms != null)
        {
            var (_, rng) = SeedResolver.Resolve(result.UsedSeed + 1000);
            _ = new MonsterHouseAssigner();
            MonsterHouseAssigner.AssignMonsterHouses(result.Rooms, clamped, rng);
        }

        return result;
    }

    private static GenerationResult GenerateBigRoom(GenerationConfig config)
    {
        var (seed, rng) = SeedResolver.Resolve(config.Seed);
        _ = new BigRoomGenerator();
        var bigResult = BigRoomGenerator.Generate(config, rng);

        // Player spawn in big room (random floor tile, not stairs)
        Vector2Int playerSpawn;
        do
        {
            int px = rng.Next(bigResult.Room.X, bigResult.Room.X + bigResult.Room.Width);
            int py = rng.Next(bigResult.Room.Y, bigResult.Room.Y + bigResult.Room.Height);
            playerSpawn = new Vector2Int(px, py);
        } while (playerSpawn == bigResult.StairsPosition);

        // Monster spawns in big room
        var monsterSpawns = new List<Vector2Int>();
        for (int i = 0; i < config.InitialMonsterCount; i++)
        {
            int mx = rng.Next(bigResult.Room.X, bigResult.Room.X + bigResult.Room.Width);
            int my = rng.Next(bigResult.Room.Y, bigResult.Room.Y + bigResult.Room.Height);
            monsterSpawns.Add(new Vector2Int(mx, my));
        }

        return new GenerationResult
        {
            Success = true,
            Grid = bigResult.Grid,
            Rooms = new List<RoomMetadata> { bigResult.Metadata }.AsReadOnly(),
            UsedSeed = seed,
            PlayerSpawn = playerSpawn,
            StairsPosition = bigResult.StairsPosition,
            MonsterSpawns = monsterSpawns.AsReadOnly(),
        };
    }
}
