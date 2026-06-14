namespace FF11Dungeon.MapGen;

public sealed class SpawnResult
{
    public bool Success { get; init; }
    public string? FailureReason { get; init; }
    public Vector2Int StairsPosition { get; init; }
    public Vector2Int PlayerSpawn { get; init; }
    public IReadOnlyList<Vector2Int>? MonsterSpawns { get; init; }
    public int StairsRoomIndex { get; init; }
    public int PlayerRoomIndex { get; init; }

    public static SpawnResult Failed(string reason) => new()
    {
        Success = false,
        FailureReason = reason,
    };
}
