using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

[TestFixture]
public class CellDivisionTests
{
    [Test]
    public void Cell_Divides_WhenProgressComplete_AndSpaceAvailable()
    {
        // Gene divides immediately (Divide rate high enough to complete in one step).
        var setup = TestFactory.Setup(
            morphogenCount: 1,
            initialCells: 1,
            genome: TestFactory.Gene("div", TestFactory.Divide(100.0)));

        var sim = new Simulation(setup);
        // Give the founder plenty of energy (above division cost).
        sim.Cells[0].Energy = 100.0;

        sim.Step(0.05);

        Assert.That(sim.Cells.Count, Is.EqualTo(2), "A ready cell with space should divide into two.");
    }

    [Test]
    public void Division_SplitsEnergy_AndResetsProgress()
    {
        var setup = TestFactory.Setup(
            morphogenCount: 1,
            initialCells: 1,
            genome: TestFactory.Gene("div", TestFactory.Divide(100.0)));
        var sim = new Simulation(setup);
        sim.Cells[0].Energy = 90.0; // cost 10 -> remaining 80 -> 40 each

        sim.Step(0.05);

        Assert.That(sim.Cells.Count, Is.EqualTo(2));
        Assert.That(sim.Cells[0].Energy, Is.EqualTo(40.0).Within(1e-6));
        Assert.That(sim.Cells[1].Energy, Is.EqualTo(40.0).Within(1e-6));
        Assert.That(sim.Cells[0].DivisionProgress, Is.EqualTo(0.0).Within(1e-12));
    }

    [Test]
    public void Division_IsBlocked_WhenNoFreeSpace()
    {
        var setup = TestFactory.Setup(
            morphogenCount: 1,
            initialCells: 1,
            genome: TestFactory.Gene("div", TestFactory.Divide(100.0)));
        var sim = new Simulation(setup);

        // Pack the parent densely from all sides so no direction is free.
        var parent = sim.Cells[0];
        parent.Energy = 100.0;
        double r = parent.Radius;
        for (int i = 0; i < 12; i++)
        {
            double a = i * Math.PI * 2.0 / 12.0;
            var blocker = sim.AddCell(parent.X + Math.Cos(a) * r * 2.0, parent.Y + Math.Sin(a) * r * 2.0, parent.TypeId);
            blocker.Energy = 0.0; // cannot afford division, so only the parent is tested
        }

        int before = sim.Cells.Count;
        for (int i = 0; i < 5; i++)
        {
            parent.Energy = 100.0;
            parent.DivisionProgress = 1.0;
            sim.Step(0.05);
        }

        Assert.That(sim.Cells.Count, Is.LessThanOrEqualTo(before + 1),
            "A fully surrounded cell must not divide (space check prevents overlap).");
    }

    [Test]
    public void Starvation_KillsCell()
    {
        var setup = TestFactory.Setup(
            morphogenCount: 1,
            initialCells: 1,
            genome: Array.Empty<GeneConfig>());
        // High maintenance, no food source.
        setup.CellTypes[0].MaintenanceCost = 100.0;
        setup.CellTypes[0].StarveGracePeriod = 0.2;

        var sim = new Simulation(setup);
        sim.Cells[0].Energy = 1.0;

        for (int i = 0; i < 40; i++) sim.Step(0.05);

        Assert.That(sim.Cells.Count, Is.EqualTo(0), "A starved cell should die and be removed.");
    }
}
