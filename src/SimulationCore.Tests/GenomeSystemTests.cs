using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

[TestFixture]
public class GenomeSystemTests
{
    private sealed class Context : GeneContext
    {
        public double Sample;
        public double Emitted;
        public Rng Rng { get; } = new(42);
        public double Killed;
        public double SampleMorphogen(int morphogen, double x, double y) => Sample;
        public void EmitMorphogen(int morphogen, double x, double y, double amountPerSecond) => Emitted += amountPerSecond;
        public void Kill(Cell cell) => Killed++;
    }

    private static GenomeConfig GenomeOf(params GeneConfig[] genes) => new()
    {
        Id = 0,
        Name = "g0",
        Genes = new List<GeneConfig>(genes)
    };

    private static CellTypeConfig TypeWith() => new()
    {
        Id = 0,
        MaxRadius = 100.0,
        MaxEnergy = 100.0,
        GrowEnergyCostPerRadius = 1.0,
        GenomeId = 0
    };

    private static Cell MakeCell() => new() { Id = 0, Radius = 5, Energy = 100, TypeId = 0, GenomeId = 0 };

    [Test]
    public void Gene_IsInactive_DuringActivationDelay()
    {
        var ctx = new Context();
        var gene = TestFactory.Gene("g", TestFactory.Emit(0, 5));
        gene.ActivationDelay = 1.0;
        var genome = GenomeOf(gene);
        var type = TypeWith();
        var cell = MakeCell();

        GenomeSystem.Evaluate(cell, type, genome, ctx, 0.5);
        Assert.That(cell.StateOf("g").Active, Is.False);
        Assert.That(ctx.Emitted, Is.EqualTo(0.0).Within(1e-12));

        // Timer reaches the delay -> gene turns on and its effects run this step.
        GenomeSystem.Evaluate(cell, type, genome, ctx, 0.5);
        Assert.That(cell.StateOf("g").Active, Is.True);
        Assert.That(ctx.Emitted, Is.EqualTo(5.0).Within(1e-12));

        // While active the effect keeps running.
        GenomeSystem.Evaluate(cell, type, genome, ctx, 0.5);
        Assert.That(ctx.Emitted, Is.EqualTo(10.0).Within(1e-12), "Emission runs every step while active.");
    }

    [Test]
    public void MorphogenRegulator_GatesEmission()
    {
        var ctx = new Context { Sample = 0.0 };
        var gene = TestFactory.Gene("secretor", TestFactory.Emit(0, 10));
        gene.Regulators.Add(new RegulatorConfig { Kind = "Morphogen", Morphogen = 0, Min = 0.0, Max = 0.4 });
        var genome = GenomeOf(gene);
        var type = TypeWith();
        var cell = MakeCell();

        GenomeSystem.Evaluate(cell, type, genome, ctx, 0.1);
        Assert.That(ctx.Emitted, Is.GreaterThan(0.0));

        ctx.Emitted = 0.0;
        ctx.Sample = 10.0; // above threshold -> gene switches off
        GenomeSystem.Evaluate(cell, type, genome, ctx, 0.1);
        Assert.That(cell.StateOf("secretor").Active, Is.False);
        Assert.That(ctx.Emitted, Is.EqualTo(0.0));
    }

    [Test]
    public void AbsorbEffect_ConvertsConcentrationToEnergy()
    {
        var ctx = new Context { Sample = 2.0 };
        var type = TypeWith();
        var genome = GenomeOf(TestFactory.Gene("uptake", TestFactory.Absorb(0, 5)));
        var cell = new Cell { Id = 0, Radius = 5, Energy = 0, TypeId = 0, GenomeId = 0 };

        GenomeSystem.Evaluate(cell, type, genome, ctx, 1.0);
        Assert.That(cell.Energy, Is.EqualTo(10.0).Within(1e-9),
            "Energy += rate * concentration * dt = 5 * 2 * 1.");
    }

    [Test]
    public void AbsorbEffect_RespectsMaxEnergy()
    {
        var ctx = new Context { Sample = 1000.0 };
        var type = TypeWith();
        var genome = GenomeOf(TestFactory.Gene("uptake", TestFactory.Absorb(0, 100)));
        var cell = new Cell { Id = 0, Radius = 5, Energy = 0, TypeId = 0, GenomeId = 0 };

        GenomeSystem.Evaluate(cell, type, genome, ctx, 1.0);
        Assert.That(cell.Energy, Is.EqualTo(type.MaxEnergy).Within(1e-9));
    }

    [Test]
    public void GrowEffect_IncreasesRadius_AndSpendsEnergy()
    {
        var ctx = new Context();
        var type = TypeWith();
        var genome = GenomeOf(TestFactory.Gene("grow", TestFactory.Grow(4)));
        var cell = new Cell { Id = 0, Radius = 5, Energy = 100, TypeId = 0, GenomeId = 0 };

        GenomeSystem.Evaluate(cell, type, genome, ctx, 1.0);
        Assert.That(cell.Radius, Is.EqualTo(9.0).Within(1e-9));
        Assert.That(cell.Energy, Is.EqualTo(96.0).Within(1e-9), "Grow costs energy per unit radius.");
    }

    [Test]
    public void GrowEffect_DoesNotExceedMaxRadius()
    {
        var ctx = new Context();
        var type = TypeWith();
        var genome = GenomeOf(TestFactory.Gene("grow", TestFactory.Grow(100)));
        var cell = new Cell { Id = 0, Radius = 95, Energy = 100, TypeId = 0, GenomeId = 0 };

        GenomeSystem.Evaluate(cell, type, genome, ctx, 1.0);
        Assert.That(cell.Radius, Is.EqualTo(type.MaxRadius).Within(1e-9));
    }

    [Test]
    public void DivideEffect_AccumulatesProgress()
    {
        var ctx = new Context();
        var type = TypeWith();
        var genome = GenomeOf(TestFactory.Gene("div", TestFactory.Divide(0.5)));
        var cell = MakeCell();

        GenomeSystem.Evaluate(cell, type, genome, ctx, 1.0);
        Assert.That(cell.DivisionProgress, Is.EqualTo(0.5).Within(1e-9));
    }
}