namespace SimulationCore.Tests;

using NUnit.Framework;

[TestFixture]
public class MorphogenProductionTests
{
    private static CellTypeConfig Type() => new() { Id = 0, GenomeId = 0, MaxEnergy = 100 };
    private static GenomeConfig GenomeOf(params GeneConfig[] genes) => new() { Id = 0, Genes = genes.ToList() };

    private sealed class Context : GeneContext
    {
        public double ExternalConcentration = 0.0;
        public double Emitted = 0.0;
        public double InternalEmitted = 0.0;
        public Rng Rng { get; } = new(1);

        public double SampleMorphogen(int morphogen, double x, double y) => ExternalConcentration;
        public void EmitMorphogen(int morphogen, double x, double y, double amountPerSecond) => Emitted += amountPerSecond;
        public void Kill(Cell cell) { }

        public bool IsMorphogenInternal(int morphogen) => false;
        public double SampleInternalMorphogen(Cell cell, int morphogen)
            => cell.InternalMorphogenConcentrations.TryGetValue(morphogen, out double v) ? v : 0.0;
        public void EmitInternalMorphogen(Cell cell, int morphogen, double amountPerSecond, double dt)
            => InternalEmitted += amountPerSecond * dt;
    }

    [Test]
    public void ActiveGene_EmitsEveryStep()
    {
        var gene = new GeneConfig
        {
            Id = "g",
            RequireMorphogenTrigger = false,
            Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0 } }
        };
        var cell = new Cell { TypeId = 0, GenomeId = 0 };
        var ctx = new Context();

        for (int i = 0; i < 5; i++)
            GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), ctx, 0.5);

        Assert.That(ctx.Emitted, Is.EqualTo(10.0 * 5));
    }

    [Test]
    public void OnceEffect_ExecutesOnlyOnce()
    {
        var gene = new GeneConfig
        {
            Id = "g",
            RequireMorphogenTrigger = false,
            Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0, Once = true } }
        };
        var cell = new Cell { TypeId = 0, GenomeId = 0 };
        var ctx = new Context();

        for (int i = 0; i < 5; i++)
            GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), ctx, 0.5);

        Assert.That(ctx.Emitted, Is.EqualTo(10.0));
    }

    [Test]
    public void Production_StopsAfterDeactivation()
    {
        var gene = new GeneConfig
        {
            Id = "g",
            RequireMorphogenTrigger = true,
            RequiredMorphogenId = 0,
            RequiredMorphogenThreshold = 0.5,
            Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0 } }
        };
        var cell = new Cell { TypeId = 0, GenomeId = 0 };
        var ctx = new Context { ExternalConcentration = 1.0 };

        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), ctx, 0.5);
        Assert.That(ctx.Emitted, Is.EqualTo(10.0));

        ctx.ExternalConcentration = 0.0;
        ctx.Emitted = 0.0;
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), ctx, 0.5);
        Assert.That(ctx.Emitted, Is.EqualTo(0.0));
    }

    [Test]
    public void Production_WithDecay_ReachesSteadyState()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[]
        {
            new MorphogenConfig { Id = 0, Type = "External", Diffusion = 0.0, Decay = 1.0 }
        };
        setup.Genomes = new[]
        {
            new GenomeConfig
            {
                Id = 0,
                Genes = new()
                {
                    new()
                    {
                        Id = "producer",
                        RequireMorphogenTrigger = false,
                        Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0 } }
                    }
                }
            }
        };
        setup.CellTypes = new[] { new CellTypeConfig { Id = 0, GenomeId = 0, BaseRadius = 6.0, MaxRadius = 6.0, MaxEnergy = 1000, MaintenanceCost = 0.0 } };

        var sim = new Simulation(setup);
        // Run until the total mass approaches the steady state (rate / decay = 10).
        for (int i = 0; i < 200; i++) sim.Step(0.05);

        double total = sim.Morphogens.TotalMass(0);
        Assert.That(total, Is.GreaterThan(5.0).And.LessThan(11.0));
    }

    [Test]
    public void Production_DtInvariant_OverSameModelTime()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[]
        {
            new MorphogenConfig { Id = 0, Type = "External", Diffusion = 0.0, Decay = 0.0 }
        };
        setup.Genomes = new[]
        {
            new GenomeConfig
            {
                Id = 0,
                Genes = new()
                {
                    new()
                    {
                        Id = "producer",
                        RequireMorphogenTrigger = false,
                        Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0 } }
                    }
                }
            }
        };
        setup.CellTypes = new[] { new CellTypeConfig { Id = 0, GenomeId = 0, BaseRadius = 6.0, MaxRadius = 6.0, MaxEnergy = 1000, MaintenanceCost = 0.0 } };

        var simA = new Simulation(setup);
        for (int i = 0; i < 100; i++) simA.Step(0.05); // 5 seconds

        var simB = new Simulation(setup);
        for (int i = 0; i < 50; i++) simB.Step(0.10); // 5 seconds

        double totalA = simA.Morphogens.TotalMass(0);
        double totalB = simB.Morphogens.TotalMass(0);
        Assert.That(totalA, Is.EqualTo(totalB).Within(1e-6));
        Assert.That(totalA, Is.EqualTo(50.0).Within(1e-6));
    }

    [Test]
    public void Concentrations_AreFiniteAndNonNegative()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[]
        {
            new MorphogenConfig { Id = 0, Type = "External", Diffusion = 0.0, Decay = 0.0 }
        };
        setup.Genomes = new[]
        {
            new GenomeConfig
            {
                Id = 0,
                Genes = new()
                {
                    new()
                    {
                        Id = "producer",
                        RequireMorphogenTrigger = false,
                        Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0 } }
                    }
                }
            }
        };
        setup.CellTypes = new[] { new CellTypeConfig { Id = 0, GenomeId = 0, BaseRadius = 6.0, MaxRadius = 6.0, MaxEnergy = 1000, MaintenanceCost = 0.0 } };

        var sim = new Simulation(setup);
        for (int i = 0; i < 50; i++) sim.Step(0.05);

        double total = sim.Morphogens.TotalMass(0);
        Assert.That(total, Is.GreaterThanOrEqualTo(0.0));
        Assert.That(double.IsFinite(total), Is.True);
    }

    [Test]
    public void Effect_DoesNotEmitTwicePerStep()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[]
        {
            new MorphogenConfig { Id = 0, Type = "External", Diffusion = 0.0, Decay = 0.0 }
        };
        setup.Genomes = new[]
        {
            new GenomeConfig
            {
                Id = 0,
                Genes = new()
                {
                    new()
                    {
                        Id = "producer",
                        RequireMorphogenTrigger = false,
                        Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0 } }
                    }
                }
            }
        };
        setup.CellTypes = new[] { new CellTypeConfig { Id = 0, GenomeId = 0, BaseRadius = 6.0, MaxRadius = 6.0, MaxEnergy = 1000, MaintenanceCost = 0.0 } };

        var sim = new Simulation(setup);
        sim.Step(1.0);

        double total = sim.Morphogens.TotalMass(0);
        Assert.That(total, Is.EqualTo(10.0).Within(1e-9));
    }
}
