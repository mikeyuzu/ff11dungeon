namespace MapViewer.MapGen;

/// <summary>
/// 壁タイルの描画バリアントを表す列挙型。
/// AutoTileProcessorが周囲8タイルのパターンに基づいて決定する。
/// </summary>
public enum WallVariant
{
    /// <summary>壁として描画不要（周囲が全てWall）</summary>
    None,

    /// <summary>直線壁</summary>
    Straight,

    /// <summary>内角（2つの隣接する基本方向が非Wall）</summary>
    InnerCorner,

    /// <summary>外角（斜め方向が非Wallだが隣接する基本方向は両方Wall）</summary>
    OuterCorner,

    /// <summary>行き止まり端（3方向以上が非Wall）</summary>
    End,
}
