using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

[TestFixture]
public class RegulatorGroupTests
{
    private sealed class Context : GeneContext
    {
        public double Emitted;
        public Rng Rng { get; } = new(42);
        public void EmitMorphogen(int morphogen, double x, double y, double amountPerSecond) => Emitted += amountPerSecond;
        public double SampleMorphogen(int morphogen, double x, double y) => 0.0;
        public void Kill(Cell cell) => cell.State = CellState.Dying;
    }

    private static GenomeConfig GenomeOf(params GeneConfig[] genes)
        => new() { Id = 0, Genes = new List<GeneConfig>(genes) };

    private static CellTypeConfig Type() => new()
    {
        Id = 0, MaxRadius = 30, GenomeId = 0
    };

    private static Cell Cell0(double age = 100, double radius = 5, int type = 0)
        => new() { Id = 0, Radius = radius, Age = age, TypeId = type, GenomeId = 0 };

    private static RegulatorConfig AgeAtLeast(double v)
        => new() { Kind = "Property", Property = "age", Min = v, Max = double.PositiveInfinity };

    private static RegulatorConfig RadiusAtLeast(double v)
        => new() { Kind = "Property", Property = "radius", Min = v, Max = double.PositiveInfinity };

    // --- OR across groups ---------------------------------------------------

    [Test]
    public void Groups_AreOrCombined()
    {
        var gene = new GeneConfig
        {
            Id = "g",
            RegulatorGroups = new List<RegulatorGroupConfig>
            {
                new() { Operator = "And", Regulators = { AgeAtLeast(1000) } }, // false
                new() { Operator = "And", Regulators = { RadiusAtLeast(10) } },   // true
            },
            Effects = { TestFactory.Emit(0, 1) }
        };
        var cell = Cell0(age: 50, radius: 12);
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), new Context(), 0.5);
        Assert.That(cell.StateOf("g").Active, Is.True, "One satisfied group (OR) must activate the gene.");
    }

    [Test]
    public void Groups_AllFalse_GeneInactive()
    {
        var gene = new GeneConfig
        {
            Id = "g",
            RegulatorGroups = new List<RegulatorGroupConfig>
            {
                new() { Operator = "And", Regulators = { AgeAtLeast(1000) } },
                new() { Operator = "And", Regulators = { RadiusAtLeast(1000) } },
            },
            Effects = { TestFactory.Emit(0, 1) }
        };
        var cell = Cell0(age: 50, radius: 12);
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), new Context(), 0.5);
        Assert.That(cell.StateOf("g").Active, Is.False);
    }

    // --- AND within a group -------------------------------------------------

    [Test]
    public void Group_AndRequiresAllConditions()
    {
        var gene = new GeneConfig
        {
            Id = "g",
            RegulatorGroups = new List<RegulatorGroupConfig>
            {
                new() { Operator = "And", Regulators = { AgeAtLeast(50), RadiusAtLeast(10) } }
            },
            Effects = { TestFactory.Emit(0, 1) }
        };

        var lowRadius = Cell0(age: 100, radius: 5);
        GenomeSystem.Evaluate(lowRadius, Type(), GenomeOf(gene), new Context(), 0.5);
        Assert.That(lowRadius.StateOf("g").Active, Is.False);

        var ok = Cell0(age: 100, radius: 12);
        GenomeSystem.Evaluate(ok, Type(), GenomeOf(gene), new Context(), 0.5);
        Assert.That(ok.StateOf("g").Active, Is.True);
    }

    [Test]
    public void Group_OrOperator_AnyCondition()
    {
        var gene = new GeneConfig
        {
            Id = "g",
            RegulatorGroups = new List<RegulatorGroupConfig>
            {
                new() { Operator = "Or", Regulators = { AgeAtLeast(1000), RadiusAtLeast(10) } }
            },
            Effects = { TestFactory.Emit(0, 1) }
        };
        var cell = Cell0(age: 50, radius: 12);
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), new Context(), 0.5);
        Assert.That(cell.StateOf("g").Active, Is.True);
    }

    // --- NOT inside a group -------------------------------------------------

    [Test]
    public void Group_Inversion_FlipsSingleCondition()
    {
        var inverted = AgeAtLeast(1000);
        inverted.Invert = true;
        var gene = new GeneConfig
        {
            Id = "g",
            RegulatorGroups = new List<RegulatorGroupConfig>
            {
                new() { Operator = "And", Regulators = { inverted } }
            },
            Effects = { TestFactory.Emit(0, 1) }
        };
        var cell = Cell0(age: 50);
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), new Context(), 0.5);
        Assert.That(cell.StateOf("g").Active, Is.True, "NOT(false) is true.");
    }

    [Test]
    public void Nested_MixedAndOrNot()
    {
        // (age>=1000 AND radius>=10) OR (NOT(age>=50) AND type==1)
        var notAge = AgeAtLeast(50); notAge.Invert = true;
        var gene = new GeneConfig
        {
            Id = "g",
            RegulatorGroups = new List<RegulatorGroupConfig>
            {
                new() { Operator = "And", Regulators = { AgeAtLeast(1000), RadiusAtLeast(10) } },
                new() { Operator = "And", Regulators = { notAge, new RegulatorConfig { Kind = "Phenotype", TypeId = 1 } } }
            },
            Effects = { TestFactory.Emit(0, 1) }
        };

        // age 20 (NOT(age>=50) true) and type 1 -> second group true.
        var cell = Cell0(age: 20, type: 1);
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), new Context(), 0.5);
        Assert.That(cell.StateOf("g").Active, Is.True);

        // age 20 but type 0 -> both groups false.
        var cell2 = Cell0(age: 20, type: 0);
        GenomeSystem.Evaluate(cell2, Type(), GenomeOf(gene), new Context(), 0.5);
        Assert.That(cell2.StateOf("g").Active, Is.False);
    }

    // --- Empty groups / unknown regulators ----------------------------------

    [Test]
    public void EmptyGroup_IsVacuouslyTrue()
    {
        var gene = new GeneConfig
        {
            Id = "g",
            RegulatorGroups = new List<RegulatorGroupConfig> { new() { Operator = "And" } },
            Effects = { TestFactory.Emit(0, 1) }
        };
        var cell = Cell0();
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), new Context(), 0.5);
        Assert.That(cell.StateOf("g").Active, Is.True, "AND of nothing is vacuously true.");
    }

    [Test]
    public void UnknownRegulator_IsNotSilentlySatisfied()
    {
        var gene = new GeneConfig
        {
            Id = "g",
            Regulators = { new RegulatorConfig { Kind = "TotallyUnknown" } },
            Effects = { TestFactory.Emit(0, 1) }
        };
        var cell = Cell0();
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), new Context(), 0.5);
        Assert.That(cell.StateOf("g").Active, Is.False,
            "Unknown regulators must not be treated as satisfied.");
    }

    // --- Backward compatibility ---------------------------------------------

    [Test]
    public void LegacyRegulators_WorkAsSingleAndGroup()
    {
        var gene = new GeneConfig
        {
            Id = "g",
            Regulators = { AgeAtLeast(50), RadiusAtLeast(10) },
            Effects = { TestFactory.Emit(0, 1) }
        };
        Assert.That(gene.EffectiveGroups(), Has.Count.EqualTo(1));
        Assert.That(gene.EffectiveGroups()[0].Operator, Is.EqualTo("And"));

        var cell = Cell0(age: 100, radius: 12);
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), new Context(), 0.5);
        Assert.That(cell.StateOf("g").Active, Is.True);
    }

    [Test]
    public void LegacyJson_WithoutGroups_StillLoadsAndRuns()
    {
        const string json = """
        [
          {
            "id": 0,
            "name": "g0",
            "genes": [
              {
                "id": "g",
                "name": "g",
                "regulators": [ { "kind": "Property", "property": "age", "min": 10.0 } ],
                "effects": [ { "kind": "EmitMorphogen", "morphogen": 0, "rate": 5.0 } ]
              }
            ]
          }
        ]
        """;
        var genomes = ConfigLoader.ParseGenomes(json);
        var gene = genomes[0].Genes[0];
        Assert.That(gene.RegulatorGroups, Is.Null);
        Assert.That(gene.EffectiveGroups(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Groups_RoundTripThroughJson()
    {
        var genome = GenomeOf(new GeneConfig
        {
            Id = "g",
            RegulatorGroups = new List<RegulatorGroupConfig>
            {
                new() { Operator = "And", Regulators = { AgeAtLeast(10) } },
                new() { Operator = "Or", Regulators = { RadiusAtLeast(4) } }
            },
            Effects = { TestFactory.Emit(0, 1) }
        });

        string json = ConfigLoader.SerializeGenomes(new[] { genome });
        var loaded = ConfigLoader.ParseGenomes(json);

        var groups = loaded[0].Genes[0].RegulatorGroups!;
        Assert.That(groups, Has.Count.EqualTo(2));
        Assert.That(groups[1].Operator, Is.EqualTo("Or"));
        Assert.That(groups[1].Regulators[0].Kind, Is.EqualTo("Property"));
        Assert.That(groups[1].Regulators[0].Property, Is.EqualTo("radius"));
    }
}

