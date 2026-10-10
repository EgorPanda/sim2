namespace SimulationCore;

/// <summary>
/// Evaluates a cell's genome against the current world state and applies the
/// effects of active genes. Genes are data (see <see cref="GeneConfig"/>): the
/// number of genes, regulators and effects is not limited by code.
/// </summary>
public static class GenomeSystem
{
    /// <summary>
    /// Runs all genes of the cell's type for one step.
    /// Context provides callbacks the effects need (emission + division signal).
    /// </summary>
    public static void Evaluate(Cell cell, CellTypeConfig type, GeneContext ctx, double dt)
    {
        cell.EnsureGeneState(type.Genome.Count);

        for (int g = 0; g < type.Genome.Count; g++)
        {
            var gene = type.Genome[g];
            bool conditionMet = RegulatorsMet(cell, gene, ctx);

            if (!conditionMet)
            {
                cell.GeneActive[g] = false;
                cell.GeneTimer[g] = 0.0;
                continue;
            }

            if (!cell.GeneActive[g])
            {
                cell.GeneTimer[g] += dt;
                if (cell.GeneTimer[g] >= gene.ActivationDelay)
                    cell.GeneActive[g] = true;
            }

            if (!cell.GeneActive[g]) continue;

            for (int e = 0; e < gene.Effects.Count; e++)
                ApplyEffect(cell, type, gene.Effects[e], ctx, dt);
        }
    }

    private static bool RegulatorsMet(Cell cell, GeneConfig gene, GeneContext ctx)
    {
        for (int r = 0; r < gene.Regulators.Count; r++)
        {
            var reg = gene.Regulators[r];
            double value = reg.Kind switch
            {
                "Morphogen" => ctx.SampleMorphogen(reg.Morphogen, cell.X, cell.Y),
                "Property" => Property(cell, reg.Property),
                _ => double.NaN
            };

            if (double.IsNaN(value)) continue;

            if (value < reg.Min || value > reg.Max)
                return false;
        }
        return true;
    }

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
}
