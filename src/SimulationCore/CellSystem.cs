namespace SimulationCore;

/// <summary>
/// Diagnostic record of a single division: who divided, what the daughter
/// became and which gene/rule produced the daughter phenotype. Kept only for
/// diagnostics and tests — it does not influence the simulation.
/// </summary>
public readonly record struct DivisionDiagnostic(
    long Sequence,
    int ParentId,
    int ParentTypeId,
    int ChildId,
    int ChildTypeId,
    int GenomeId,
    string? RuleGeneId,
    bool Inherited)
{
    public override string ToString()
        => $"#{Sequence} родитель {ParentId} ({ParentTypeId}) → потомок {ChildId} ({ChildTypeId})" +
           (Inherited ? " (наследование)" : $", правило «{RuleGeneId}»");
}

/// <summary>
/// Owns the cell population: metabolism, apoptosis and division. Division is
/// data-driven — a cell divides when its DivisionProgress reaches 1 (set by the
/// "Divide" gene effect) and it has enough energy and free space. A
/// "BlockDivision" gene effect suppresses division for the step.
/// </summary>
public sealed class CellSystem
{
    /// <summary>Keep only the most recent divisions so the log stays bounded.</summary>
    private const int MaxDivisionLog = 64;

    private readonly List<Cell> _cells;
    private readonly List<int> _queryScratch = new();
    private readonly List<DivisionDiagnostic> _divisionLog = new(MaxDivisionLog);
    private long _divisionSeq;

    public CellSystem(List<Cell> cells)
    {
        _cells = cells;
    }

    /// <summary>Recent divisions (most recent last). Read-only for consumers.</summary>
    public IReadOnlyList<DivisionDiagnostic> DivisionLog => _divisionLog;

    /// <summary>Applies maintenance cost and ages cells; marks starved cells for death.</summary>
    public void Metabolism(double dt, IReadOnlyDictionary<int, CellTypeConfig> types)
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            if (cell.State == CellState.Dying) continue;

            types.TryGetValue(cell.TypeId, out var type);
            double maintenance = type?.MaintenanceCost ?? 0.0;

            cell.Energy -= maintenance * dt;
            cell.Age += dt;

            if (cell.Energy <= 0.0)
            {
                cell.Energy = 0.0;
                cell.StarveTimer += dt;
                double grace = type?.StarveGracePeriod ?? 1.0;
                if (cell.StarveTimer >= grace)
                    Kill(cell);
            }
            else
            {
                cell.StarveTimer = 0.0;
            }
        }
    }

    public static void Kill(Cell cell)
    {
        if (cell.State == CellState.Dying) return;
        cell.State = CellState.Dying;
        cell.DeathTimer = 0.0;
    }

    /// <summary>Removes cells that finished dying. Returns number removed.</summary>
    public int Cleanup(double dt)
    {
        int removed = 0;
        for (int i = _cells.Count - 1; i >= 0; i--)
        {
            var cell = _cells[i];
            if (cell.State != CellState.Dying) continue;

            cell.DeathTimer += dt;
            if (cell.DeathTimer >= 0.6)
            {
                _cells.RemoveAt(i);
                removed++;
            }
        }
        return removed;
    }

    /// <summary>
    /// Attempts division for every cell whose division progress completed.
    /// Daughters inherit the parent's genome and regulation state, and split the
    /// parent's energy. The daughter phenotype comes from the "Divide" effect's
    /// <see cref="GeneEffectConfig.ChildTypeId"/> (<see cref="Cell.PendingChildTypeId"/>)
    /// when one was set during the last genome evaluation, otherwise it inherits
    /// the parent's phenotype. Division is blocked when the cell is too crowded,
    /// or when a "BlockDivision" gene effect is active. When a
    /// <paramref name="gradient"/> is supplied, the split axis is biased along
    /// the local morphogen gradient, so cells grow towards the signal.
    /// </summary>
    public int ResolveDivisions(
        SpatialHash hash,
        IReadOnlyDictionary<int, CellTypeConfig> types,
        Rng rng,
        Func<Cell> createCell,
        double maxQueryRadius,
        Func<double, double, (double Dx, double Dy)>? gradient = null)
    {
        int divisions = 0;
        int originalCount = _cells.Count;

        for (int i = 0; i < originalCount; i++)
        {
            var cell = _cells[i];
            if (cell.State != CellState.Alive) continue;
            if (cell.DivisionBlocked) continue;
            if (cell.DivisionProgress < 1.0) continue;
            if (!types.TryGetValue(cell.TypeId, out var type)) continue;
            if (cell.Energy < type.DivisionEnergyCost) continue;

            if (!FindFreeDirection(cell, type, hash, rng, maxQueryRadius, gradient, out double dx, out double dy))
                continue;

            int childTypeId = cell.PendingChildTypeId ?? cell.TypeId;
            if (!types.ContainsKey(childTypeId)) childTypeId = cell.TypeId;
            bool inherited = childTypeId == cell.TypeId;
            string? ruleGene = cell.PendingDivideGene;

            double cost = type.DivisionEnergyCost;
            double remaining = Math.Max(0.0, cell.Energy - cost);
            double half = remaining * 0.5;

            double radius = type.BaseRadius;

            // Parent moves back along the split axis, child goes forward.
            cell.X -= dx * radius;
            cell.Y -= dy * radius;
            cell.Radius = radius;
            cell.Energy = half;
            cell.DivisionProgress = 0.0;
            cell.Age = 0.0;
            cell.PendingChildTypeId = null;
            cell.PendingDivideGene = null;

            var child = createCell();
            child.TypeId = childTypeId;
            child.GenomeId = cell.GenomeId;
            child.X = cell.X + dx * radius * 2.0;
            child.Y = cell.Y + dy * radius * 2.0;
            child.Radius = radius;
            child.Energy = half;
            child.Age = 0.0;
            child.State = CellState.Alive;
            cell.CopyRegulationStateTo(child);
            _cells.Add(child);
            RecordDivision(cell, child, ruleGene, inherited);
            divisions++;
        }

        return divisions;
    }

    private void RecordDivision(Cell parent, Cell child, string? ruleGene, bool inherited)
    {
        _divisionLog.Add(new DivisionDiagnostic(
            ++_divisionSeq,
            parent.Id,
            parent.TypeId,
            child.Id,
            child.TypeId,
            child.GenomeId,
            ruleGene,
            inherited));
        if (_divisionLog.Count > MaxDivisionLog)
            _divisionLog.RemoveAt(0);
    }

    private bool FindFreeDirection(
        Cell cell, CellTypeConfig type, SpatialHash hash, Rng rng,
        double maxQueryRadius,
        Func<double, double, (double Dx, double Dy)>? gradient,
        out double dx, out double dy)
    {
        const int attempts = 16;

        // Random phase so orientation still depends on the seeded RNG.
        double phase = rng.Range(0.0, Math.PI * 2.0);

        // Bias the scan towards the morphogen gradient when one is available.
        double biasAngle = double.NaN;
        if (gradient != null)
        {
            var (gx, gy) = gradient(cell.X, cell.Y);
            double mag = Math.Sqrt(gx * gx + gy * gy);
            if (double.IsFinite(mag) && mag > 1e-9)
                biasAngle = Math.Atan2(gy, gx);
        }

        for (int a = 0; a < attempts; a++)
        {
            double angle = double.IsFinite(biasAngle)
                ? biasAngle + phase * 0.35 + a * (Math.PI * 2.0 / attempts)
                : phase + a * (Math.PI * 2.0 / attempts);
            double cdx = Math.Cos(angle);
            double cdy = Math.Sin(angle);

            double px = cell.X + cdx * type.BaseRadius * 2.0;
            double py = cell.Y + cdy * type.BaseRadius * 2.0;

            if (IsSpaceFree(cell.Id, px, py, type.BaseRadius, hash))
            {
                dx = cdx;
                dy = cdy;
                return true;
            }
        }

        dx = 0;
        dy = 0;
        return false;
    }

    private bool IsSpaceFree(int ignoreId, double x, double y, double radius, SpatialHash hash)
    {
        hash.QueryCircle(x, y, radius * 1.6 + 0.001, _queryScratch);
        for (int k = 0; k < _queryScratch.Count; k++)
        {
            var other = _cells[_queryScratch[k]];
            if (other.Id == ignoreId) continue;
            if (other.State == CellState.Dying) continue;

            double ddx = other.X - x;
            double ddy = other.Y - y;
            double minDist = radius + other.Radius;
            if (ddx * ddx + ddy * ddy < minDist * minDist)
                return false;
        }
        return true;
    }
}