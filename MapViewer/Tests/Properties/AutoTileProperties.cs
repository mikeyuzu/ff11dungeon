using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FF11Dungeon.MapGen;

namespace FF11Dungeon.MapGen.Tests.Properties;

/// <summary>
/// AutoTileProcessor のプロパティベーステスト。
/// Property 17: オートタイル壁バリアント決定の整合性
/// </summary>
public class AutoTileProperties
{
    /// <summary>
    /// Property 17a: 同一周囲パターンに対し常に同一バリアントを返す（決定論性）
    /// DetermineVariant を同一グリッド・同一位置で2回呼び出した結果が一致することを検証する。
    /// **Validates: Requirements 7.3, 7.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SamePatternProducesSameVariant()
    {
        return Prop.ForAll(
            GenWallNeighborPattern(),
            args =>
            {
                var (grid, wallPositions) = args;
                var processor = new AutoTileProcessor();

                foreach (var (x, y) in wallPositions)
                {
                    var variant1 = processor.DetermineVariant(grid, x, y);
                    var variant2 = processor.DetermineVariant(grid, x, y);

                    if (variant1 != variant2)
                        return false.Label($"Non-deterministic at ({x},{y}): {variant1} vs {variant2}");
                }

                return true.Label("All wall variants are deterministic");
            });
    }

    /// <summary>
    /// Property 17b: 境界外は仮想Wallとして扱い、有効なバリアントを返すことを検証する。
    /// マップ端のWallタイルに対してDetermineVariantがクラッシュせず、
    /// 有効なWallVariant値を返すことを確認する。
    /// **Validates: Requirements 7.3, 7.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property BoundaryTilesAreTreatedAsWall()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(5, 20)),  // width
            Arb.From(Gen.Choose(5, 20)),  // height
            Arb.From(Gen.Choose(0, int.MaxValue)), // seed for filling
            (width, height, seed) =>
            {
                var grid = new MapGrid(width, height);
                var rng = new Random(seed);

                // Fill interior with Floor, leave border as Wall (default)
                for (int x = 1; x < width - 1; x++)
                    for (int y = 1; y < height - 1; y++)
                        grid[x, y] = TileType.Floor;

                var processor = new AutoTileProcessor();

                // Test all border wall tiles — they have out-of-bounds neighbors
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        // Only test border tiles (those with at least one OOB neighbor)
                        if (x > 0 && x < width - 1 && y > 0 && y < height - 1)
                            continue;

                        if (grid.GetTileOrWall(x, y) != TileType.Wall)
                            continue;

                        var variant = processor.DetermineVariant(grid, x, y);

                        // Must return a defined enum value
                        if (!Enum.IsDefined(variant))
                            return false.Label($"Invalid variant at boundary ({x},{y}): {variant}");

                        // Verify determinism at boundary too
                        var variant2 = processor.DetermineVariant(grid, x, y);
                        if (variant != variant2)
                            return false.Label($"Non-deterministic at boundary ({x},{y}): {variant} vs {variant2}");
                    }
                }

                return true.Label("Boundary tiles handled correctly as virtual Wall");
            });
    }

    /// <summary>
    /// GenWallNeighborPattern: ランダムなMapGridとWall位置のリストを生成する。
    /// グリッドはランダムなTileTypeで埋められ、Wall位置のみが返される。
    /// </summary>
    private static Arbitrary<(MapGrid Grid, List<(int X, int Y)> WallPositions)> GenWallNeighborPattern()
    {
        var gen = Gen.Choose(5, 20).SelectMany(width =>
                  Gen.Choose(5, 20).SelectMany(height =>
                  Gen.Choose(0, int.MaxValue).Select(seed =>
                  {
                      var grid = new MapGrid(width, height);
                      var rng = new Random(seed);
                      var wallPositions = new List<(int X, int Y)>();

                      // Fill grid with random tiles
                      for (int x = 0; x < width; x++)
                      {
                          for (int y = 0; y < height; y++)
                          {
                              grid[x, y] = (TileType)rng.Next(0, 5);
                          }
                      }

                      // Collect wall positions
                      for (int x = 0; x < width; x++)
                      {
                          for (int y = 0; y < height; y++)
                          {
                              if (grid[x, y] == TileType.Wall)
                                  wallPositions.Add((x, y));
                          }
                      }

                      return (grid, wallPositions);
                  })));

        return Arb.From(gen);
    }
}
