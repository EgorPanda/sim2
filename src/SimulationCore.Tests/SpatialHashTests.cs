using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

[TestFixture]
public class SpatialHashTests
{
    private static List<Cell> MakeCells(params (double x, double y, double r)[] specs)
    {
        var list = new List<Cell>();
        for (int i = 0; i < specs.Length; i++)
            list.Add(new Cell { Id = i, X = specs[i].x, Y = specs[i].y, Radius = specs[i].r });
        return list;
    }

    [Test]
    public void QueryCircle_ReturnsOnlyNearbyCells()
    {
        var cells = MakeCells((10, 10, 5), (12, 12, 5), (200, 200, 5));
        var hash = new SpatialHash(32.0);
        hash.Rebuild(cells);

        var results = new List<int>();
        hash.QueryCircle(11, 11, 10, results);

        Assert.That(results, Does.Contain(0));
        Assert.That(results, Does.Contain(1));
        Assert.That(results, Does.Not.Contain(2));
    }

    [Test]
    public void QueryResults_AreSorted_ForDeterminism()
    {
        var cells = MakeCells((95, 95, 5), (97, 97, 5), (93, 96, 5));
        var hash = new SpatialHash(32.0);
        hash.Rebuild(cells);

        var results = new List<int>();
        hash.QueryCircle(95, 95, 10, results);

        var sorted = new List<int>(results);
        sorted.Sort();
        Assert.That(results, Is.EqualTo(sorted));
    }

    [Test]
    public void QueryCircle_FindsCellsAcrossBucketBoundaries()
    {
        var cells = MakeCells((31.9, 10, 2), (32.1, 10, 2));
        var hash = new SpatialHash(32.0);
        hash.Rebuild(cells);

        var results = new List<int>();
        hash.QueryCircle(32.0, 10, 5, results);

        Assert.That(results.Count, Is.EqualTo(2));
    }
}
