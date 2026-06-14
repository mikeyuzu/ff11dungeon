namespace MapViewer.MapGen;

public sealed class RoomGenerationResult
{
    public IReadOnlyList<Room> Rooms { get; init; } = [];
    public IReadOnlyList<RoomMetadata> Metadata { get; init; } = [];
}
