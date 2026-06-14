namespace FF11Dungeon.MapGen;

public sealed class RoomGenerationResult
{
    public IReadOnlyList<Room> Rooms { get; init; } = Array.Empty<Room>();
    public IReadOnlyList<RoomMetadata> Metadata { get; init; } = Array.Empty<RoomMetadata>();
}
