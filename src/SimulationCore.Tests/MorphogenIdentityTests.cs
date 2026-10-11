using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

/// <summary>
/// SIM1.7 morphogen identity, internal/external regulation and spatial emission tests.
/// </summary>
[TestFixture]
public class MorphogenIdentityTests
{
    [Test]
    public void ExternalMorphogenId0_WorksNormally()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: TestFactory.Gene("g", TestFactory.Emit(0, 10.0)));
        setup.Morphogens[0].Diffusion = 1.0;

        var sim = new Simulation(setup);
        sim.Step(0.05);

        Assert.That(sim.Morphogens.HasField(0), Is.True);
        Assert.That(sim.Morphogens.TotalMass(0), Is.GreaterThan(0.0));
        Assert.That(sim.SampleMorphogen(0, sim.Cells[0].X, sim.Cells[0].Y), Is.GreaterThan(0.0));
    }

    [Test]
    public void InternalMorphogenId0_DoesNotCreateExternalField()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: TestFactory.Gene("g",
            new GeneEffectConfig { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0 }));
        setup.Morphogens[0].Type = "Internal";

        var sim = new Simulation(setup);
        sim.Step(0.05);

        Assert.That(sim.Morphogens.FieldCount, Is.EqualTo(0), "Internal morphogens must not allocate fields.");
        Assert.That(sim.Cells[0].InternalMorphogenConcentrations.ContainsKey(0), Is.True);
    }

    [Test]
    public void NoExternalMorphogens_NoDummyField()
    {
        var setup = TestFactory.Setup(morphogenCount: 0, genome: Array.Empty<GeneConfig>());
        var sim = new Simulation(setup);

        Assert.That(sim.Morphogens.FieldCount, Is.EqualTo(0));
        Assert.That(sim.SampleMorphogen(0, 100, 100), Is.EqualTo(0.0));
        Assert.That(sim.MorphogenGradient(0, 100, 100), Is.EqualTo((0.0, 0.0)));
    }

    [Test]
    public void NonExistentMorphogenId_ReturnsZero()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: Array.Empty<GeneConfig>());
        var sim = new Simulation(setup);

        Assert.That(sim.SampleMorphogen(99, 100, 100), Is.EqualTo(0.0));
        Assert.That(sim.SampleMorphogen(-1, 100, 100), Is.EqualTo(0.0));
    }

    [Test]
    public void SparseExternalIds_AllocateCompactFields()
    {
        var sim = new SimulationConfig { WorldWidth = 256, WorldHeight = 256, MorphogenResolution = 16, RngSeed = 1 };
        var morphogens = new[]
        {
            new MorphogenConfig { Id = 2, Name = "a" },
            new MorphogenConfig { Id = 7, Name = "b" }
        };
        var setup = new SimulationSetup(
            sim, morphogens,
            new[] { new GenomeConfig { Id = 0 } },
            new[] { new CellTypeConfig { Id = 0, GenomeId = 0 } });

        var s = new Simulation(setup);
        Assert.That(s.Morphogens.FieldCount, Is.EqualTo(2));
        Assert.That(s.Morphogens.HasField(2), Is.True);
        Assert.That(s.Morphogens.HasField(7), Is.True);
        Assert.That(s.Morphogens.HasField(0), Is.False);

        s.Morphogens.SetField(2, 5, 5, 1.0);
        Assert.That(s.SampleMorphogen(2, 5 * 16 + 8, 5 * 16 + 8), Is.EqualTo(1.0).Within(1e-9));
        Assert.That(s.SampleMorphogen(7, 5 * 16 + 8, 5 * 16 + 8), Is.EqualTo(0.0));
    }

    [Test]
    public void MixedInternalExternal_OnlyExternalGetsField()
    {
        var sim = new SimulationConfig { WorldWidth = 256, WorldHeight = 256, MorphogenResolution = 16, RngSeed = 1 };
        var morphogens = new[]
        {
            new MorphogenConfig { Id = 0, Name = "ext" },
            new MorphogenConfig { Id = 1, Name = "int", Type = "Internal" }
        };
        var setup = new SimulationSetup(
            sim, morphogens,
            new[] { new GenomeConfig { Id = 0 } },
            new[] { new CellTypeConfig { Id = 0, GenomeId = 0 } });

        var s = new Simulation(setup);
        Assert.That(s.Morphogens.FieldCount, Is.EqualTo(1));
        Assert.That(s.Morphogens.HasField(0), Is.True);
        Assert.That(s.Morphogens.HasField(1), Is.False);
    }
}

[TestFixture]
public class MorphogenRegulatorTests
{
    [Test]
    public void MorphogenRegulator_ReadsExternalConcentration()
    {
        var gene = TestFactory.Gene("sense", TestFactory.Grow(1.0));
        gene.Regulators.Add(new RegulatorConfig { Kind = "Morphogen", Morphogen = 0, Min = 0.5, Max = double.PositiveInfinity });
        var setup = TestFactory.Setup(morphogenCount: 1, genome: gene);

        var sim = new Simulation(setup);
        // Seed the field at the founder cell's location.
        var cell = sim.Cells[0];
        int gx = (int)Math.Floor(cell.X / setup.Simulation.MorphogenResolution);
        int gy = (int)Math.Floor(cell.Y / setup.Simulation.MorphogenResolution);
        // Seed a uniform high field so the cell is guaranteed to sense it.
        for (int y = 0; y < sim.Morphogens.GridHeight; y++)
            for (int x = 0; x < sim.Morphogens.GridWidth; x++)
                sim.Morphogens.SetField(0, x, y, 10.0);
        sim.Step(0.05);

        Assert.That(sim.IsMorphogenInternal(0), Is.False, "Morphogen 0 must be external in this test.");
        Assert.That(sim.SampleMorphogen(0, cell.X, cell.Y), Is.GreaterThan(0.5), "Field sample at cell should be high.");

        Console.WriteLine($"[DEBUG] cell pos=({cell.X},{cell.Y}) sample={sim.SampleMorphogen(0, cell.X, cell.Y)} internal={sim.IsMorphogenInternal(0)}");
        foreach (var kvp in sim.Cells[0].GeneStates)
            Console.WriteLine($"[DEBUG] gene {kvp.Key} active={kvp.Value.Active}");

        Assert.That(sim.Cells[0].StateOf("sense").Active, Is.True, "External regulator activates when field is high.");
    }

    [Test]
    public void MorphogenRegulator_ReadsInternalConcentration_NotExternalField()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: new[]
        {
            new GeneConfig
            {
                Name = "mem", Id = "mem",
                Regulators = { new RegulatorConfig { Kind = "Morphogen", Morphogen = 0, Min = 0.5, Max = double.PositiveInfinity } },
                Effects = { new GeneEffectConfig { Kind = "Grow", Rate = 1.0 } }
            }
        });
        setup.Morphogens[0].Type = "Internal";
        setup.Simulation.InitialInternalMorphogens[0] = 1.0;

        var sim = new Simulation(setup);
        sim.Step(0.05);

        Assert.That(sim.Cells[0].StateOf("mem").Active, Is.True, "Internal regulator reads the cell's own concentration.");
    }

    [Test]
    public void InternalRegulator_DoesNotReadExternalField()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: new[]
        {
            new GeneConfig
            {
                Name = "mem", Id = "mem",
                Regulators = { new RegulatorConfig { Kind = "Morphogen", Morphogen = 0, Min = 0.5, Max = double.PositiveInfinity } },
                Effects = { new GeneEffectConfig { Kind = "Grow", Rate = 1.0 } }
            }
        });
        setup.Morphogens[0].Type = "Internal";
        // No initial internal concentration, but set external field high.
        var sim = new Simulation(setup);
        sim.Morphogens.SetField(0, 10, 10, 1.0);
        sim.Step(0.05);

        Assert.That(sim.Cells[0].StateOf("mem").Active, Is.False,
            "Internal regulator must not accidentally read the external field of the same id.");
    }
}

[TestFixture]
public class EmissionRadiusTests
{
    [Test]
    public void ZeroRadius_IsPointSource()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: new[]
        {
            new GeneConfig
            {
                Name = "emit", Id = "emit",
                Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 } },
                Effects = { new GeneEffectConfig { Kind = "EmitMorphogen", Morphogen = 0, Rate = 100.0, EmissionRadius = 0.0 } }
            }
        });

        var sim = new Simulation(setup);
        sim.Step(0.05);

        // All mass should sit in the single grid cell under the cell.
        double total = sim.Morphogens.TotalMass(0);
        int gx = (int)Math.Floor(sim.Cells[0].X / 8.0);
        int gy = (int)Math.Floor(sim.Cells[0].Y / 8.0);
        double local = sim.Morphogens.GetField(0, gx, gy);
        Assert.That(local, Is.EqualTo(total).Within(1e-9), "Zero radius emits into one grid cell.");
    }

    [Test]
    public void PositiveRadius_SpreadsToNeighbouringCells()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: new[]
        {
            new GeneConfig
            {
                Name = "emit", Id = "emit",
                Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 } },
                Effects = { new GeneEffectConfig { Kind = "EmitMorphogen", Morphogen = 0, Rate = 100.0, EmissionRadius = 20.0 } }
            }
        });

        var sim = new Simulation(setup);
        sim.Step(0.05);

        int gx = (int)Math.Floor(sim.Cells[0].X / 8.0);
        int gy = (int)Math.Floor(sim.Cells[0].Y / 8.0);
        double local = sim.Morphogens.GetField(0, gx, gy);
        Assert.That(local, Is.GreaterThan(0.0));

        bool spread = false;
        for (int dy = -3; dy <= 3 && !spread; dy++)
            for (int dx = -3; dx <= 3 && !spread; dx++)
                if (dx == 0 && dy == 0) continue;
                else if (sim.Morphogens.GetField(0, gx + dx, gy + dy) > 0.0)
                    spread = true;

        Assert.That(spread, Is.True, "Radius 20 must deposit signal into several grid cells.");
    }

    [Test]
    public void Radius_DoesNotCreateMass()
    {
        // Same rate, different radii -> same total mass per step.
        var setupA = TestFactory.Setup(morphogenCount: 1, genome: new[]
        {
            new GeneConfig
            {
                Name = "a", Id = "a",
                Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 } },
                Effects = { new GeneEffectConfig { Kind = "EmitMorphogen", Morphogen = 0, Rate = 100.0, EmissionRadius = 0.0 } }
            }
        });
        var setupB = TestFactory.CloneSetup(setupA);
        setupB.Genomes[0].Genes[0].Effects[0].EmissionRadius = 30.0;

        var simA = new Simulation(setupA);
        var simB = new Simulation(setupB);
        simA.Step(0.05);
        simB.Step(0.05);

        Assert.That(simB.Morphogens.TotalMass(0), Is.EqualTo(simA.Morphogens.TotalMass(0)).Within(1e-9),
            "Kernel normalization keeps total emission independent of radius.");
    }

    [Test]
    public void InternalEmission_IgnoresRadius()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: new[]
        {
            new GeneConfig
            {
                Name = "mem", Id = "mem",
                Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 } },
                Effects = { new GeneEffectConfig { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0, EmissionRadius = 50.0 } }
            }
        });
        setup.Morphogens[0].Type = "Internal";

        var sim = new Simulation(setup);
        sim.Step(0.05);

        Assert.That(sim.Morphogens.FieldCount, Is.EqualTo(0));
        Assert.That(sim.Cells[0].InternalMorphogenConcentrations[0], Is.EqualTo(0.5).Within(1e-9));
    }
}

[TestFixture]
public class MorphogenPropagationTests
{
    [Test]
    public void Diffusion_SpreadsToNeighbouringCell()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: Array.Empty<GeneConfig>());
        setup.Morphogens[0].Diffusion = 50.0;
        setup.Morphogens[0].Decay = 0.0;

        var sim = new Simulation(setup);
        sim.Morphogens.SetField(0, 16, 16, 1000.0);

        // Place a second cell nearby, in the direction diffusion will spread.
        sim.AddCell(16 * 8.0 + 8.0 + 24.0, 16 * 8.0 + 8.0, 0);

        for (int i = 0; i < 40; i++) sim.Step(0.05);

        Assert.That(sim.SampleMorphogen(0, sim.Cells[1].X, sim.Cells[1].Y), Is.GreaterThan(0.0),
            "Diffusion must carry the signal to a nearby cell.");
    }

    [Test]
    public void ZeroDiffusion_KeepsSignalLocal()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: Array.Empty<GeneConfig>());
        setup.Morphogens[0].Diffusion = 0.0;

        var sim = new Simulation(setup);
        sim.Morphogens.SetField(0, 16, 16, 1000.0);
        sim.AddCell(16 * 8.0 + 8.0 + 80.0, 16 * 8.0 + 8.0, 0);

        for (int i = 0; i < 20; i++) sim.Step(0.05);

        Assert.That(sim.SampleMorphogen(0, sim.Cells[1].X, sim.Cells[1].Y), Is.EqualTo(0.0).Within(1e-9),
            "Without diffusion the signal must stay at the source.");
    }

    [Test]
    public void HigherDiffusion_SpreadsFaster()
    {
        var setupA = TestFactory.Setup(morphogenCount: 1, genome: Array.Empty<GeneConfig>());
        setupA.Morphogens[0].Diffusion = 10.0;
        setupA.Morphogens[0].Decay = 0.0;
        var setupB = TestFactory.Setup(morphogenCount: 1, genome: Array.Empty<GeneConfig>());
        setupB.Morphogens[0].Diffusion = 100.0;
        setupB.Morphogens[0].Decay = 0.0;

        var simA = new Simulation(setupA);
        var simB = new Simulation(setupB);
        simA.Morphogens.SetField(0, 16, 16, 1000.0);
        simB.Morphogens.SetField(0, 16, 16, 1000.0);

        for (int i = 0; i < 20; i++)
        {
            simA.Step(0.05);
            simB.Step(0.05);
        }

        double farA = simA.SampleMorphogen(0, 16 * 8.0 + 8.0 + 48.0, 16 * 8.0 + 8.0);
        double farB = simB.SampleMorphogen(0, 16 * 8.0 + 8.0 + 48.0, 16 * 8.0 + 8.0);
        Assert.That(farB, Is.GreaterThan(farA), "Higher diffusion spreads the signal farther.");
    }

    [Test]
    public void HigherDecay_RemovesSignalFaster()
    {
        var setupA = TestFactory.Setup(morphogenCount: 1, genome: Array.Empty<GeneConfig>());
        setupA.Morphogens[0].Diffusion = 0.0;
        setupA.Morphogens[0].Decay = 0.0;
        var setupB = TestFactory.Setup(morphogenCount: 1, genome: Array.Empty<GeneConfig>());
        setupB.Morphogens[0].Diffusion = 0.0;
        setupB.Morphogens[0].Decay = 2.0;

        var simA = new Simulation(setupA);
        var simB = new Simulation(setupB);
        simA.Morphogens.SetField(0, 16, 16, 1000.0);
        simB.Morphogens.SetField(0, 16, 16, 1000.0);

        for (int i = 0; i < 20; i++)
        {
            simA.Step(0.05);
            simB.Step(0.05);
        }

        Assert.That(simB.Morphogens.TotalMass(0), Is.LessThan(simA.Morphogens.TotalMass(0)),
            "Higher decay removes mass faster.");
    }

    [Test]
    public void StoppingProduction_StopsNewMass()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: new[]
        {
            new GeneConfig
            {
                Name = "pulse", Id = "pulse",
                Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 0.5 } },
                Effects = { new GeneEffectConfig { Kind = "EmitMorphogen", Morphogen = 0, Rate = 1000.0 } }
            }
        });
        setup.Morphogens[0].Diffusion = 0.0;
        setup.Morphogens[0].Decay = 0.0;

        var sim = new Simulation(setup);
        for (int i = 0; i < 20; i++) sim.Step(0.05);

        double massDuring = sim.Morphogens.TotalMass(0);
        Assert.That(massDuring, Is.GreaterThan(0.0));

        // After 0.5 s the gene is off; with no decay mass stays constant.
        for (int i = 0; i < 20; i++) sim.Step(0.05);
        double massAfter = sim.Morphogens.TotalMass(0);

        Assert.That(massAfter, Is.EqualTo(massDuring).Within(1e-9),
            "Once production stops, total field mass must not grow.");
    }

    [Test]
    public void ValuesStayFiniteAndNonNegative()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: TestFactory.Gene("g", TestFactory.Emit(0, 1e6)));
        setup.Morphogens[0].Diffusion = 500.0;
        setup.Morphogens[0].Decay = 5.0;

        var sim = new Simulation(setup);
        for (int i = 0; i < 200; i++) sim.Step(0.05);

        Assert.That(double.IsFinite(sim.Morphogens.TotalMass(0)), Is.True);
        Assert.That(sim.Morphogens.TotalMass(0), Is.GreaterThanOrEqualTo(0.0));
    }
}

[TestFixture]
public class CellularMemoryTests
{
    [Test]
    public void InternalSignal_Persists_AfterExternalDisappears()
    {
        // External 0 triggers production of internal 1. After the external pulse
        // ends, internal 1 remains and keeps a gene active.
        var setup = TestFactory.Setup(morphogenCount: 2, genome: new[]
        {
            new GeneConfig
            {
                Name = "sense", Id = "sense",
                Regulators = { new RegulatorConfig { Kind = "Morphogen", Morphogen = 0, Min = 0.1, Max = double.PositiveInfinity } },
                Effects = { new GeneEffectConfig { Kind = "EmitMorphogen", Morphogen = 1, Rate = 20.0 } }
            },
            new GeneConfig
            {
                Name = "memory", Id = "memory",
                Regulators = { new RegulatorConfig { Kind = "Morphogen", Morphogen = 1, Min = 0.1, Max = double.PositiveInfinity } },
                Effects = { new GeneEffectConfig { Kind = "Grow", Rate = 1.0 } }
            }
        });
        setup.Morphogens[1].Type = "Internal";
        setup.Morphogens[1].Decay = 0.0;
        setup.Morphogens[0].Diffusion = 0.0; // keep external pulse local

        var sim = new Simulation(setup);
        var cell = sim.Cells[0];
        int gx = (int)Math.Floor(cell.X / setup.Simulation.MorphogenResolution);
        int gy = (int)Math.Floor(cell.Y / setup.Simulation.MorphogenResolution);
        sim.Morphogens.SetField(0, gx, gy, 1.0);

        for (int i = 0; i < 10; i++) sim.Step(0.05);

        Assert.That(sim.Cells[0].StateOf("memory").Active, Is.True, "Internal signal activates the memory gene.");

        // Remove external signal.
        sim.Morphogens.SetField(0, gx, gy, 0.0);
        for (int i = 0; i < 5; i++) sim.Step(0.05);

        Assert.That(sim.Cells[0].InternalMorphogenConcentrations[1], Is.GreaterThan(0.0),
            "Internal concentration persists after external signal is gone.");
        Assert.That(sim.Cells[0].StateOf("memory").Active, Is.True,
            "Memory gene stays active from internal concentration.");
    }

    [Test]
    public void InternalSignal_DoesNotSpreadToNeighbour()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: new[]
        {
            new GeneConfig
            {
                Name = "mem", Id = "mem",
                Regulators =
                {
                    new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 },
                    new RegulatorConfig { Kind = "Phenotype", TypeId = 0 } // only type 0 produces
                },
                Effects = { new GeneEffectConfig { Kind = "EmitMorphogen", Morphogen = 0, Rate = 100.0 } }
            }
        });
        setup.Morphogens[0].Type = "Internal";
        TestFactory.AddType(setup, 1, "other");

        var sim = new Simulation(setup);
        sim.AddCell(sim.Cells[0].X + 30, sim.Cells[0].Y, 1);
        sim.Step(0.05);

        Assert.That(sim.Cells[0].InternalMorphogenConcentrations.ContainsKey(0), Is.True);
        Assert.That(sim.Cells[1].InternalMorphogenConcentrations.ContainsKey(0), Is.False,
            "Internal morphogen must stay inside the producing cell; the neighbour does not produce it.");
    }
}
