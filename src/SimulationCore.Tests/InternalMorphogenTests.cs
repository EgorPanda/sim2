namespace SimulationCore.Tests;

using NUnit.Framework;
using System.IO;

[TestFixture]
public class InternalMorphogenTests
{
    private static SimulationSetup MemorySetup()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[]
        {
            new MorphogenConfig { Id = 0, Type = "External", Diffusion = 0.0, Decay = 0.0 },
            new MorphogenConfig { Id = 1, Type = "Internal", Diffusion = 0.0, Decay = 0.0 }
        };
        setup.Genomes = new[]
        {
            new GenomeConfig
            {
                Id = 0,
                Genes = new()
                {
                    new()
                    {
                        Id = "senseExternal",
                        RequireMorphogenTrigger = true,
                        RequiredMorphogenId = 0,
                        RequiredMorphogenThreshold = 0.5,
                        Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 1, Rate = 20.0 } }
                    },
                    new()
                    {
                        Id = "actInternal",
                        RequireMorphogenTrigger = true,
                        RequiredMorphogenId = 1,
                        RequiredMorphogenThreshold = 1.0,
                        Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 0, Rate = 5.0 } }
                    }
                }
            }
        };
        setup.CellTypes = new[]
        {
            new CellTypeConfig { Id = 0, GenomeId = 0, BaseRadius = 6.0, MaxRadius = 6.0, MaxEnergy = 1000, MaintenanceCost = 0.0 }
        };
        setup.Simulation.InitialExternalSources.Add(new MorphogenSourceConfig
        {
            MorphogenId = 0,
            X = setup.Simulation.WorldWidth * 0.5,
            Y = setup.Simulation.WorldHeight * 0.5,
            Radius = 20.0,
            Concentration = 1.0,
            Profile = "uniform"
        });
        return setup;
    }

    [Test]
    public void InternalMorphogen_IsStoredPerCell()
    {
        var setup = MemorySetup();
        var sim = new Simulation(setup);
        sim.Step(0.05);

        var a = sim.Cells[0];
        var b = sim.AddCell(a.X + 100.0, a.Y, 0);

        Assert.That(a.InternalMorphogenConcentrations.ContainsKey(1), Is.True);
        Assert.That(b.InternalMorphogenConcentrations.ContainsKey(1), Is.False);
    }

    [Test]
    public void InternalProduction_OnlyIncreasesProducerCell()
    {
        var setup = MemorySetup();
        var sim = new Simulation(setup);
        sim.Step(0.05);

        double producer = sim.Cells[0].InternalMorphogenConcentrations[1];
        Assert.That(producer, Is.GreaterThan(0.0));
    }

    [Test]
    public void InternalMorphogen_DoesNotDiffuseToNeighbor()
    {
        var setup = MemorySetup();
        var sim = new Simulation(setup);
        sim.Step(0.05);

        var neighbor = sim.AddCell(sim.Cells[0].X + 12.0, sim.Cells[0].Y, 0);
        double v = neighbor.InternalMorphogenConcentrations.GetValueOrDefault(1);
        Assert.That(v, Is.EqualTo(0.0));
    }

    [Test]
    public void InternalDecay_HappensLocally()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[]
        {
            new MorphogenConfig { Id = 1, Type = "Internal", Diffusion = 0.0, Decay = 2.0 }
        };
        setup.Genomes = new[] { new GenomeConfig { Id = 0, Genes = new() } };
        setup.CellTypes = new[] { new CellTypeConfig { Id = 0, GenomeId = 0, BaseRadius = 6.0, MaxRadius = 6.0, MaxEnergy = 100, MaintenanceCost = 0.0 } };

        var sim = new Simulation(setup);
        sim.Cells[0].InternalMorphogenConcentrations[1] = 1.0;
        double before = sim.Cells[0].InternalMorphogenConcentrations[1];

        sim.Step(0.5);
        double after = sim.Cells[0].InternalMorphogenConcentrations[1];

        Assert.That(after, Is.LessThan(before));
        Assert.That(after, Is.EqualTo(before * Math.Exp(-2.0 * 0.5)).Within(1e-9));
    }

    [Test]
    public void InternalConcentration_ActivatesGene()
    {
        var setup = MemorySetup();
        var sim = new Simulation(setup);
        // Seed enough external signal and step to produce internal morphogen.
        for (int i = 0; i < 10; i++) sim.Step(0.05);

        double internalConc = sim.Cells[0].InternalMorphogenConcentrations.GetValueOrDefault(1);
        Assert.That(internalConc, Is.GreaterThanOrEqualTo(1.0));
        Assert.That(sim.Cells[0].StateOf("actInternal").Active, Is.True);
    }

    [Test]
    public void Division_InheritsInternalMorphogens()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[]
        {
            new MorphogenConfig { Id = 1, Type = "Internal", Diffusion = 0.0, Decay = 0.0 }
        };
        setup.Genomes = new[]
        {
            new GenomeConfig
            {
                Id = 0,
                Genes = new()
                {
                    new()
                    {
                        Id = "divide",
                        RequireMorphogenTrigger = false,
                        Effects = new() { new() { Kind = "Divide", Rate = 100.0 } }
                    }
                }
            }
        };
        setup.CellTypes = new[]
        {
            new CellTypeConfig { Id = 0, GenomeId = 0, BaseRadius = 6.0, MaxRadius = 6.0, MaxEnergy = 100, MaintenanceCost = 0.0, DivisionEnergyCost = 0.0 }
        };

        var sim = new Simulation(setup);
        sim.Cells[0].InternalMorphogenConcentrations[1] = 5.0;

        sim.Step(0.05);

        Assert.That(sim.Cells.Count, Is.GreaterThan(1));
        var child = sim.Cells.First(c => c.Id != sim.Cells[0].Id);
        Assert.That(child.InternalMorphogenConcentrations[1], Is.EqualTo(5.0).Within(1e-9));
    }

    [Test]
    public void SaveLoad_PreservesInternalConcentrations()
    {
        var setup = MemorySetup();
        var sim = new Simulation(setup);
        sim.Step(0.05);
        string path = Path.Combine(Path.GetTempPath(), $"sim2_internal_{Guid.NewGuid()}.json");
        try
        {
            SaveSystem.Save(path, sim);
            var loaded = new Simulation(setup);
            SaveSystem.Load(path, loaded);

            Assert.That(loaded.Cells[0].InternalMorphogenConcentrations.ContainsKey(1), Is.True);
            Assert.That(loaded.Cells[0].InternalMorphogenConcentrations[1],
                Is.EqualTo(sim.Cells[0].InternalMorphogenConcentrations[1]).Within(1e-9));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void Load_OldSave_WithoutInternalConcentrations_Succeeds()
    {
        var setup = MemorySetup();
        var sim = new Simulation(setup);
        string path = Path.Combine(Path.GetTempPath(), $"sim2_v2_{Guid.NewGuid()}.json");
        try
        {
            SaveSystem.Save(path, sim);
            string json = File.ReadAllText(path);
            json = json.Replace("\"version\": 3", "\"version\": 2");
            json = json.Replace("\"internalMorphogenConcentrations\": {}", "");
            File.WriteAllText(path, json);

            var loaded = new Simulation(setup);
            Assert.DoesNotThrow(() => SaveSystem.Load(path, loaded));
            Assert.That(loaded.Cells[0].InternalMorphogenConcentrations.Count, Is.EqualTo(0));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
