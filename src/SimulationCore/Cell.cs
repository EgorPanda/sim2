namespace SimulationCore;

public enum CellState
{
    Alive,
    Dividing,
    Dying
}

/// <summary>Mutable per-gene expression state of a single cell.</summary>
public sealed class GeneState
{
    public bool Active;
    public double ActivationTimer;
}

/// <summary>
/// A single cell. Positions and radius are continuous world-space doubles.
/// Phenotype (<see cref="TypeId"/>) selects which phenotype parameters apply,
/// <see cref="GenomeId"/> selects which shared genome this cell expresses, and
/// per-gene state (activation/flags/timers) lives here so a cell is a
/// self-contained individual that shares its genome and phenotype data with the
/// rest of the tissue.
/// </summary>
public sealed class Cell
{
    public int Id;
    public double X;
    public double Y;
    public double Radius;
    public int TypeId;
    public int GenomeId;
    public CellState State = CellState.Alive;

    public double Energy;
    public double Age;
    public double DivisionProgress;
    public double StarveTimer;
    public double DeathTimer;

    /// <summary>Gene regulation state keyed by gene id. One entry per gene.</summary>
    public readonly Dictionary<string, GeneState> GeneStates = new();

    /// <summary>Genes silenced by a "SilenceGene" effect (never reactivate).</summary>
    public readonly HashSet<string> SilencedGenes = new();

    /// <summary>Named timers started by "StartTimer"; values count down to 0.</summary>
    public readonly Dictionary<string, double> Timers = new();

    /// <summary>Set by the "BlockDivision" effect; prevents division this step.</summary>
    public bool DivisionBlocked;

    /// <summary>
    /// Child phenotype the next division of this cell should produce, set by the
    /// "Divide" gene effect. Null means "inherit the parent's phenotype". This is
    /// a per-step intent: the last active Divide effect wins (deterministic
    /// genome order), and it is cleared after the division is resolved.
    /// </summary>
    public int? PendingChildTypeId;

    /// <summary>
    /// Gene id of the "Divide" effect that produced <see cref="PendingChildTypeId"/>,
    /// recorded for diagnostics and tests. Cleared together with the intent.
    /// </summary>
    public string? PendingDivideGene;

    // Recomputed each step so genes can regulate on local density.
    public double NeighbourCount;

    public GeneState StateOf(string geneId)
    {
        if (!GeneStates.TryGetValue(geneId, out var state))
        {
            state = new GeneState();
            GeneStates[geneId] = state;
        }
        return state;
    }

    public bool IsSilenced(string geneId) => SilencedGenes.Contains(geneId);

    public double TimerRemaining(string name) => Timers.TryGetValue(name, out double t) ? t : 0.0;

    /// <summary>Copies per-gene state, silenced set and timers onto another cell.</summary>
    public void CopyRegulationStateTo(Cell other)
    {
        other.GeneStates.Clear();
        foreach (var kv in GeneStates)
            other.GeneStates[kv.Key] = new GeneState { Active = kv.Value.Active, ActivationTimer = kv.Value.ActivationTimer };

        other.SilencedGenes.Clear();
        foreach (var id in SilencedGenes) other.SilencedGenes.Add(id);

        other.Timers.Clear();
        foreach (var kv in Timers) other.Timers[kv.Key] = kv.Value;
    }
}