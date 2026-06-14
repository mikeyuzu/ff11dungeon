namespace MapViewer.MapGen;

public sealed class RoomMetadata
{
    public Room Room { get; init; }
    public bool IsHiddenRoom { get; set; }
    public bool IsMonsterHouse { get; set; }
    public float ItemDensityMultiplier { get; set; } = 1.0f;
    public float MonsterDensityMultiplier { get; set; } = 1.0f;
}
