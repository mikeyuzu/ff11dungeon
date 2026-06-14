namespace MapViewer.MapGen;

public sealed class GenerationResult
{
    public bool Success { get; init; }
    public string? FailureReason { get; init; }
    public MapGrid? Grid { get; init; }
    public IReadOnlyList<RoomMetadata>? Rooms { get; init; }
    public uint UsedSeed { get; init; }
    public Vector2Int? PlayerSpawn { get; init; }
    public Vector2Int? StairsPosition { get; init; }
    public IReadOnlyList<Vector2Int>? MonsterSpawns { get; init; }
}
