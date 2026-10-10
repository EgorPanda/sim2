namespace SimulationCore;

/// <summary>
/// Evaluates a cell's genome against the current world state and applies the
/// effects of active genes. Genes are data (see <see cref="GeneConfig"/>): the
/// number of genes, regulators and effects is not limited by code.
///
/// M1 additions: shared genomes referenced by phenotypes, per-gene state keyed
/// by stable gene id, negation/inverted regulators, phenotype/state/timer
/// regulators, once-per-activation effects, named timers, gene silencing,
/// cell-type switching, probabilistic (dt-invariant) apoptosis and
/// division blocking.
/// </summary>
public static class GenomeSystem
{
    /// <summary>
    /// Runs all genes of the cell's referenced genome for one step.
    /// Context provides the callbacks effects need (emission, killing, RNG).
    /// </summary>
    public static void Evaluate(Cell cell, CellTypeConfig type, GenomeConfig genome, GeneContext ctx, double dt)
    {
        cell.GenomeId = genome.Id;
        TickTimers(cell, dt);

        var genes = genome.Genes;
        for (int g = 0; g < genes.Count; g++)
        {
            var gene = genes[g];
            var state = cell.StateOf(gene.Key);

            // Silenced genes never activate and forget their delay progress.
            if (cell.IsSilenced(gene.Key))
            {
                state.Active = false;
                state.ActivationTimer = 0.0;
                continue;
            }

            bool conditionMet = RegulatorsMet(cell, gene, ctx);

            if (!conditionMet)
            {
                state.Active = false;
                state.ActivationTimer = 0.0;
                continue;
            }

            bool justActivated = false;
            if (!state.Active)
            {
                state.ActivationTimer += dt;
                if (state.ActivationTimer >= gene.ActivationDelay)
                {
                    state.Active = true;
                    justActivated = true;
                }
            }

            if (!state.Active) continue;

            for (int e = 0; e < gene.Effects.Count; e++)
            {
                var effect = gene.Effects[e];
                if (effect.Once && !justActivated) continue;
                ApplyEffect(cell, type, effect, ctx, dt);
            }
        }
    }

    /// <summary>
    /// Advances named timers by one step. Timers count down and are removed when
    /// they expire; a timer started this step is not decremented until next step.
    /// </summary>
    private static void TickTimers(Cell cell, double dt)
    {
        if (cell.Timers.Count == 0) return;
        foreach (var name in cell.Timers.Keys.ToArray())
        {
            double remaining = cell.Timers[name] - dt;
            if (remaining <= 0.0) cell.Timers.Remove(name);
            else cell.Timers[name] = remaining;
        }
    }

    private static bool RegulatorsMet(Cell cell, GeneConfig gene, GeneContext ctx)
    {
        for (int r = 0; r < gene.Regulators.Count; r++)
        {
            var reg = gene.Regulators[r];
            bool met = EvaluateRegulator(cell, reg, ctx);
            if (reg.Invert) met = !met;
            if (!met) return false;
        }
        return true;
    }

    private static bool EvaluateRegulator(Cell cell, RegulatorConfig reg, GeneContext ctx)
    {
        switch (reg.Kind)
        {
            case "Morphogen":
            {
                double value = ctx.SampleMorphogen(reg.Morphogen, cell.X, cell.Y);
                return InRange(value, reg.Min, reg.Max);
            }

            case "Property":
            {
                double value = Property(cell, reg.Property);
                return double.IsNaN(value) || InRange(value, reg.Min, reg.Max);
            }

            case "Phenotype":
                return cell.TypeId == reg.TypeId;

            case "State":
                return (int)cell.State == reg.TypeId;

            case "Timer":
                return cell.TimerRemaining(reg.Timer) > 0.0;

            default:
                return true;
        }
    }

    private static bool InRange(double value, double min, double max)
        => !double.IsNaN(value) && value >= min && value <= max;

    private static double Property(Cell cell, string name) => name switch
    {
        "energy" => cell.Energy,
        "radius" => cell.Radius,
        "age" => cell.Age,
        "neighbours" => cell.NeighbourCount,
        _ => double.NaN
    };

    private static void ApplyEffect(Cell cell, CellTypeConfig type, GeneEffectConfig effect, GeneContext ctx, double dt)
    {
        switch (effect.Kind)
        {
            case "EmitMorphogen":
                ctx.EmitMorphogen(effect.Morphogen, cell.X, cell.Y, effect.Rate);
                break;

            case "Absorb":
            {
                double conc = ctx.SampleMorphogen(effect.Morphogen, cell.X, cell.Y);
                cell.Energy = Num.Clamp(cell.Energy + effect.Rate * conc * dt, 0.0, type.MaxEnergy);
                break;
            }

            case "Grow":
            {
                if (cell.Radius < type.MaxRadius)
                {
                    double grow = effect.Rate * dt;
                    double newRadius = Math.Min(type.MaxRadius, cell.Radius + grow);
                    double actual = newRadius - cell.Radius;
                    cell.Radius = newRadius;
                    cell.Energy -= actual * type.GrowEnergyCostPerRadius;
                }
                break;
            }

            case "Divide":
                cell.DivisionProgress += effect.Rate * dt;
                break;

            case "SetCellType":
                cell.TypeId = effect.TypeId;
                break;

            case "StartTimer":
                if (string.IsNullOrEmpty(effect.Timer)) break;
                // Do not restart a timer that is already running.
                if (cell.TimerRemaining(effect.Timer) <= 0.0)
                    cell.Timers[effect.Timer] = Math.Max(0.0, effect.Duration);
                break;

            case "SilenceGene":
                if (!string.IsNullOrEmpty(effect.Gene))
                {
                    cell.SilencedGenes.Add(effect.Gene);
                    // Deactivate immediately so silencing applies within the same step
                    // regardless of genome order.
                    if (cell.GeneStates.TryGetValue(effect.Gene, out var silenced))
                    {
                        silenced.Active = false;
                        silenced.ActivationTimer = 0.0;
                    }
                }
                break;

            case "BlockDivision":
                cell.DivisionBlocked = true;
                break;

            case "Apoptosis":
            {
                // Deterministic when Probabililty <= 0; otherwise dt-invariant chance.
                if (effect.Probability <= 0.0)
                {
                    ctx.Kill(cell);
                }
                else
                {
                    double chance = 1.0 - Math.Exp(-effect.Probability * dt);
                    if (ctx.Rng.NextDouble() < chance) ctx.Kill(cell);
                }
                break;
            }
        }
    }
}

/// <summary>
/// Bridges the genome evaluator to the rest of the simulation without creating a
/// direct dependency, so genome logic stays testable in isolation.
/// </summary>
public interface GeneContext
{
    double SampleMorphogen(int morphogen, double x, double y);
    void EmitMorphogen(int morphogen, double x, double y, double amountPerSecond);
    void Kill(Cell cell);
    Rng Rng { get; }
}