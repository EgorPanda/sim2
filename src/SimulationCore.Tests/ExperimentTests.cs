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

    private static string FindDifferentiationDir()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "config", "experiments", "differentiation");
            if (File.Exists(Path.Combine(candidate, "simulation.json")))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate 'config/experiments/differentiation'.");
    }

    [Test]
    public void DifferentiationExperiment_IsShippedAndValid()
    {
        var profile = ConfigLoader.LoadExperiment(
            Path.Combine(FindExperimentsDir(), "differentiation.json"));
        Assert.That(profile.CellTypes.Length, Is.EqualTo(3), "Profile resolves the directory form.");
        Assert.That(ConfigValidator.HasErrors(ConfigValidator.Validate(profile)), Is.False,
            "The shipped profile must load and validate through the file-picker path.");

        var setup = ConfigLoader.LoadFromDirectory(FindDifferentiationDir());

        Assert.That(setup.Genomes.Length, Is.EqualTo(1), "All phenotypes share one genome.");
        Assert.That(setup.CellTypes.Length, Is.EqualTo(3), "Stem, worker and skin phenotypes.");
        Assert.That(setup.Genomes[0].Genes.Any(g =>
            g.Effects.Any(e => e.Kind == "Divide" && e.ChildTypeId.HasValue)), Is.True,
            "The demo must differentiate daughters at birth via Divide.ChildTypeId.");
        Assert.That(ConfigValidator.HasErrors(ConfigValidator.Validate(setup)), Is.False,
            "The shipped differentiation experiment must validate without errors.");
    }

    [Test]
    public void DifferentiationExperiment_BirthsWorkerAndSkin_WithoutPostHocChanges()
    {
        var setup = ConfigLoader.LoadFromDirectory(FindDifferentiationDir());
        var sim = new Simulation(setup);

        for (int i = 0; i < 1000; i++)
            sim.Step(setup.Simulation.FixedTimeStep);

        var counts = sim.Cells.GroupBy(c => c.TypeId).ToDictionary(g => g.Key, g => g.Count());
        Assert.That(counts.ContainsKey(0), Is.True, "Stem founders must stay in the colony.");
        Assert.That(counts.ContainsKey(1), Is.True, "Worker daughters must be born (high signal zone).");
        Assert.That(counts.ContainsKey(2), Is.True, "Skin daughters must be born (low signal zone).");

        var log = sim.CellSystem.DivisionLog;
        Assert.That(log.Count, Is.GreaterThanOrEqualTo(2), "The demo must actually divide.");
        Assert.That(log.All(d => d.ParentTypeId == 0), Is.True,
            "Only stem cells divide; differentiated workers/skin must never divide.");
        Assert.That(log.Any(d => d.ChildTypeId == 1) && log.Any(d => d.ChildTypeId == 2), Is.True,
            "Both birth phenotypes must be realised.");
        Assert.That(log.Any(d => d.Inherited), Is.False,
            "The demo never relies on the legacy inherit-parent fallback.");
    }

    [Test]
    public void DifferentiationExperiment_IsReproducible()
    {
        var setup = ConfigLoader.LoadFromDirectory(FindDifferentiationDir());

        var a = new Simulation(setup);
        var b = new Simulation(setup);
        for (int i = 0; i < 600; i++) { a.Step(setup.Simulation.FixedTimeStep); b.Step(setup.Simulation.FixedTimeStep); }

        Assert.That(a.Cells.Count, Is.EqualTo(b.Cells.Count));
        var countsA = a.Cells.GroupBy(c => c.TypeId).ToDictionary(g => g.Key, g => g.Count());
        var countsB = b.Cells.GroupBy(c => c.TypeId).ToDictionary(g => g.Key, g => g.Count());
        Assert.That(countsA, Is.EquivalentTo(countsB), "Fixed RNG seed reproduces identical colonies.");
    }

    private static string FindCellMemoryDir()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "config", "experiments", "cell_memory");
            if (File.Exists(Path.Combine(candidate, "simulation.json")))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate 'config/experiments/cell_memory'.");
    }

    [Test]
    public void CellMemoryExperiment_IsShippedAndValid()
    {
        var setup = ConfigLoader.LoadFromDirectory(FindCellMemoryDir());

        Assert.That(setup.Morphogens.Any(m => m.IsInternal), Is.True, "Demo must contain an internal morphogen.");
        Assert.That(setup.Morphogens.Any(m => m.IsExternal), Is.True, "Demo must contain an external morphogen.");
        Assert.That(setup.Genomes[0].Genes.Any(g => g.RequireMorphogenTrigger), Is.True,
            "Demo must use the mandatory morphogen trigger.");
        Assert.That(ConfigValidator.HasErrors(ConfigValidator.Validate(setup)), Is.False,
            "The shipped cell_memory experiment must validate without errors.");
    }

    [Test]
    public void CellMemoryExperiment_ProducesMemoryCellsAfterExternalSignalFades()
    {
        var setup = ConfigLoader.LoadFromDirectory(FindCellMemoryDir());
        var sim = new Simulation(setup);

        for (int i = 0; i < 600; i++)
            sim.Step(setup.Simulation.FixedTimeStep);

        var memory = sim.Cells.Where(c => c.TypeId == 1).ToList();
        Assert.That(memory.Count, Is.GreaterThan(0), "Some cells must switch to the memory phenotype.");

        foreach (var cell in memory)
        {
            Assert.That(cell.InternalMorphogenConcentrations.GetValueOrDefault(1), Is.GreaterThan(0.0),
                "Memory cells must retain the internal morphogen.");
        }
    }

    [Test]
    public void CellMemoryExperiment_IsReproducible()
    {
        var setup = ConfigLoader.LoadFromDirectory(FindCellMemoryDir());

        var a = new Simulation(setup);
        var b = new Simulation(setup);
        for (int i = 0; i < 600; i++) { a.Step(setup.Simulation.FixedTimeStep); b.Step(setup.Simulation.FixedTimeStep); }

        Assert.That(a.Cells.Count, Is.EqualTo(b.Cells.Count));
        var countsA = a.Cells.GroupBy(c => c.TypeId).ToDictionary(g => g.Key, g => g.Count());
        var countsB = b.Cells.GroupBy(c => c.TypeId).ToDictionary(g => g.Key, g => g.Count());
        Assert.That(countsA, Is.EquivalentTo(countsB), "Fixed RNG seed reproduces identical cell_memory colonies.");
    }
}