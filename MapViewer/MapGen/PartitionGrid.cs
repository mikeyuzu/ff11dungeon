namespace MapViewer.MapGen;

public sealed class PartitionGrid(Partition[,] partitions)
{
    private readonly Partition[,] _partitions = partitions;

    public int Rows { get; } = partitions.GetLength(0);
    public int Columns { get; } = partitions.GetLength(1);

    public Partition this[int row, int col] => _partitions[row, col];

    public IEnumerable<Partition> All
    {
        get
        {
            for (int r = 0; r < Rows; r++)
            {
                for (int c = 0; c < Columns; c++)
                {
                    yield return _partitions[r, c];
                }
            }
        }
    }
}
