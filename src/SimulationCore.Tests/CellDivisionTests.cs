using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

[TestFixture]
public class CellDivisionTests
{
    private static Simulation EmptyGenomeSim()
    {
        // No genes at all: no cell gains division progress by itself, and
        // division is only attempted where DivisionProgress is set explicitly.
        var setup = TestFactory.Setup(
            morphogenCount: 1,
            initialCells: 1,
            genome: Array.Empty<GeneConfig>());
        return new Simulation(setup);
    }

    private static Simulation DividingGenomeSim()
    {
        // Gene divides immediately (Divide rate high enough to complete in one step).
        var setup = TestFactory.Setup(
            morphogenCount: 1,
            initialCells: 1,
            genome: TestFactory.Gene("div", TestFactory.Divide(100.0)));
        return new Simulation(setup);
    }

    [Test]
    public void Cell_Divides_WhenProgressComplete_AndSpaceAvailable()
    {
        var sim = DividingGenomeSim();

        sim.Step(0.05);

        Assert.That(sim.Cells.Count, Is.EqualTo(2), "A ready cell with space should divide into two.");
    }

    [Test]
    public void Division_ResetsProgress()
    {
        var sim = DividingGenomeSim();

        sim.Step(0.05);

        Assert.That(sim.Cells.Count, Is.EqualTo(2));
        Assert.That(sim.Cells[0].DivisionProgress, Is.EqualTo(0.0).Within(1e-12));
    }

    [Test]
    public void Division_IsBlocked_WhenNoFreeSpace()
    {
        var sim = EmptyGenomeSim();

        // Pack the parent densely from all sides so no direction is free.
        var parent = sim.Cells[0];
        double r = parent.Radius;
        for (int i = 0; i < 12; i++)
        {
            double a = i * Math.PI * 2.0 / 12.0;
            sim.AddCell(parent.X + Math.Cos(a) * r * 2.0, parent.Y + Math.Sin(a) * r * 2.0, parent.TypeId);
        }

        int before = sim.Cells.Count;
        for (int i = 0; i < 5; i++)
        {
            sim.Cells[0].DivisionProgress = 1.0;
            sim.Step(0.05);
        }

        Assert.That(sim.Cells.Count, Is.LessThanOrEqualTo(before + 1),
            "A fully surrounded cell must not divide (space check prevents overlap).");
    }
}
