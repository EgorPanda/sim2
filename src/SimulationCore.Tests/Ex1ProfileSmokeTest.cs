using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

[TestFixture]
public class Ex1ProfileSmokeTest
{
    [Test]
    public void UserEx1Profile_LoadsCleanly_AndRuns()
    {
        string profile = "E:\\sim23\\Genom\\ex1.json";
        if (!File.Exists(profile)) Assert.Ignore("ex1.json is not present in this workspace.");

        var loaded = ConfigLoader.LoadExperiment(profile);
        Assert.That(loaded.Simulation.DivisionGradientMorphogen, Is.EqualTo(-1),
            "The stale gradient pointer was auto-repaired.");
        var issues = ConfigValidator.Validate(loaded);
        Assert.That(issues.Where(i => i.IsError), Is.Empty, string.Join("\n", issues));

        var sim = new Simulation(loaded);
        sim.Step(0.05);
        Assert.That(sim.Cells[0].State == CellState.Alive, Is.True);
        Assert.That(sim.Cells.Count, Is.EqualTo(1));
    }
}
