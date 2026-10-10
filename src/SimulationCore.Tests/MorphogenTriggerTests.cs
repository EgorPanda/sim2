namespace SimulationCore.Tests;

using NUnit.Framework;

[TestFixture]
public class MorphogenTriggerTests
{
    private static CellTypeConfig Type() => new() { Id = 0, GenomeId = 0, MaxEnergy = 100 };

    private static GenomeConfig GenomeOf(params GeneConfig[] genes) => new() { Id = 0, Genes = genes.ToList() };

    private sealed class Context : GeneContext
    {
        public double ExternalConcentration = 0.0;
        public double Emitted = 0.0;
        public double InternalEmitted = 0.0;
        public HashSet<int> ExistingMorphogens = new() { 0 };
        public HashSet<int> InternalMorphogens = new();
        public Rng Rng { get; } = new(1);

        public double SampleMorphogen(int morphogen, double x, double y)
            => ExistingMorphogens.Contains(morphogen) ? ExternalConcentration : 0.0;
        public void EmitMorphogen(int morphogen, double x, double y, double amountPerSecond) => Emitted += amountPerSecond;
        public void Kill(Cell cell) { }

        public bool IsMorphogenInternal(int morphogen) => InternalMorphogens.Contains(morphogen);
        public double SampleInternalMorphogen(Cell cell, int morphogen)
            => cell.InternalMorphogenConcentrations.TryGetValue(morphogen, out double v) ? v : 0.0;
        public void EmitInternalMorphogen(Cell cell, int morphogen, double amountPerSecond, double dt)
            => InternalEmitted += amountPerSecond * dt;
    }

    [Test]
    public void NewGene_CreatedWithMandatoryTrigger()
    {
        var genome = new GenomeConfig { Id = 0 };
        var morphogens = new[]
        {
            new MorphogenConfig { Id = 0, Type = "External" },
            new MorphogenConfig { Id = 1, Type = "Internal" }
        };

        var gene = ConfigDraft.NewGene(genome, morphogens);

        Assert.That(gene.RequireMorphogenTrigger, Is.True, "new gene must have mandatory trigger enabled");
        Assert.That(gene.RequiredMorphogenId, Is.EqualTo(0), "first external morphogen should be preselected");
        Assert.That(gene.RequiredMorphogenThreshold, Is.GreaterThan(0.0), "threshold should be positive");
    }

    [Test]
    public void NewGene_WithoutMorphogens_IsNotConfigured()
    {
        var genome = new GenomeConfig { Id = 0 };
        var gene = ConfigDraft.NewGene(genome, Array.Empty<MorphogenConfig>());

        Assert.That(gene.RequireMorphogenTrigger, Is.True);
        Assert.That(gene.RequiredMorphogenId, Is.EqualTo(-1));
    }

    [Test]
    public void Trigger_MissingMorphogen_KeepsGeneInactive()
    {
        var gene = new GeneConfig
        {
            Id = "g",
            RequireMorphogenTrigger = true,
            RequiredMorphogenId = -1,
            Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0 } }
        };
        var cell = new Cell { TypeId = 0, GenomeId = 0 };
        var ctx = new Context();

        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), ctx, 0.5);

        Assert.That(cell.StateOf("g").Active, Is.False);
        Assert.That(ctx.Emitted, Is.EqualTo(0.0));
    }

    [Test]
    public void Trigger_ExternalBelowThreshold_KeepsGeneInactive()
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
        var ctx = new Context { ExternalConcentration = 0.2 };

        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), ctx, 0.5);

        Assert.That(cell.StateOf("g").Active, Is.False);
        Assert.That(ctx.Emitted, Is.EqualTo(0.0));
    }

    [Test]
    public void Trigger_ExternalAtThreshold_ActivatesGeneAndRunsEffects()
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
        var ctx = new Context { ExternalConcentration = 0.7 };

        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), ctx, 0.5);

        Assert.That(cell.StateOf("g").Active, Is.True);
        Assert.That(ctx.Emitted, Is.EqualTo(10.0));
    }

    [Test]
    public void Trigger_FallingBelowThreshold_StopsProduction()
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
        var ctx = new Context { ExternalConcentration = 0.7 };

        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), ctx, 0.5);
        Assert.That(cell.StateOf("g").Active, Is.True);

        ctx.ExternalConcentration = 0.1;
        ctx.Emitted = 0.0;
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), ctx, 0.5);

        Assert.That(cell.StateOf("g").Active, Is.False);
        Assert.That(ctx.Emitted, Is.EqualTo(0.0));
    }

    [Test]
    public void Trigger_InternalThreshold_ActivatesFromLocalConcentration()
    {
        var gene = new GeneConfig
        {
            Id = "g",
            RequireMorphogenTrigger = true,
            RequiredMorphogenId = 5,
            RequiredMorphogenThreshold = 1.0,
            Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0 } }
        };
        var cell = new Cell { TypeId = 0, GenomeId = 0 };
        cell.InternalMorphogenConcentrations[5] = 1.5;
        var ctx = new Context { InternalMorphogens = new HashSet<int> { 5 } };

        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), ctx, 0.5);

        Assert.That(cell.StateOf("g").Active, Is.True);
        Assert.That(ctx.Emitted, Is.EqualTo(10.0));
    }

    [Test]
    public void Trigger_WorksTogetherWithRegulatorGroups()
    {
        var gene = new GeneConfig
        {
            Id = "g",
            RequireMorphogenTrigger = true,
            RequiredMorphogenId = 0,
            RequiredMorphogenThreshold = 0.5,
            RegulatorGroups = new()
            {
                new() { Operator = "And", Regulators = new() { new() { Kind = "Property", Property = "energy", Min = 10.0 } } }
            },
            Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0 } }
        };
        var cell = new Cell { TypeId = 0, GenomeId = 0, Energy = 5.0 };
        var ctx = new Context { ExternalConcentration = 0.7 };

        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), ctx, 0.5);
        Assert.That(cell.StateOf("g").Active, Is.False, "additional regulator blocks activation");

        cell.Energy = 20.0;
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), ctx, 0.5);
        Assert.That(cell.StateOf("g").Active, Is.True, "trigger + regulator satisfied");
    }

    [Test]
    public void LegacyGene_WithoutTrigger_RemainsAlwaysOn()
    {
        var gene = new GeneConfig
        {
            Id = "g",
            RequireMorphogenTrigger = false,
            Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0 } }
        };
        var cell = new Cell { TypeId = 0, GenomeId = 0 };
        var ctx = new Context { ExternalConcentration = 0.0 };

        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), ctx, 0.5);

        Assert.That(cell.StateOf("g").Active, Is.True);
        Assert.That(ctx.Emitted, Is.EqualTo(10.0));
    }

    [Test]
    public void ExternalTrigger_SamplesFieldAtCellPosition()
    {
        var gene = new GeneConfig
        {
            Id = "g",
            RequireMorphogenTrigger = true,
            RequiredMorphogenId = 0,
            RequiredMorphogenThreshold = 0.5,
            Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0 } }
        };
        var cell = new Cell { TypeId = 0, GenomeId = 0, X = 10.0, Y = 20.0 };
        var ctx = new Context { ExternalConcentration = 0.7 };

        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), ctx, 0.5);

        Assert.That(cell.StateOf("g").Active, Is.True);
    }

    [Test]
    public void Trigger_LogsReason_WhenMorphogenMissing()
    {
        var gene = new GeneConfig
        {
            Id = "g",
            RequireMorphogenTrigger = true,
            RequiredMorphogenId = 99,
            RequiredMorphogenThreshold = 0.5,
            Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0 } }
        };
        var cell = new Cell { TypeId = 0, GenomeId = 0 };
        var ctx = new Context { ExternalConcentration = 1.0, ExistingMorphogens = new HashSet<int> { 0 } };

        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), ctx, 0.5);

        Assert.That(cell.StateOf("g").Active, Is.False);
    }
}
