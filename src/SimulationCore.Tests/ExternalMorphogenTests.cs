namespace SimulationCore.Tests;

using NUnit.Framework;

[TestFixture]
public class ExternalMorphogenTests
{
    [Test]
    public void ExternalProduction_AddsToField()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[] { new MorphogenConfig { Id = 0, Type = "External", Diffusion = 0.0, Decay = 0.0 } };
        setup.Genomes = new[]
        {
            new GenomeConfig
            {
                Id = 0,
                Genes = new()
                {
                    new()
                    {
                        Id = "producer",
                        RequireMorphogenTrigger = false,
                        Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 0, Rate = 10.0 } }
                    }
                }
            }
        };
        setup.CellTypes = new[] { new CellTypeConfig { Id = 0, GenomeId = 0, BaseRadius = 6.0, MaxRadius = 6.0, MaxEnergy = 1000, MaintenanceCost = 0.0 } };

        var sim = new Simulation(setup);
        sim.Step(0.1);

        Assert.That(sim.Morphogens.TotalMass(0), Is.EqualTo(1.0).Within(1e-9));
    }

    [Test]
    public void Diffusion_TransfersSignalToNeighbouringAreas()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[] { new MorphogenConfig { Id = 0, Type = "External", Diffusion = 100.0, Decay = 0.0 } };
        setup.Simulation.InitialExternalSources.Add(new MorphogenSourceConfig
        {
            MorphogenId = 0,
            X = setup.Simulation.WorldWidth * 0.5,
            Y = setup.Simulation.WorldHeight * 0.5,
            Radius = 8.0,
            Concentration = 10.0,
            Profile = "uniform"
        });

        var sim = new Simulation(setup);
        double before = sim.SampleMorphogen(0, setup.Simulation.WorldWidth * 0.5 + 40.0, setup.Simulation.WorldHeight * 0.5);
        for (int i = 0; i < 50; i++) sim.Step(0.05);
        double after = sim.SampleMorphogen(0, setup.Simulation.WorldWidth * 0.5 + 40.0, setup.Simulation.WorldHeight * 0.5);

        Assert.That(after, Is.GreaterThan(before));
    }

    [Test]
    public void ExternalDecay_ReducesConcentration()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[] { new MorphogenConfig { Id = 0, Type = "External", Diffusion = 0.0, Decay = 1.0 } };
        setup.Simulation.InitialExternalSources.Add(new MorphogenSourceConfig
        {
            MorphogenId = 0,
            X = setup.Simulation.WorldWidth * 0.5,
            Y = setup.Simulation.WorldHeight * 0.5,
            Radius = 8.0,
            Concentration = 10.0,
            Profile = "uniform"
        });

        var sim = new Simulation(setup);
        double before = sim.Morphogens.TotalMass(0);
        for (int i = 0; i < 50; i++) sim.Step(0.05);
        double after = sim.Morphogens.TotalMass(0);

        Assert.That(after, Is.LessThan(before));
    }

    [Test]
    public void NeighbourCell_ActivatesGeneFromExternalMorphogen()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[] { new MorphogenConfig { Id = 0, Type = "External", Diffusion = 50.0, Decay = 0.1 } };
        setup.Simulation.InitialExternalSources.Add(new MorphogenSourceConfig
        {
            MorphogenId = 0,
            X = setup.Simulation.WorldWidth * 0.5,
            Y = setup.Simulation.WorldHeight * 0.5,
            Radius = 8.0,
            Concentration = 10.0,
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
                        Id = "sensor",
                        RequireMorphogenTrigger = true,
                        RequiredMorphogenId = 0,
                        RequiredMorphogenThreshold = 0.1,
                        Effects = new() { new() { Kind = "EmitMorphogen", Morphogen = 0, Rate = 1.0 } }
                    }
                }
            }
        };
        setup.CellTypes = new[] { new CellTypeConfig { Id = 0, GenomeId = 0, BaseRadius = 6.0, MaxRadius = 6.0, MaxEnergy = 1000, MaintenanceCost = 0.0 } };

        var sim = new Simulation(setup);
        var placed = sim.AddCell(setup.Simulation.WorldWidth * 0.5 + 16.0, setup.Simulation.WorldHeight * 0.5, 0);
        for (int i = 0; i < 30; i++) sim.Step(0.05);

        Assert.That(placed.StateOf("sensor").Active, Is.True);
    }

    [Test]
    public void OldMorphogenConfig_LoadsAsExternal()
    {
        string json = "[{ \"id\": 0, \"name\": \"legacy\", \"diffusion\": 10.0, \"decay\": 0.1 }]";
        var morphogens = ConfigLoader.ParseMorphogens(json);
        Assert.That(morphogens[0].IsExternal, Is.True);
    }

    [Test]
    public void ExternalGradient_IsFinite()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[] { new MorphogenConfig { Id = 0, Type = "External", Diffusion = 0.0, Decay = 0.0 } };
        setup.Simulation.InitialExternalSources.Add(new MorphogenSourceConfig
        {
            MorphogenId = 0,
            X = setup.Simulation.WorldWidth * 0.5,
            Y = setup.Simulation.WorldHeight * 0.5,
            Radius = 8.0,
            Concentration = 10.0,
            Profile = "uniform"
        });

        var sim = new Simulation(setup);
        var (dx, dy) = sim.Morphogens.Gradient(0, setup.Simulation.WorldWidth * 0.5 + 12.0, setup.Simulation.WorldHeight * 0.5);

        Assert.That(double.IsFinite(dx), Is.True);
        Assert.That(double.IsFinite(dy), Is.True);
    }
}
