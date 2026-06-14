namespace MapViewer.MapGen;

public readonly struct Room
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int PartitionRow { get; init; }
    public int PartitionCol { get; init; }
}
