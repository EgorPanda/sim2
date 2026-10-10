using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

[TestFixture]
public class ConfigLoaderTests
{
    private static string FindConfigDir()
    {
        // Walk up from the test output directory to the repository root.
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "config");
            if (File.Exists(Path.Combine(candidate, "simulation.json")))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the 'config' directory.");
    }

    [Test]
    public void LoadFromDirectory_ReadsAllThreeFiles()
    {
        var setup = ConfigLoader.LoadFromDirectory(FindConfigDir());

        Assert.That(setup.Simulation.WorldWidth, Is.GreaterThan(0));
        Assert.That(setup.Morphogens.Length, Is.GreaterThanOrEqualTo(1));
        Assert.That(setup.Genomes.Length, Is.GreaterThanOrEqualTo(1));
        Assert.That(setup.CellTypes.Length, Is.GreaterThanOrEqualTo(1));
        Assert.That(setup.Genomes[0].Genes.Count, Is.GreaterThan(0));
    }

    [Test]
    public void ShippedConfig_BuildsRunnableSimulation_ThatGrows()
    {
        var setup = ConfigLoader.LoadFromDirectory(FindConfigDir());
        var sim = new Simulation(setup);

        Assert.That(sim.Cells.Count, Is.EqualTo(1), "One founder cell should be spawned.");

        for (int i = 0; i < 2000; i++) sim.Step(setup.Simulation.FixedTimeStep);

        Assert.That(sim.Cells.Count, Is.GreaterThan(1),
            "The shipped genome should produce a growing multicellular organism.");
    }
}
