namespace FF11Dungeon.MapGen;

public sealed class MapGrid
{
    private readonly TileType[,] _tiles;

    public int Width { get; }
    public int Height { get; }

    public MapGrid(int width, int height)
    {
        Width = width;
        Height = height;
        _tiles = new TileType[width, height];
        // 初期値: すべてWall (TileType.Wall = 0)
    }

    public TileType this[int x, int y]
    {
        get => _tiles[x, y];
        set => _tiles[x, y] = value;
    }

    /// <summary>
    /// 指定座標がグリッド内かを判定する。
    /// </summary>
    public bool InBounds(int x, int y)
        => x >= 0 && x < Width && y >= 0 && y < Height;

    /// <summary>
    /// 指定座標のタイルが通行可能（Floor, Corridor, RoomEntrance, StairsDown）かを判定する。
    /// </summary>
    public bool IsPassable(int x, int y)
        => InBounds(x, y) && this[x, y] != TileType.Wall;

    /// <summary>
    /// 境界外をWallとして扱い、タイルを取得する（Auto_Tile_Processor用）。
    /// </summary>
    public TileType GetTileOrWall(int x, int y)
        => InBounds(x, y) ? this[x, y] : TileType.Wall;
}
