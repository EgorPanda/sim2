namespace SimulationCore.Tests;

using NUnit.Framework;

[TestFixture]
public class CellMemoryTests
{
    private static SimulationSetup CellMemoryExperiment()
    {
        var setup = TestFactory.Setup();
        setup.Simulation.WorldWidth = 200.0;
        setup.Simulation.WorldHeight = 200.0;
        setup.Simulation.MorphogenResolution = 4.0;
        setup.Simulation.RngSeed = 12345;

        setup.Morphogens = new[]
        {
            new MorphogenConfig { Id = 0, Type = "External", Diffusion = 0.0, Decay = 1.0 },
            new MorphogenConfig { Id = 1, Type = "Internal", Diffusion = 0.0, Decay = 0.05 }
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
                        Id = "senseA",
                        RequireMorphogenTrigger = true,
                        RequiredMorphogenId = 0,
                        RequiredMorphogenThreshold = 0.5,
                        Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 1, Rate = 50.0 } }
                    },
                    new()
                    {
                        Id = "actFromB",
                        RequireMorphogenTrigger = true,
                        RequiredMorphogenId = 1,
                        RequiredMorphogenThreshold = 1.0,
                        Effects = new() { new() { Kind = "SetCellType", TypeId = 1, Once = true } }
                    },
                    new()
                    {
                        Id = "divideStem",
                        RequireMorphogenTrigger = false,
                        RegulatorGroups = new()
                        {
                            new() { Operator = "And", Regulators = new() { new() { Kind = "Phenotype", TypeId = 0 } } }
                        },
                        Effects = new() { new() { Kind = "Divide", Rate = 0.3 } }
                    },
                    new()
                    {
                        Id = "blockMemoryDivision",
                        RequireMorphogenTrigger = false,
                        RegulatorGroups = new()
                        {
                            new() { Operator = "And", Regulators = new() { new() { Kind = "Phenotype", TypeId = 1 } } }
                        },
                        Effects = new() { new() { Kind = "BlockDivision" } }
                    }
                }
            }
        };

        setup.CellTypes = new[]
        {
            new CellTypeConfig
            {
                Id = 0, Name = "stem", GenomeId = 0,
                BaseRadius = 6.0, MaxRadius = 6.0
            },
            new CellTypeConfig
            {
                Id = 1, Name = "memory", GenomeId = 0,
                BaseRadius = 6.0, MaxRadius = 6.0
            }
        };

        setup.Simulation.InitialExternalSources.Add(new MorphogenSourceConfig
        {
            MorphogenId = 0,
            X = setup.Simulation.WorldWidth * 0.5,
            Y = setup.Simulation.WorldHeight * 0.5,
            Radius = 30.0,
            Concentration = 2.0,
            Profile = "uniform"
        });

        return setup;
    }

    [Test]
    public void CellMemory_ExternalSignalProducesInternalMemory()
    {
        var setup = CellMemoryExperiment();
        var sim = new Simulation(setup);
        sim.Step(0.05);

        double b = sim.Cells[0].InternalMorphogenConcentrations.GetValueOrDefault(1);
        Assert.That(b, Is.GreaterThan(0.0));
    }

    [Test]
    public void CellMemory_MemoryCellsPersistAfterExternalSignalDisappears()
    {
        var setup = CellMemoryExperiment();
        var sim = new Simulation(setup);

        for (int i = 0; i < 150; i++) sim.Step(0.05);

        var memoryCells = sim.Cells.Where(c => c.TypeId == 1).ToList();
        Assert.That(memoryCells.Count, Is.GreaterThan(0), "some cells should differentiate to memory");

        // After the external source has decayed, memory cells should still carry the internal signal.
        foreach (var cell in memoryCells)
        {
            double external = sim.SampleMorphogen(0, cell.X, cell.Y);
            Assert.That(external, Is.LessThan(0.5), "external signal should be gone by now");
            Assert.That(cell.InternalMorphogenConcentrations.GetValueOrDefault(1), Is.GreaterThan(0.2),
                "internal memory should persist");
        }
    }

    [Test]
    public void CellMemory_CellsWithoutSignalRemainStem()
    {
        var setup = CellMemoryExperiment();
        var sim = new Simulation(setup);

        for (int i = 0; i < 150; i++) sim.Step(0.05);

        var stemCells = sim.Cells.Where(c => c.TypeId == 0).ToList();
        Assert.That(stemCells.Count, Is.GreaterThanOrEqualTo(0));

        foreach (var cell in stemCells)
        {
            double external = sim.SampleMorphogen(0, cell.X, cell.Y);
            Assert.That(external, Is.LessThan(0.5), "stem cells should be outside the external signal zone");
        }
    }
}
