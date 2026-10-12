namespace SimulationCore;

/// <summary>
/// Resolves overlaps between cells so they do not intersect and form a coherent
/// tissue. Uses the spatial hash for neighbourhoods and position-based
/// separation (each overlapping pair is pushed apart along their axis).
///
/// M1: an optional per-phenotype-pair adhesion map lets cohesive pairs relax to
/// a smaller rest distance (partial overlap), producing distinct tissues. The
/// correction is purely positional, so it stays stable (no oscillation).
/// </summary>
public sealed class CellMechanics
{
    private readonly List<int> _scratch = new();

    /// <summary>Number of relaxation passes per step.</summary>
    public int Iterations { get; set; } = 2;

    /// <summary>How strongly overlaps are corrected per pass (1.0 = full separation).</summary>
    public double Stiffness { get; set; } = 0.5;

    /// <summary>Maximum allowed overlap fraction (of the touching distance) at adhesion 1.</summary>
    public double MaxOverlap { get; set; } = 0.4;

    /// <summary>Adhesion strength in [0,1] for a phenotype pair; null means no adhesion.</summary>
    public Func<int, int, double>? Adhesion { get; set; }

    public void Resolve(IReadOnlyList<Cell> cells, SpatialHash hash, double maxQueryRadius)
    {
        for (int iter = 0; iter < Iterations; iter++)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                var a = cells[i];
                if (a.State == CellState.Dying) continue;

                // Candidates that could possibly overlap: their centre must be
                // within a.Radius + b.Radius <= a.Radius + maxQueryRadius, so the
                // query radius a.Radius + maxQueryRadius is exact (the old
                // a.Radius * 2 + maxQueryRadius over-scanned by a.Radius).
                hash.QueryCircle(a.X, a.Y, a.Radius + maxQueryRadius + 0.001, _scratch);
                for (int k = 0; k < _scratch.Count; k++)
                {
                    int j = _scratch[k];
                    if (j <= i) continue;
                    var b = cells[j];
                    if (b.State == CellState.Dying) continue;

                    double dx = b.X - a.X;
                    double dy = b.Y - a.Y;
                    double distSq = dx * dx + dy * dy;
                    double touch = a.Radius + b.Radius;

                    // Adhesion relaxes the required separation for cohesive pairs.
                    double adhesion = Adhesion?.Invoke(a.TypeId, b.TypeId) ?? 0.0;
                    if (adhesion < 0.0) adhesion = 0.0;
                    if (adhesion > 1.0) adhesion = 1.0;
                    double minDist = touch * (1.0 - MaxOverlap * adhesion);

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