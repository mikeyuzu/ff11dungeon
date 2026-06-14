namespace MapViewer.MapGen;

public readonly struct Corridor
{
    public IReadOnlyList<Vector2Int> Path { get; init; }
    public int SourceRoomIndex { get; init; }
    public int TargetRoomIndex { get; init; }
}
