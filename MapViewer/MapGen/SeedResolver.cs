namespace FF11Dungeon.MapGen;

/// <summary>
/// シード値の解決と決定論的な Random インスタンスの生成を行うヘルパー。
/// </summary>
public static class SeedResolver
{
    /// <summary>
    /// シード値を解決し、決定論的な Random インスタンスを生成する。
    /// シード未指定時はランダムに生成する。
    /// </summary>
    /// <param name="configSeed">設定されたシード値（nullの場合は自動生成）</param>
    /// <returns>解決されたシード値と、それに基づく Random インスタンス</returns>
    public static (uint resolvedSeed, Random rng) Resolve(uint? configSeed)
    {
        uint seed = configSeed ?? (uint)Random.Shared.Next();
        var rng = new Random((int)seed);
        return (seed, rng);
    }
}
