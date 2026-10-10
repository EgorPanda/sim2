namespace SimulationCore;

/// <summary>
/// Uniform spatial hash over a 2D domain. Rebuilt cheaply each step from the
/// current cell positions; neighbour queries are O(k) instead of O(n).
/// </summary>
public sealed class SpatialHash
{
    private readonly double _cellSize;
    private readonly double _invCellSize;
    private readonly Dictionary<long, List<int>> _buckets = new();

    public SpatialHash(double cellSize)
    {
        _cellSize = cellSize > 0 ? cellSize : throw new ArgumentOutOfRangeException(nameof(cellSize));
        _invCellSize = 1.0 / _cellSize;
    }

    public double CellSize => _cellSize;

    private static long Key(int bx, int by) => ((long)bx << 32) ^ (uint)by;

    public void Clear() => _buckets.Clear();

    public void Rebuild(IReadOnlyList<Cell> cells)
    {
        Clear();
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            int bx = (int)Math.Floor(c.X * _invCellSize);
            int by = (int)Math.Floor(c.Y * _invCellSize);
            long key = Key(bx, by);
            if (!_buckets.TryGetValue(key, out var list))
            {
                list = new List<int>(4);
                _buckets[key] = list;
            }
            list.Add(i);
        }
    }

    /// <summary>
    /// Appends indices of all cells whose bucket overlaps the query circle.
    /// The caller must still test exact distances. Results are sorted ascending
    /// so processing order is deterministic.
    /// </summary>
    public void QueryCircle(double x, double y, double radius, List<int> results)
    {
        results.Clear();
        int minX = (int)Math.Floor((x - radius) * _invCellSize);
        int maxX = (int)Math.Floor((x + radius) * _invCellSize);
        int minY = (int)Math.Floor((y - radius) * _invCellSize);
        int maxY = (int)Math.Floor((y + radius) * _invCellSize);

        for (int by = minY; by <= maxY; by++)
        {
            for (int bx = minX; bx <= maxX; bx++)
            {
                if (_buckets.TryGetValue(Key(bx, by), out var list))
                    results.AddRange(list);
            }
        }

        if (results.Count > 1)
            results.Sort();
    }
}
