namespace FF11Dungeon.MapGen;

public sealed class CorridorResult
{
    public IReadOnlyList<Corridor> Corridors { get; init; } = Array.Empty<Corridor>();
}
