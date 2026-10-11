using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

/// <summary>
/// Smoke tests for the bundled demonstration experiments. They must load,
/// validate cleanly and step without crashing.
/// </summary>
[TestFixture]
public class DemoExperimentsTests
{
    private static string ProjectRoot => "E:\\sim23";

    [Test]
    public void CellMemory_LoadsAndRuns()
    {
        string profile = System.IO.Path.Combine(ProjectRoot, "config", "experiments", "cell_memory.json");
        var setup = ConfigLoader.LoadExperiment(profile);
        var issues = ConfigValidator.Validate(setup);
        Assert.That(issues.Where(i => i.IsError), Is.Empty, string.Join("\n", issues));

        var sim = new Simulation(setup);
        for (int i = 0; i < 50; i++) sim.Step(0.05);

        // The initial external source triggers internal memory production.
        Assert.That(sim.Cells[0].InternalMorphogenConcentrations.ContainsKey(1), Is.True,
            "Cell memory experiment must produce internal morphogen B.");
    }

    [Test]
    public void MorphogenSpread_LoadsAndRuns()
    {
        string profile = System.IO.Path.Combine(ProjectRoot, "config", "experiments", "morphogen_spread.json");
        var setup = ConfigLoader.LoadExperiment(profile);
        var issues = ConfigValidator.Validate(setup);
        Assert.That(issues.Where(i => i.IsError), Is.Empty, string.Join("\n", issues));

        var sim = new Simulation(setup);
        for (int i = 0; i < 50; i++) sim.Step(0.05);

        // Signal A must leave the producer and reach the responder.
        Assert.That(sim.Morphogens.TotalMass(0), Is.GreaterThan(0.0),
            "External signal A must accumulate in the field.");
    }
}
