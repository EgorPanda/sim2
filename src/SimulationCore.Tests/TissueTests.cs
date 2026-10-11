using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

[TestFixture]
public class TissueTests
{
    // --- Adhesion -----------------------------------------------------------

    [Test]
    public void Adhesion_AllowsControlledOverlap_ForCoupledPair()
    {
        // Two cells of the same phenotype with adhesion 1.0 and maxOverlap 0.4:
        // they should settle at touch*(1-0.4) = 0.6*touch instead of full separation.
        var cells = new List<Cell>
        {
            new() { Id = 0, X = 100, Y = 100, Radius = 8, TypeId = 0 },
            new() { Id = 1, X = 112, Y = 100, Radius = 8, TypeId = 0 }
        };
        var hash = new SpatialHash(32.0);
        var mechanics = new CellMechanics
        {
            Iterations = 8,
            Stiffness = 0.9,
            MaxOverlap = 0.4,
            Adhesion = (a, b) => 1.0
        };

        for (int i = 0; i < 60; i++)
        {
            hash.Rebuild(cells);
            mechanics.Resolve(cells, hash, 16.0);
        }

        double dist = cells[1].X - cells[0].X;
        double touch = cells[0].Radius + cells[1].Radius;
        double target = touch * (1.0 - 0.4);

        Assert.That(dist, Is.LessThan(touch), "Cohesive cells should be able to stay closer than touching.");
        Assert.That(dist, Is.GreaterThanOrEqualTo(target * 0.9), "They must still respect the adhesion-restricted rest distance.");
    }

    [Test]
    public void Adhesion_SeparatesFully_WhenStrengthIsZero()
    {
        var cells = new List<Cell>
        {
            new() { Id = 0, X = 100, Y = 100, Radius = 8, TypeId = 0 },
            new() { Id = 1, X = 104, Y = 100, Radius = 8, TypeId = 0 }
        };
        var hash = new SpatialHash(32.0);
        var mechanics = new CellMechanics
        {
            Iterations = 8,
            Stiffness = 0.9,
            Adhesion = (a, b) => 0.0
        };

        for (int i = 0; i < 20; i++)
        {
            hash.Rebuild(cells);
            mechanics.Resolve(cells, hash, 16.0);
        }

        double dist = cells[1].X - cells[0].X;
        double touch = cells[0].Radius + cells[1].Radius;
        Assert.That(dist, Is.GreaterThanOrEqualTo(touch - 0.5), "Zero adhesion must still resolve to full separation.");
    }

    [Test]
    public void Adhesion_NoOscillation_UnderRepeatedResolution()
    {
        // With adhesion the system must converge, not jitter between overlaps.
        var cells = new List<Cell>
        {
            new() { Id = 0, X = 100, Y = 100, Radius = 8, TypeId = 0 },
            new() { Id = 1, X = 110, Y = 100, Radius = 8, TypeId = 1 }
        };
        var hash = new SpatialHash(32.0);
        var mechanics = new CellMechanics
        {
            Iterations = 4,
            Stiffness = 0.7,
            MaxOverlap = 0.4,
            Adhesion = (a, b) => 0.8
        };

        double prev = 0;
        bool converged = true;
        for (int i = 0; i < 30; i++)
        {
            hash.Rebuild(cells);
            mechanics.Resolve(cells, hash, 16.0);
            double dist = cells[1].X - cells[0].X;
            if (i > 10 && Math.Abs(dist - prev) > 0.5) converged = false;
            prev = dist;
        }
        Assert.That(converged, Is.True, "Adhesive pairs must settle without oscillation.");
    }

    // --- Directed division --------------------------------------------------

    [Test]
    public void DirectedDivision_PrefersGradientDirection()
    {
        // Morphogen field rises to the right; the child should appear on the +x side.
        var setup = TestFactory.Setup(
            morphogenCount: 1,
            diffusion: 0.0,
            decay: 0.0,
            world: 512.0,
            resolution: 16.0,
            initialCells: 1,
            genome: TestFactory.Gene("div", TestFactory.Divide(100.0)));
        setup.Simulation.DivisionGradientMorphogen = 0;

        var sim = new Simulation(setup);

        // Build a field that increases along +x: value(x) = x.
        for (int gx = 0; gx < sim.Morphogens.GridWidth; gx++)
            for (int gy = 0; gy < sim.Morphogens.GridHeight; gy++)
                sim.Morphogens.SetField(0, gx, gy, gx * 16.0);

        double startX = sim.Cells[0].X;
        sim.Step(0.05);

        Assert.That(sim.Cells.Count, Is.EqualTo(2));
        Cell child = sim.Cells[1].Id > sim.Cells[0].Id ? sim.Cells[1] : sim.Cells[0];
        Assert.That(child.X, Is.GreaterThan(startX), "Child should be offset towards the morphogen gradient.");
    }

    [Test]
    public void DirectedDivision_StableOnFlatGradient()
    {
        // No gradient -> must not produce NaN or pick an invalid direction.
        var setup = TestFactory.Setup(
            morphogenCount: 1,
            diffusion: 0.0,
            initialCells: 1,
            genome: TestFactory.Gene("div", TestFactory.Divide(100.0)));
        setup.Simulation.DivisionGradientMorphogen = 0;

        var sim = new Simulation(setup);

        sim.Step(0.05);

        Assert.That(sim.Cells.Count, Is.EqualTo(2));
        foreach (var c in sim.Cells)
        {
            Assert.That(double.IsNaN(c.X) || double.IsNaN(c.Y), Is.False, "Cell position must stay finite.");
            Assert.That(double.IsInfinity(c.X) || double.IsInfinity(c.Y), Is.False);
        }
    }

    // --- Regulation state inheritance ---------------------------------------

    [Test]
    public void Division_InheritsGeneState_AndRegulation()
    {
        // Gene activates instantly; after division the child copies the parent's
        // active gene state, its silenced-gene set, timers and shared genome.
        var always = new GeneConfig
        {
            Id = "on",
            Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 } },
            Effects = { TestFactory.Emit(0, 1) }
        };
        var victim = new GeneConfig
        {
            Id = "victim",
            Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 } },
            Effects = { TestFactory.Emit(0, 1) }
        };
        var silencer = new GeneConfig
        {
            Id = "s",
            Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 } },
            Effects =
            {
                new GeneEffectConfig { Kind = "SilenceGene", Gene = "victim", Once = true },
                new GeneEffectConfig { Kind = "Divide", Rate = 10.0 }
            }
        };
        var setup = TestFactory.Setup(morphogenCount: 1, initialCells: 1, genome: new[] { victim, always, silencer });
        setup.Simulation.DivisionGradientMorphogen = -1;

        var sim = new Simulation(setup);
        var parent = sim.Cells[0];

        // Step 1 activates the genes; the silencer silences 'victim' and begins division.
        sim.Step(0.05);
        Assert.That(parent.IsSilenced("victim"), Is.True);
        Assert.That(parent.StateOf("victim").Active, Is.False, "Silencing must take effect immediately.");
        Assert.That(parent.StateOf("on").Active, Is.True);
        Assert.That(sim.Cells.Count, Is.EqualTo(1), "Division needs a second step at this rate.");

        // Step 2 completes division.
        sim.Step(0.05);
        Assert.That(sim.Cells.Count, Is.EqualTo(2), "Silencer's Divide effect should have completed division.");

        var child = sim.Cells[1];
        Assert.That(child.IsSilenced("victim"), Is.True, "Child must inherit the silenced-gene set.");
        Assert.That(child.StateOf("victim").Active, Is.False, "A silenced gene must stay inactive in the child.");
        Assert.That(child.StateOf("on").Active, Is.True, "Child must inherit active gene state.");
        Assert.That(child.GenomeId, Is.EqualTo(parent.GenomeId), "Child must reference the same shared genome.");
    }

    // --- Shared genome / phenotype separation --------------------------------

    [Test]
    public void PhenotypeChange_DoesNotAlterSharedGenome()
    {
        var setup = TestFactory.Setup(
            morphogenCount: 1,
            initialCells: 1,
            genome: TestFactory.Gene("g", TestFactory.Emit(0, 1)));
        var sim = new Simulation(setup);

        var genomeBefore = sim.GenomeOf(sim.Cells[0].GenomeId);
        sim.Cells[0].TypeId = 5; // arbitrary phenotype switch
        sim.Step(0.05);
        var genomeAfter = sim.GenomeOf(sim.Cells[0].GenomeId);

        Assert.That(ReferenceEquals(genomeBefore, genomeAfter), Is.True,
            "Phenotype changes must not create or replace the shared genome.");
        Assert.That(genomeBefore, Is.Not.Null);
    }

    // --- Multiple morphogens --------------------------------------------------

    [Test]
    public void MultipleMorphogens_FieldsAreIndependent()
    {
        var system = new MorphogenSystem(128.0, 128.0, 8.0, 3);
        system.SetField(0, 8, 8, 50.0);

        var configs = new Dictionary<int, MorphogenConfig>
        {
            [0] = new() { Id = 0, Diffusion = 10.0, Decay = 0.0 },
            [1] = new() { Id = 1, Diffusion = 10.0, Decay = 0.0 },
            [2] = new() { Id = 2, Diffusion = 0.0, Decay = 0.0 }
        };

        system.Emit(0, 64, 64, 20.0);
        system.Update(1.0, configs);

        Assert.That(system.TotalMass(1), Is.EqualTo(0.0).Within(1e-9), "Field 1 must stay untouched.");
        Assert.That(system.Gradient(1, 64, 64), Is.EqualTo((0.0, 0.0)), "Zero field -> zero gradient.");
        Assert.That(system.TotalMass(0), Is.GreaterThan(0.0), "Field 0 received its own source.");
        Assert.That(system.Sample(0, 64, 64), Is.GreaterThan(0.0));
    }

    [Test]
    public void MorphogenCount_IsDrivenByHighestConfiguredId()
    {
        var sim = new SimulationConfig { WorldWidth = 256, WorldHeight = 256, MorphogenResolution = 16, RngSeed = 1 };
        var morphogens = new[]
        {
            new MorphogenConfig { Id = 0, Name = "a" },
            new MorphogenConfig { Id = 5, Name = "b" }
        };
        var setup = new SimulationSetup(
            sim, morphogens,
            new[] { new GenomeConfig { Id = 0 } },
            new[] { new CellTypeConfig { Id = 0, GenomeId = 0 } });

        var s = new Simulation(setup);
        Assert.That(s.Morphogens.MorphogenCount, Is.EqualTo(6), "Count must span up to the largest configured id.");
        Assert.That(s.SampleMorphogen(5, 128, 128), Is.EqualTo(0.0).Within(1e-12));
        Assert.That(s.SampleMorphogen(7, 128, 128), Is.EqualTo(0.0).Within(1e-12), "Out-of-range id returns 0.");
    }
}
