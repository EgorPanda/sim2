namespace SimulationCore;

/// <summary>
/// Resolves overlaps between cells so they do not intersect and form a coherent
/// tissue. Uses the spatial hash for neighbourhoods and position-based
/// separation (each overlapping pair is pushed apart along their axis).
/// </summary>
public sealed class CellMechanics
{
    private readonly List<int> _scratch = new();

    /// <summary>Number of relaxation passes per step.</summary>
    public int Iterations { get; set; } = 2;

    /// <summary>How strongly overlaps are corrected per pass (1.0 = full separation).</summary>
    public double Stiffness { get; set; } = 0.5;

    public void Resolve(IReadOnlyList<Cell> cells, SpatialHash hash, double maxQueryRadius)
    {
        for (int iter = 0; iter < Iterations; iter++)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                var a = cells[i];
                if (a.State == CellState.Dying) continue;

                hash.QueryCircle(a.X, a.Y, a.Radius * 2.0 + maxQueryRadius, _scratch);
                for (int k = 0; k < _scratch.Count; k++)
                {
                    int j = _scratch[k];
                    if (j <= i) continue;
                    var b = cells[j];
                    if (b.State == CellState.Dying) continue;

                    double dx = b.X - a.X;
                    double dy = b.Y - a.Y;
                    double distSq = dx * dx + dy * dy;
                    double minDist = a.Radius + b.Radius;
                    if (distSq >= minDist * minDist) continue;

                    double dist = Math.Sqrt(distSq);
                    double overlap;
                    double nx;
                    double ny;

                    if (dist < 1e-6)
                    {
                        // Deterministic fallback direction for coincident centres.
                        double angle = (a.Id * 2654435761u % 62831853) / 10000000.0;
                        nx = Math.Cos(angle);
                        ny = Math.Sin(angle);
                        overlap = minDist;
                    }
                    else
                    {
                        nx = dx / dist;
                        ny = dy / dist;
                        overlap = minDist - dist;
                    }

                    double push = overlap * 0.5 * Stiffness;
                    double px = nx * push;
                    double py = ny * push;

                    a.X -= px;
                    a.Y -= py;
                    b.X += px;
                    b.Y += py;
                }
            }
        }
    }
}
