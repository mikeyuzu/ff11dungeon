namespace FF11Dungeon.MapGen;

public sealed class PartitionGrid
{
    private readonly Partition[,] _partitions;

    public int Rows { get; }
    public int Columns { get; }

    public PartitionGrid(Partition[,] partitions)
    {
        Rows = partitions.GetLength(0);
        Columns = partitions.GetLength(1);
        _partitions = partitions;
    }

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
