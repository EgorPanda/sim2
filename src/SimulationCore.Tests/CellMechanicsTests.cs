using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

[TestFixture]
public class CellMechanicsTests
{
    [Test]
    public void OverlappingCells_AreSeparated_AfterResolve()
    {
        var cells = new List<Cell>
        {
            new() { Id = 0, X = 100, Y = 100, Radius = 8 },
            new() { Id = 1, X = 104, Y = 100, Radius = 8 }
        };
        var hash = new SpatialHash(32.0);
        hash.Rebuild(cells);

        var mechanics = new CellMechanics { Iterations = 10, Stiffness = 0.9 };
        for (int i = 0; i < 40; i++)
        {
            for (int j = 0; j < 4; j++) mechanics.Resolve(cells, hash, 16.0);
            hash.Rebuild(cells);
        }

        double dx = cells[1].X - cells[0].X;
        double dy = cells[1].Y - cells[0].Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double minDist = cells[0].Radius + cells[1].Radius;

        Assert.That(dist, Is.GreaterThanOrEqualTo(minDist - 0.5),
            "Overlapping cells should be pushed apart until they no longer intersect.");
    }

    [Test]
    public void CoincidentCells_GetSeparatedDeterministically()
    {
        var cells = new List<Cell>
        {
            new() { Id = 0, X = 50, Y = 50, Radius = 5 },
            new() { Id = 1, X = 50, Y = 50, Radius = 5 }
        };
        var hash = new SpatialHash(32.0);
        hash.Rebuild(cells);

        var mechanics = new CellMechanics { Iterations = 2 };
        mechanics.Resolve(cells, hash, 10.0);

        Assert.That(cells[0].X != cells[1].X || cells[0].Y != cells[1].Y, Is.True,
            "Coincident cells must be pushed apart, not left on top of each other.");
    }
}
