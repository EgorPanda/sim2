namespace SimulationCore.Tests;

using NUnit.Framework;

[TestFixture]
public class InitializationTests
{
    [Test]
    public void InitialExternalSource_CreatesFieldConcentration()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[]
        {
            new MorphogenConfig { Id = 0, Type = "External", Diffusion = 0.0, Decay = 0.0 }
        };
        setup.Simulation.InitialExternalSources.Add(new MorphogenSourceConfig
        {
            MorphogenId = 0,
            X = setup.Simulation.WorldWidth * 0.5,
            Y = setup.Simulation.WorldHeight * 0.5,
            Radius = 20.0,
            Concentration = 5.0,
            Profile = "uniform"
        });

        var sim = new Simulation(setup);
        double c = sim.SampleMorphogen(0, setup.Simulation.WorldWidth * 0.5, setup.Simulation.WorldHeight * 0.5);
        Assert.That(c, Is.EqualTo(5.0).Within(1e-9));
    }

    [Test]
    public void FoundersStartWithZeroInternalConcentration()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[]
        {
            new MorphogenConfig { Id = 7, Type = "Internal" }
        };

        var sim = new Simulation(setup);
        Assert.That(sim.Cells[0].InternalMorphogenConcentrations.ContainsKey(7), Is.False,
            "Founders must not start with internal morphogen concentrations.");
    }

    [Test]
    public void NoInitialSignal_GeneWithRequiredTriggerStaysInactive()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[] { new MorphogenConfig { Id = 0, Type = "External" } };
        setup.Genomes = new[]
        {
            new GenomeConfig
            {
                Id = 0,
                Genes = new()
                {
                    new()
                    {
                        Id = "needsSignal",
                        RequireMorphogenTrigger = true,
                        RequiredMorphogenId = 0,
                        RequiredMorphogenThreshold = 0.1,
                        Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0 } }
                    }
                }
            }
        };
        setup.CellTypes = new[]
        {
            new CellTypeConfig { Id = 0, GenomeId = 0, BaseRadius = 6.0, MaxRadius = 6.0 }
        };

        var sim = new Simulation(setup);
        sim.Step(0.05);

        Assert.That(sim.Cells[0].StateOf("needsSignal").Active, Is.False);
    }

    [Test]
    public void InitialSignal_StartsGenomeCascade()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[] { new MorphogenConfig { Id = 0, Type = "External" } };
        setup.Simulation.InitialExternalSources.Add(new MorphogenSourceConfig
        {
            MorphogenId = 0,
            X = setup.Simulation.WorldWidth * 0.5,
            Y = setup.Simulation.WorldHeight * 0.5,
            Radius = 20.0,
            Concentration = 1.0,
            Profile = "uniform"
        });
        setup.Genomes = new[]
        {
            new GenomeConfig
            {
                Id = 0,
                Genes = new()
                {
                    new()
                    {
                        Id = "needsSignal",
                        RequireMorphogenTrigger = true,
                        RequiredMorphogenId = 0,
                        RequiredMorphogenThreshold = 0.1,
                        Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0 } }
                    }
                }
            }
        };
        setup.CellTypes = new[]
        {
            new CellTypeConfig { Id = 0, GenomeId = 0, BaseRadius = 6.0, MaxRadius = 6.0 }
        };

        var sim = new Simulation(setup);
        sim.Step(0.05);

        Assert.That(sim.Cells[0].StateOf("needsSignal").Active, Is.True);
        Assert.That(sim.Morphogens.TotalMass(0), Is.GreaterThan(0.0));
    }
}

