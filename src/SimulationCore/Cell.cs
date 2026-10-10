namespace SimulationCore;

public enum CellState
{
    Alive,
    Dividing,
    Dying
}

/// <summary>
/// A single cell. Positions and radius are continuous world-space doubles.
/// Per-gene state (activation timers/flags) is stored inline so a cell is a
/// self-contained individual that shares its genome with the rest of the tissue.
/// </summary>
public sealed class Cell
{
    public int Id;
    public double X;
    public double Y;
    public double Radius;
    public int TypeId;
    public CellState State = CellState.Alive;

    public double Energy;
    public double Age;
    public double DivisionProgress;
    public double StarveTimer;
    public double DeathTimer;

    // Recomputed each step so genes can regulate on local density.
    public double NeighbourCount;

    // Per-gene regulation state (length == genome length of the cell type).
    public bool[] GeneActive = Array.Empty<bool>();
    public double[] GeneTimer = Array.Empty<double>();

    public void EnsureGeneState(int geneCount)
    {
        if (GeneActive.Length == geneCount) return;
        GeneActive = new bool[geneCount];
        GeneTimer = new double[geneCount];
    }
}
