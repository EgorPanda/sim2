using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

[TestFixture]
public class ExperimentTests
{
    private static string FindExperimentsDir()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "config", "experiments");
            if (File.Exists(Path.Combine(candidate, "multicellular.json")))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the 'config/experiments' directory.");
    }

    [Test]
    public void MulticellularExperiment_LoadsThreePhenotypes()
    {
        var setup = ConfigLoader.LoadExperiment(Path.Combine(FindExperimentsDir(), "multicellular.json"));

        Assert.That(setup.Simulation.WorldWidth, Is.GreaterThan(0));
        Assert.That(setup.Morphogens.Length, Is.GreaterThanOrEqualTo(3), "Demo uses several morphogens.");
        Assert.That(setup.Genomes.Length, Is.EqualTo(1), "All phenotypes share one genome.");
        Assert.That(setup.CellTypes.Length, Is.EqualTo(3), "Stem, worker and skin phenotypes.");
        Assert.That(setup.Adhesion.Pairs.Count, Is.GreaterThan(0));

        // Every phenotype references the same shared genome.
        foreach (var type in setup.CellTypes)
            Assert.That(setup.Genomes.Any(g => g.Id == type.GenomeId), Is.True, $"Type {type.Name} references a missing genome.");
    }

    [Test]
    public void MulticellularExperiment_IsReproducible()
    {
        var setup = ConfigLoader.LoadExperiment(Path.Combine(FindExperimentsDir(), "multicellular.json"));

        var a = new Simulation(setup);
        var b = new Simulation(setup);
        for (int i = 0; i < 600; i++) { a.Step(setup.Simulation.FixedTimeStep); b.Step(setup.Simulation.FixedTimeStep); }

        Assert.That(a.Cells.Count, Is.EqualTo(b.Cells.Count));
        for (int i = 0; i < a.Cells.Count; i++)
            Assert.That(a.Cells[i].X, Is.EqualTo(b.Cells[i].X).Within(1e-9), $"cell {i}");
    }

    [Test]
    public void MulticellularExperiment_GrowsButStaysBounded()
    {
        var setup = ConfigLoader.LoadExperiment(Path.Combine(FindExperimentsDir(), "multicellular.json"));
        var sim = new Simulation(setup);

        int maxCells = 0;
        int samples = 600;
        for (int i = 0; i < samples; i++)
        {
            sim.Step(setup.Simulation.FixedTimeStep);
            maxCells = Math.Max(maxCells, sim.Cells.Count);
        }

        Assert.That(sim.Cells.Count, Is.GreaterThan(1), "The colony must not die out.");
        Assert.That(maxCells, Is.LessThan(2000), "Colony must not grow without bound.");
    }

    [Test]
    public void MulticellularExperiment_ProducesMultiplePhenotypes()
    {
        var setup = ConfigLoader.LoadExperiment(Path.Combine(FindExperimentsDir(), "multicellular.json"));
        var sim = new Simulation(setup);

        var seen = new HashSet<int>();
        for (int i = 0; i < 1000; i++)
        {
            sim.Step(setup.Simulation.FixedTimeStep);
            foreach (var cell in sim.Cells)
                if (cell.TypeId >= 0) seen.Add(cell.TypeId);
        }

        Assert.That(seen, Does.Contain(0), "Stem cells must be present.");
        Assert.That(seen, Does.Contain(1), "The worker stage must appear.");
        Assert.That(seen, Does.Contain(2), "Differentiation must reach the skin phenotype.");
    }
}