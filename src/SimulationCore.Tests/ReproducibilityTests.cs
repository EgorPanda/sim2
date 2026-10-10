using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

[TestFixture]
public class ReproducibilityTests
{
    private static Simulation BuildSimulation(int seed)
    {
        // A genome that emits and absorbs a morphogen and grows, so behaviour
        // depends on RNG (division angles) and floating-point state.
        var emit = TestFactory.Gene("emit", TestFactory.Emit(0, 20));
        var absorb = new GeneConfig
        {
            Name = "absorb",
            Regulators = { new RegulatorConfig { Kind = "Morphogen", Morphogen = 0, Min = 0.001, Max = double.PositiveInfinity } },
            Effects = { TestFactory.Absorb(0, 10) }
        };
        var grow = new GeneConfig
        {
            Name = "grow",
            Regulators = { new RegulatorConfig { Kind = "Property", Property = "energy", Min = 20, Max = double.PositiveInfinity } },
            Effects = { TestFactory.Grow(2) }
        };
        var divide = new GeneConfig
        {
            Name = "divide",
            Regulators = { new RegulatorConfig { Kind = "Property", Property = "energy", Min = 40, Max = double.PositiveInfinity } },
            Effects = { TestFactory.Divide(1) }
        };

        var setup = TestFactory.Setup(
            morphogenCount: 1,
            diffusion: 20,
            decay: 0.1,
            initialCells: 1,
            seed: seed,
            genome: new[] { emit, absorb, grow, divide });

        return new Simulation(setup);
    }

    [Test]
    public void SameSeed_ProducesIdenticalState()
    {
        var a = BuildSimulation(42);
        var b = BuildSimulation(42);

        for (int i = 0; i < 400; i++)
        {
            a.Step(0.05);
            b.Step(0.05);
        }

        Assert.That(a.Cells.Count, Is.EqualTo(b.Cells.Count));
        for (int i = 0; i < a.Cells.Count; i++)
        {
            Assert.That(a.Cells[i].X, Is.EqualTo(b.Cells[i].X).Within(1e-12), $"cell {i} X");
            Assert.That(a.Cells[i].Y, Is.EqualTo(b.Cells[i].Y).Within(1e-12), $"cell {i} Y");
            Assert.That(a.Cells[i].Radius, Is.EqualTo(b.Cells[i].Radius).Within(1e-12), $"cell {i} radius");
            Assert.That(a.Cells[i].Energy, Is.EqualTo(b.Cells[i].Energy).Within(1e-9), $"cell {i} energy");
        }
        Assert.That(a.Time, Is.EqualTo(b.Time).Within(1e-12));
    }

    [Test]
    public void DifferentSeed_CanProduceDifferentLayout()
    {
        var a = BuildSimulation(1);
        var b = BuildSimulation(9999);

        for (int i = 0; i < 400; i++)
        {
            a.Step(0.05);
            b.Step(0.05);
        }

        bool identical = a.Cells.Count == b.Cells.Count;
        if (identical)
        {
            for (int i = 0; i < a.Cells.Count && identical; i++)
                if (Math.Abs(a.Cells[i].X - b.Cells[i].X) > 1e-6) identical = false;
        }
        Assert.That(identical, Is.False, "Different seeds should diverge in layout.");
    }

    [Test]
    public void SaveLoad_RestoresState_AndContinuesIdentically()
    {
        var sim = BuildSimulation(7);
        for (int i = 0; i < 200; i++) sim.Step(0.05);

        string path = Path.Combine(Path.GetTempPath(), $"sim2_save_{Guid.NewGuid():N}.json");
        try
        {
            SaveSystem.Save(path, sim);

            var loaded = BuildSimulation(7);
            SaveSystem.Load(path, loaded);

            Assert.That(loaded.Cells.Count, Is.EqualTo(sim.Cells.Count));
            Assert.That(loaded.Time, Is.EqualTo(sim.Time).Within(1e-12));
            Assert.That(loaded.Tick, Is.EqualTo(sim.Tick));

            // Both continue to evolve identically after a save/load round-trip.
            for (int i = 0; i < 100; i++)
            {
                sim.Step(0.05);
                loaded.Step(0.05);
            }

            Assert.That(loaded.Cells.Count, Is.EqualTo(sim.Cells.Count));
            for (int i = 0; i < sim.Cells.Count; i++)
                Assert.That(loaded.Cells[i].X, Is.EqualTo(sim.Cells[i].X).Within(1e-9), $"cell {i} X after load");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void SaveLoad_PreservesNamedTimerElapsedTime()
    {
        var sim = BuildSimulation(7);
        var cell = sim.Cells[0];
        cell.Timers["life"] = 7.0;
        cell.TimerTotals["life"] = 10.0;

        string path = Path.Combine(Path.GetTempPath(), $"sim2_timer_{Guid.NewGuid():N}.json");
        try
        {
            SaveSystem.Save(path, sim);

            var loaded = BuildSimulation(7);
            SaveSystem.Load(path, loaded);

            var restored = loaded.Cells[0];
            Assert.That(restored.TimerRemaining("life"), Is.EqualTo(7.0).Within(1e-9));
            Assert.That(restored.TimerElapsed("life"), Is.EqualTo(3.0).Within(1e-9));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
