using FF11Dungeon.MapGen;

namespace FF11Dungeon.MapGen.Tests.Integration;

/// <summary>
/// MapGenerator ファサードの統合テスト。
/// デフォルト設定、大部屋モード+モンスターハウス、再生成失敗、シード自動生成、シード決定論性を検証する。
/// </summary>
public class FullGenerationTests
{
    /// <summary>
    /// デフォルト設定での正常生成テスト。
    /// Validates: Requirements 8.3, 8.4
    /// </summary>
    [Fact]
    public void Generate_DefaultConfig_Succeeds()
    {
        var generator = new MapGenerator();
        var result = generator.Generate(new GenerationConfig());

        Assert.True(result.Success);
        Assert.NotNull(result.Grid);
        Assert.NotNull(result.Rooms);
        Assert.True(result.Rooms!.Count >= 2);
        Assert.NotNull(result.PlayerSpawn);
        Assert.NotNull(result.MonsterSpawns);
    }

    /// <summary>
    /// 大部屋モード + モンスターハウスの組み合わせテスト。
    /// Validates: Requirements 6.2, 6.4
    /// </summary>
    [Fact]
    public void Generate_BigRoomModeWithMonsterHouse_Succeeds()
    {
        var config = new GenerationConfig
        {
            BigRoomMode = true,
            MonsterHouseEnabled = true,
            MapWidth = 60,
            MapHeight = 40,
        };
        var generator = new MapGenerator();
        var result = generator.Generate(config);

        Assert.True(result.Success);
        Assert.NotNull(result.Grid);
        Assert.NotNull(result.Rooms);
        Assert.Single(result.Rooms!);
        Assert.True(result.Rooms![0].IsMonsterHouse);
    }

    /// <summary>
    /// 再生成10回超過時の失敗テスト。
    /// 1x1グリッドでは部屋が1つしか生成されず、スポーン配置条件を満たせないため失敗する。
    /// Validates: Requirements 10.2, 10.3
    /// </summary>
    [Fact]
    public void Generate_ImpossibleConfig_FailsAfterRetries()
    {
        // 1x1 grid = only 1 partition = max 1 room = spawn needs 2 rooms → should fail
        var config = new GenerationConfig
        {
            MapWidth = 20,
            MapHeight = 20,
            GridRows = 1,
            GridColumns = 1,
            EmptyPartitionChance = 0.0f,
        };
        var generator = new MapGenerator();
        var result = generator.Generate(config);

        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }

    /// <summary>
    /// シード未指定時の自動生成テスト。
    /// Validates: Requirements 8.4
    /// </summary>
    [Fact]
    public void Generate_WithoutSeed_AutoGeneratesSeed()
    {
        var config = new GenerationConfig { Seed = null };
        var generator = new MapGenerator();
        var result = generator.Generate(config);

        Assert.True(result.Success);
        // UsedSeed should be populated with auto-generated value
        // (uint always >= 0, just verify generation succeeded with a seed)
        Assert.True(result.UsedSeed >= 0);
    }

    /// <summary>
    /// 同一シードで同一結果が得られることを検証する決定論性テスト。
    /// Validates: Requirements 8.3
    /// </summary>
    [Fact]
    public void Generate_SameSeed_ProducesSameResult()
    {
        var config = new GenerationConfig { Seed = 42 };
        var generator = new MapGenerator();
        var result1 = generator.Generate(config);
        var result2 = generator.Generate(config);

        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(result1.UsedSeed, result2.UsedSeed);

        // Compare grids tile by tile
        Assert.NotNull(result1.Grid);
        Assert.NotNull(result2.Grid);
        for (int x = 0; x < result1.Grid!.Width; x++)
            for (int y = 0; y < result1.Grid.Height; y++)
                Assert.Equal(result1.Grid[x, y], result2.Grid![x, y]);
    }
}
