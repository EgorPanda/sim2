using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

/// <summary>
/// A small end-to-end check for several distinct genome sets: every set must
/// validate cleanly, survive a save/load round-trip (as the Laboratory's
/// «Сохранить» / «Открыть…» produce), and step a live simulation with the
/// expected biological behaviour.
/// </summary>
[TestFixture]
public class GenomeSetSmokeTests
{
    private static void ValidateAndRoundTrip(SimulationSetup setup, string path)
    {
        var issues = ConfigValidator.Validate(setup);
        Assert.That(issues.Where(i => i.IsError), Is.Empty, $"{path}: configuration must validate without errors.");

        ConfigWriter.SaveSetupToDirectory(path, setup);
        var loaded = ConfigLoader.LoadFromDirectory(path);
        Assert.That(loaded.Morphogens.Length, Is.EqualTo(setup.Morphogens.Length), $"{path}: morphogen count");
        Assert.That(loaded.Genomes.Length, Is.EqualTo(setup.Genomes.Length), $"{path}: genome count");
        Assert.That(loaded.CellTypes.Length, Is.EqualTo(setup.CellTypes.Length), $"{path}: cell type count");

        var issues2 = ConfigValidator.Validate(loaded);
        Assert.That(issues2.Where(i => i.IsError), Is.Empty, $"{path}: the reloaded configuration must still be valid.");
    }

    private static Simulation RunSteps(SimulationSetup setup, int steps)
    {
        var sim = new Simulation(setup);
        double dt = setup.Simulation.FixedTimeStep;
        for (int i = 0; i < steps; i++) sim.Step(dt);
        return sim;
    }

    [Test]
    public void GenomeSet_MinimalDivide_GrowsPopulation()
    {
        var setup = TestFactory.Setup(
            initialCells: 1,
            genome: TestFactory.Gene("div", TestFactory.Divide(1.0)));

        string path = TempDir("set_minimal");
        try
        {
            ValidateAndRoundTrip(setup, path);
            var sim = RunSteps(setup, 40); // progress 1.0 in 2 s, then a division
            Assert.That(sim.Cells.Count, Is.GreaterThan(1), "An always-on Divide gene must grow the population.");
        }
        finally { Directory.Delete(path, true); }
    }

    [Test]
    public void GenomeSet_DifferentiationCascade_SwitchesPhenotypes()
    {
        var setup = TestFactory.Setup(
            morphogenCount: 1,
            initialCells: 1,
            genome: new[]
            {
                TestFactory.Gene("div_worker",
                    TestFactory.Divide((int?)1, 1.0)),
                new GeneConfig
                {
                    Name = "block_worker", Id = "block_worker",
                    Regulators = { new RegulatorConfig { Kind = "Phenotype", TypeId = 1 } },
                    Effects = { new GeneEffectConfig { Kind = "BlockDivision" } }
                }
            });
        TestFactory.AddType(setup, 1, "worker");

        string path = TempDir("set_dif");
        try
        {
            ValidateAndRoundTrip(setup, path);
            var sim = RunSteps(setup, 30);
            Assert.That(sim.Cells.Any(c => c.TypeId == 1), Is.True,
                "Daughters of the worker rule are born as phenotype 1.");
        }
        finally { Directory.Delete(path, true); }
    }

    [Test]
    public void GenomeSet_Chemotaxis_CellsMoveAlongGradient()
    {
        var setup = TestFactory.Setup(
            morphogenCount: 1,
            genome: new[]
            {
                new GeneConfig
                {
                    Name = "move", Id = "move",
                    Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 } },
                    Effects = { new GeneEffectConfig { Kind = "Move", Morphogen = 0, Rate = 20.0 } }
                }
            });

        string path = TempDir("set_chemotaxis");
        try
        {
            ValidateAndRoundTrip(setup, path);

            var sim = new Simulation(setup);
            double dt = setup.Simulation.FixedTimeStep;

            // A field rising along +x: value(x) = 0.001 * x.
            for (int gx = 0; gx < sim.Morphogens.GridWidth; gx++)
                for (int gy = 0; gy < sim.Morphogens.GridHeight; gy++)
                    sim.Morphogens.SetField(0, gx, gy, gx * 0.001);

            double startX = sim.Cells[0].X;
            for (int i = 0; i < 40; i++) sim.Step(dt);

            Assert.That(sim.Cells[0].X, Is.GreaterThan(startX + 1.0),
                "An active Move gene pulls the cell along the morphogen gradient.");
        }
        finally { Directory.Delete(path, true); }
    }

    [Test]
    public void GenomeSet_TimerSchedule_GeneFiresAfterGivenTime()
    {
        var setup = TestFactory.Setup(
            morphogenCount: 1,
            genome: new[]
            {
                new GeneConfig
                {
                    Name = "startLife", Id = "startLife",
                    Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 } },
                    Effects = { new GeneEffectConfig { Kind = "StartTimer", Timer = "life", Duration = 10.0, Once = true } }
                },
                new GeneConfig
                {
                    Name = "gate", Id = "gate",
                    Regulators = { new RegulatorConfig { Kind = "Timer", Timer = "life", Min = 1.0 } },
                    Effects = { new GeneEffectConfig { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0, Once = true } }
                }
            });

        string path = TempDir("set_timer");
        try
        {
            ValidateAndRoundTrip(setup, path);

            var sim = new Simulation(setup);
            double dt = setup.Simulation.FixedTimeStep;
            double total = 0.0;
            int ticks = 0;
            while (total < 0.9)
            {
                sim.Step(dt); total += dt; ticks++;
            }
            Assert.That(sim.Cells[0].StateOf("gate").Active, Is.False,
                "The gate gene must wait until 1 s of the timer elapsed.");

            while (total < 2.0)
            {
                sim.Step(dt); total += dt; ticks++;
            }
            Assert.That(sim.Cells[0].StateOf("gate").Active, Is.True,
                "After 1 s of the timer the gated gene activates.");
        }
        finally { Directory.Delete(path, true); }
    }

    [Test]
    public void GenomeSet_EmptyGenome_IsValidAndRuns()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: Array.Empty<GeneConfig>());

        string path = TempDir("set_empty");
        try
        {
            ValidateAndRoundTrip(setup, path);
            var sim = RunSteps(setup, 10);
            Assert.That(sim.Cells.Count, Is.EqualTo(1), "A gene-less cell just lives.");
            Assert.That(sim.Cells[0].State == CellState.Alive, Is.True);
        }
        finally { Directory.Delete(path, true); }
    }

    [Test]
    public void GenomeSet_StaleExternalReferences_SanitizeOnLoad()
    {
        // A profile edited the way the user did: morphogen 0 deleted, gradient
        // and initial signals still point at it. Loading must auto-repair.
        var setup = TestFactory.Setup(morphogenCount: 2, genome: TestFactory.Gene("g", TestFactory.Emit(1, 1.0)));
        setup.Morphogens = new[]
        {
            new MorphogenConfig { Id = 1, Name = "m1", Diffusion = 1.0, Decay = 0.1 },
            new MorphogenConfig { Id = 2, Name = "m2", Diffusion = 1.0, Decay = 0.1 }
        };
        setup.Simulation.DivisionGradientMorphogen = 0;
        setup.Simulation.InitialExternalSources.Add(new MorphogenSourceConfig
        {
            MorphogenId = 0,
            X = 10, Y = 10, Radius = 20, Concentration = 1.0,
            Profile = "uniform"
        });

        string path = TempDir("set_stale");
        try
        {
            ConfigWriter.SaveSetupToDirectory(path, setup);
            var loaded = ConfigLoader.LoadFromDirectory(path);

            Assert.That(loaded.Simulation.DivisionGradientMorphogen, Is.EqualTo(-1),
                "A dangling division-gradient pointer is disabled at load.");
            Assert.That(loaded.Simulation.InitialExternalSources, Is.Empty,
                "Dangling initial sources are dropped at load.");
            var issues = ConfigValidator.Validate(loaded);
            Assert.That(issues.Where(i => i.IsError), Is.Empty);

            var sim = new Simulation(loaded);
            sim.Step(0.05);
            Assert.That(sim.Cells[0].State == CellState.Alive, Is.True);
        }
        finally { Directory.Delete(path, true); }
    }

    private static string TempDir(string name)
    {
        string path = Path.Combine(Path.GetTempPath(), $"sim2_{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
