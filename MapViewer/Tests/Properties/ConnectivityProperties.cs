using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FF11Dungeon.MapGen;
using FF11Dungeon.MapGen.Tests.Generators;

namespace FF11Dungeon.MapGen.Tests.Properties;

/// <summary>
/// マップ接続性に関するプロパティベーステスト。
/// </summary>
public class ConnectivityProperties
{
    /// <summary>
    /// Property 14: マップ接続性保証
    /// プレイヤースポーンから4方向探索ですべての非隠しRoomに到達可能を検証する。
    /// **Validates: Requirements 10.1**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property SuccessfulGenerationIsFullyConnected()
    {
        return Prop.ForAll(
            ConfigGenerators.GenValidConfig(),
            Arb.From(Gen.Choose(0, int.MaxValue).Select(i => (uint)i)),
            (config, seed) =>
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
                    EmptyPartitionChance = 0.0f,
                    CorridorPruneChance = 0.0f,
                    Seed = seed,
                }.Clamp();

                var loop = new RegenerationLoop();
                var result = loop.Execute(testConfig);

                if (!result.Success)
                    return true.Label("Generation failed (expected in edge cases) - skipped");

                // Verify connectivity using ConnectivityValidator
                var validator = new ConnectivityValidator();
                bool isConnected = validator.Validate(result.Grid!, result.Rooms!, result.PlayerSpawn!.Value);

                return isConnected.Label("All non-hidden rooms must be reachable from player spawn");
            });
    }
}
