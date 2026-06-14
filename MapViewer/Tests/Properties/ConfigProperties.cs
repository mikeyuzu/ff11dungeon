using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using MapViewer.Tests.Generators;

namespace MapViewer.Tests.Properties;

/// <summary>
/// Property 10: パラメータクランプの正当性
/// 範囲外の値が最も近い境界値にクランプされることを検証する。
/// **Validates: Requirements 8.1, 8.2**
/// </summary>
public class ConfigProperties
{
    [Property(MaxTest = 100)]
    public Property ClampedValuesAreWithinValidRanges()
    {
        return Prop.ForAll(
            ConfigGenerators.GenOutOfRangeConfig(),
            config =>
            {
                var clamped = config.Clamp();

                // マップ寸法: [20, 200]
                var mapWidthValid = clamped.MapWidth >= 20 && clamped.MapWidth <= 200;
                var mapHeightValid = clamped.MapHeight >= 20 && clamped.MapHeight <= 200;

                // グリッド分割: [1, 10]
                var gridRowsValid = clamped.GridRows >= 1 && clamped.GridRows <= 10;
                var gridColumnsValid = clamped.GridColumns >= 1 && clamped.GridColumns <= 10;

                // 部屋最小サイズ: [5, 50]
                var minRoomWidthValid = clamped.MinRoomWidth >= 5 && clamped.MinRoomWidth <= 50;
                var minRoomHeightValid = clamped.MinRoomHeight >= 5 && clamped.MinRoomHeight <= 50;

                // 部屋最大サイズ: >= MinRoomSize (after clamping)
                var maxRoomWidthValid = clamped.MaxRoomWidth >= clamped.MinRoomWidth;
                var maxRoomHeightValid = clamped.MaxRoomHeight >= clamped.MinRoomHeight;

                // 確率パラメータ: [0.0, 1.0]
                var emptyPartitionChanceValid = clamped.EmptyPartitionChance >= 0.0f && clamped.EmptyPartitionChance <= 1.0f;
                var corridorPruneChanceValid = clamped.CorridorPruneChance >= 0.0f && clamped.CorridorPruneChance <= 1.0f;
                var monsterHouseChanceValid = clamped.MonsterHouseChance >= 0.0f && clamped.MonsterHouseChance <= 1.0f;

                // スポーン: [1, 100]
                var initialMonsterCountValid = clamped.InitialMonsterCount >= 1 && clamped.InitialMonsterCount <= 100;

                return mapWidthValid
                    .Label("MapWidth in [20, 200]")
                    .And(mapHeightValid
                        .Label("MapHeight in [20, 200]"))
                    .And(gridRowsValid
                        .Label("GridRows in [1, 10]"))
                    .And(gridColumnsValid
                        .Label("GridColumns in [1, 10]"))
                    .And(minRoomWidthValid
                        .Label("MinRoomWidth in [5, 50]"))
                    .And(minRoomHeightValid
                        .Label("MinRoomHeight in [5, 50]"))
                    .And(maxRoomWidthValid
                        .Label("MaxRoomWidth >= MinRoomWidth"))
                    .And(maxRoomHeightValid
                        .Label("MaxRoomHeight >= MinRoomHeight"))
                    .And(emptyPartitionChanceValid
                        .Label("EmptyPartitionChance in [0.0, 1.0]"))
                    .And(corridorPruneChanceValid
                        .Label("CorridorPruneChance in [0.0, 1.0]"))
                    .And(monsterHouseChanceValid
                        .Label("MonsterHouseChance in [0.0, 1.0]"))
                    .And(initialMonsterCountValid
                        .Label("InitialMonsterCount in [1, 100]"));
            });
    }
}
