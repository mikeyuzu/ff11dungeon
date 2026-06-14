namespace FF11Dungeon.MapGen;

public sealed class GenerationConfig
{
    // マップ寸法
    public int MapWidth { get; init; } = 60;        // 20～200
    public int MapHeight { get; init; } = 40;       // 20～200

    // グリッド分割
    public int GridRows { get; init; } = 3;         // 1～10
    public int GridColumns { get; init; } = 3;      // 1～10

    // 部屋サイズ
    public int MinRoomWidth { get; init; } = 5;     // 5～50
    public int MinRoomHeight { get; init; } = 5;    // 5～50
    public int MaxRoomWidth { get; init; } = 15;    // MinRoomWidth～(PartitionWidth - 2)
    public int MaxRoomHeight { get; init; } = 15;   // MinRoomHeight～(PartitionHeight - 2)

    // 確率パラメータ
    public float EmptyPartitionChance { get; init; } = 0.1f;   // 0.0～1.0
    public float CorridorPruneChance { get; init; } = 0.0f;    // 0.0～1.0
    public float MonsterHouseChance { get; init; } = 0.0f;     // 0.0～1.0

    // モード
    public bool BigRoomMode { get; init; } = false;
    public bool MonsterHouseEnabled { get; init; } = false;

    // シード
    public uint? Seed { get; init; } = null;        // null = 自動生成

    // スポーン
    public int InitialMonsterCount { get; init; } = 5;

    /// <summary>
    /// 全パラメータを有効範囲にクランプした新しい GenerationConfig を返す。
    /// MinRoomSize > MaxRoomSize の場合は MaxRoomSize を MinRoomSize に合わせる。
    /// </summary>
    public GenerationConfig Clamp()
    {
        // マップ寸法のクランプ
        var mapWidth = Math.Clamp(MapWidth, 20, 200);
        var mapHeight = Math.Clamp(MapHeight, 20, 200);

        // グリッド分割のクランプ
        var gridRows = Math.Clamp(GridRows, 1, 10);
        var gridColumns = Math.Clamp(GridColumns, 1, 10);

        // 部屋最小サイズのクランプ
        var minRoomWidth = Math.Clamp(MinRoomWidth, 5, 50);
        var minRoomHeight = Math.Clamp(MinRoomHeight, 5, 50);

        // 部屋最大サイズのクランプ（下限は対応するMinRoomSize）
        var maxRoomWidth = Math.Clamp(MaxRoomWidth, minRoomWidth, 200);
        var maxRoomHeight = Math.Clamp(MaxRoomHeight, minRoomHeight, 200);

        // MinRoomSize > MaxRoomSize の場合の修正
        if (maxRoomWidth < minRoomWidth)
            maxRoomWidth = minRoomWidth;
        if (maxRoomHeight < minRoomHeight)
            maxRoomHeight = minRoomHeight;

        // 確率パラメータのクランプ
        var emptyPartitionChance = Math.Clamp(EmptyPartitionChance, 0.0f, 1.0f);
        var corridorPruneChance = Math.Clamp(CorridorPruneChance, 0.0f, 1.0f);
        var monsterHouseChance = Math.Clamp(MonsterHouseChance, 0.0f, 1.0f);

        // スポーンのクランプ
        var initialMonsterCount = Math.Clamp(InitialMonsterCount, 1, 100);

        return new GenerationConfig
        {
            MapWidth = mapWidth,
            MapHeight = mapHeight,
            GridRows = gridRows,
            GridColumns = gridColumns,
            MinRoomWidth = minRoomWidth,
            MinRoomHeight = minRoomHeight,
            MaxRoomWidth = maxRoomWidth,
            MaxRoomHeight = maxRoomHeight,
            EmptyPartitionChance = emptyPartitionChance,
            CorridorPruneChance = corridorPruneChance,
            MonsterHouseChance = monsterHouseChance,
            BigRoomMode = BigRoomMode,
            MonsterHouseEnabled = MonsterHouseEnabled,
            Seed = Seed,
            InitialMonsterCount = initialMonsterCount,
        };
    }
}
