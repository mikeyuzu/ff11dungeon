using FsCheck;
using FsCheck.Fluent;
using MapViewer.MapGen;

namespace MapViewer.Tests.Generators;

/// <summary>
/// FsCheck カスタムジェネレーター: GenerationConfig 用
/// </summary>
public static class ConfigGenerators
{
    /// <summary>
    /// 有効範囲内の GenerationConfig を生成する。
    /// </summary>
    public static Arbitrary<GenerationConfig> GenValidConfig()
    {
        var gen = Gen.Choose(20, 200).SelectMany(mapWidth =>
                  Gen.Choose(20, 200).SelectMany(mapHeight =>
                  Gen.Choose(1, 10).SelectMany(gridRows =>
                  Gen.Choose(1, 10).SelectMany(gridColumns =>
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

        return Arb.From(gen);
    }

    /// <summary>
    /// 範囲外の値を含む GenerationConfig を生成する（クランプテスト用）。
    /// 各パラメータは有効範囲を超える可能性のある広い範囲から生成される。
    /// </summary>
    public static Arbitrary<GenerationConfig> GenOutOfRangeConfig()
    {
        var gen = Gen.Choose(-100, 500).SelectMany(mapWidth =>
                  Gen.Choose(-100, 500).SelectMany(mapHeight =>
                  Gen.Choose(-5, 20).SelectMany(gridRows =>
                  Gen.Choose(-5, 20).SelectMany(gridColumns =>
                  Gen.Choose(-10, 100).SelectMany(minRoomWidth =>
                  Gen.Choose(-10, 100).SelectMany(minRoomHeight =>
                  Gen.Choose(-10, 300).SelectMany(maxRoomWidth =>
                  Gen.Choose(-10, 300).SelectMany(maxRoomHeight =>
                  Gen.Choose(-100, 200).SelectMany(emptyPartitionChanceInt =>
                  Gen.Choose(-100, 200).SelectMany(corridorPruneChanceInt =>
                  Gen.Choose(-100, 200).SelectMany(monsterHouseChanceInt =>
                  Gen.Choose(-50, 200).Select(initialMonsterCount =>
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
                      EmptyPartitionChance = emptyPartitionChanceInt / 100.0f,
                      CorridorPruneChance = corridorPruneChanceInt / 100.0f,
                      MonsterHouseChance = monsterHouseChanceInt / 100.0f,
                      InitialMonsterCount = initialMonsterCount,
                  }))))))))))));

        return Arb.From(gen);
    }
}
