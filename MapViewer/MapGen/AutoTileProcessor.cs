namespace FF11Dungeon.MapGen;

/// <summary>
/// 指定位置のWallタイルに対し、周囲8タイルを検査して壁バリアントを決定する。
/// マップ境界外は仮想Wallとして扱う（MapGrid.GetTileOrWallを使用）。
/// 同一の周囲パターンに対しては常に同一のバリアントを返す（決定論的）。
/// </summary>
public sealed class AutoTileProcessor
{
    /// <summary>
    /// 指定位置のWallタイルに対し、周囲8タイルの非Wallパターンに基づいて
    /// 壁バリアントを決定する。
    /// </summary>
    /// <param name="grid">マップグリッド</param>
    /// <param name="x">X座標</param>
    /// <param name="y">Y座標</param>
    /// <returns>決定されたWallVariant</returns>
    public WallVariant DetermineVariant(MapGrid grid, int x, int y)
    {
        // 対象タイルがWallでない場合はNoneを返す
        if (grid.GetTileOrWall(x, y) != TileType.Wall)
        {
            return WallVariant.None;
        }

        // 4基本方向の隣接タイルが非Wallかを判定
        bool northPassable = IsNonWall(grid, x, y - 1);
        bool southPassable = IsNonWall(grid, x, y + 1);
        bool eastPassable = IsNonWall(grid, x + 1, y);
        bool westPassable = IsNonWall(grid, x - 1, y);

        // 基本方向の非Wall数をカウント
        int passableCardinals = (northPassable ? 1 : 0)
                              + (southPassable ? 1 : 0)
                              + (eastPassable ? 1 : 0)
                              + (westPassable ? 1 : 0);

        switch (passableCardinals)
        {
            case 0:
                // 基本方向に非Wallがない場合、斜め方向を検査
                // 斜め方向が非Wallかつ隣接する両基本方向がWallの場合→OuterCorner
                if (HasExposedDiagonal(grid, x, y, northPassable, southPassable, eastPassable, westPassable))
                {
                    return WallVariant.OuterCorner;
                }
                return WallVariant.None;

            case 1:
                return WallVariant.Straight;

            case 2:
                // 対向する2方向（N+S または E+W）→ Straight（廊下壁）
                if ((northPassable && southPassable) || (eastPassable && westPassable))
                {
                    return WallVariant.Straight;
                }
                // 隣接する2方向（N+E, N+W, S+E, S+W）→ InnerCorner
                return WallVariant.InnerCorner;

            default:
                // 3方向以上が非Wall → End（突出した壁端）
                return WallVariant.End;
        }
    }

    /// <summary>
    /// 指定座標のタイルが非Wall（通行可能タイル）かを判定する。
    /// 境界外はWallとして扱われる。
    /// </summary>
    private static bool IsNonWall(MapGrid grid, int x, int y)
    {
        return grid.GetTileOrWall(x, y) != TileType.Wall;
    }

    /// <summary>
    /// 斜め方向のうち、隣接する両基本方向がWallであるにもかかわらず
    /// 非Wallとなっている斜めタイルが存在するかを検査する。
    /// この場合、外角（OuterCorner）として描画する必要がある。
    /// </summary>
    private static bool HasExposedDiagonal(
        MapGrid grid, int x, int y,
        bool northPassable, bool southPassable,
        bool eastPassable, bool westPassable)
    {
        // NE: NorthとEastが両方Wallの場合のみチェック
        if (!northPassable && !eastPassable && IsNonWall(grid, x + 1, y - 1))
            return true;

        // NW: NorthとWestが両方Wallの場合のみチェック
        if (!northPassable && !westPassable && IsNonWall(grid, x - 1, y - 1))
            return true;

        // SE: SouthとEastが両方Wallの場合のみチェック
        if (!southPassable && !eastPassable && IsNonWall(grid, x + 1, y + 1))
            return true;

        // SW: SouthとWestが両方Wallの場合のみチェック
        if (!southPassable && !westPassable && IsNonWall(grid, x - 1, y + 1))
            return true;

        return false;
    }
}
