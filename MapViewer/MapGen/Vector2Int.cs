namespace MapViewer.MapGen;

public readonly record struct Vector2Int(int X, int Y)
{
    public static Vector2Int operator +(Vector2Int a, Vector2Int b)
        => new(a.X + b.X, a.Y + b.Y);

    public static readonly Vector2Int Up = new(0, -1);
    public static readonly Vector2Int Down = new(0, 1);
    public static readonly Vector2Int Left = new(-1, 0);
    public static readonly Vector2Int Right = new(1, 0);

    public static readonly Vector2Int[] FourDirections = [Up, Down, Left, Right];
    public static readonly Vector2Int[] EightDirections =
    [
        Up, Down, Left, Right,
        new(-1, -1), new(1, -1), new(-1, 1), new(1, 1)
    ];
}
