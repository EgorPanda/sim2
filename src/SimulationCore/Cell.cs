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

    /// <summary>Internal morphogen concentrations keyed by morphogen id (cellular memory).</summary>
    public readonly Dictionary<int, double> InternalMorphogenConcentrations = new();

    /// <summary>Genes silenced by a "SilenceGene" effect (never reactivate).</summary>
    public readonly HashSet<string> SilencedGenes = new();

    /// <summary>
    /// Named timers started by "StartTimer"; values count down to 0. A finished
    /// timer stays at 0 (it is not removed) so <see cref="TimerElapsed"/> keeps
    /// reporting the full duration after expiry.
    /// </summary>
    public readonly Dictionary<string, double> Timers = new();

    /// <summary>
    /// Original duration of each named timer, set together with <see cref="Timers"/>.
    /// Used to derive the elapsed time <c>duration - remaining</c>. Missing for
    /// legacy saves, in which case elapsed is reported as 0.
    /// </summary>
    public readonly Dictionary<string, double> TimerTotals = new();

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

    /// <summary>
    /// Seconds elapsed since the named timer was last started. Returns 0 when the
    /// timer was never started (or for a legacy save without a recorded duration).
    /// After expiry the elapsed time saturates at the timer's full duration.
    /// </summary>
    public double TimerElapsed(string name)
    {
        if (!Timers.TryGetValue(name, out double remaining)) return 0.0;
        double total = TimerTotals.TryGetValue(name, out double t) ? t : 0.0;
        double elapsed = total - remaining;
        return elapsed > 0.0 ? elapsed : 0.0;
    }

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

        other.TimerTotals.Clear();
        foreach (var kv in TimerTotals) other.TimerTotals[kv.Key] = kv.Value;
    }

    /// <summary>Copies internal morphogen concentrations onto another cell (division inheritance).</summary>
    public void CopyInternalMorphogensTo(Cell other)
    {
        other.InternalMorphogenConcentrations.Clear();
        foreach (var kv in InternalMorphogenConcentrations)
            other.InternalMorphogenConcentrations[kv.Key] = kv.Value;
    }
}