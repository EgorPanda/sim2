using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

/// <summary>
/// M1.5+: daughters born with an explicit child phenotype ("Divide" effect's
/// ChildTypeId) differentiate the tissue during division, with the parent keeping
/// its own phenotype. Legacy effects without the field inherit the parent type.
/// </summary>
[TestFixture]
public class CellDifferentiationTests
{
    private static GeneEffectConfig Div(int? childTypeId, double rate) => TestFactory.Divide(childTypeId, rate);

    private static SimulationSetup TwoTypeSetup()
    {
        var workerGene = TestFactory.Gene("div_worker", Div(1, 100.0));
        workerGene.Regulators.Add(new RegulatorConfig { Kind = "Morphogen", Morphogen = 0, Min = 0.5 });
        var skinGene = TestFactory.Gene("div_skin", Div(2, 100.0));
        skinGene.Regulators.Add(new RegulatorConfig { Kind = "Morphogen", Morphogen = 0, Max = 0.5 });

        var setup = TestFactory.Setup(
            morphogenCount: 1,
            initialCells: 1,
            genome: new[] { workerGene, skinGene });
        TestFactory.AddType(setup, 1, "worker", "#4ea8de");
        TestFactory.AddType(setup, 2, "skin", "#b09bd1");
        return setup;
    }

    private static void SeedHalfPlane(Simulation sim, SimulationSetup setup, double high, double low)
    {
        // Left half of the world is "high signal", right half is "low". A whole
        // half-plane is seeded so bilinear interpolation between the two zones
        // stays flat within each half and small cell drift cannot flip the zone.
        int gw = (int)(setup.Simulation.WorldWidth / setup.Simulation.MorphogenResolution);
        for (int gy = 0; gy < gw; gy++)
            for (int gx = 0; gx < gw; gx++)
                sim.Morphogens.SetField(0, gx, gy, gx < gw / 2 ? high : low);
    }

    private static Simulation SetupGradient(double high, double low)
    {
        var setup = TwoTypeSetup();
        var sim = new Simulation(setup);

        // Two stem founders well inside their zones: left in high, right in low.
        sim.Cells.Clear();
        sim.AddCell(60.0, 60.0, 0);
        sim.AddCell(228.0, 60.0, 0);
        sim.Rng.SetSeed(42);

        SeedHalfPlane(sim, setup, high, low);

        return sim;
    }

    [Test]
    public void LegacyDivide_NoChildType_DaughterInheritsParentPhenotype()
    {
        var setup = TestFactory.Setup(
            initialCells: 1,
            genome: TestFactory.Gene("div", TestFactory.Divide(100.0)));
        var sim = new Simulation(setup);
        int parentType = sim.Cells[0].TypeId;

        sim.Step(0.05);

        Assert.That(sim.Cells.Count, Is.EqualTo(2));
        Assert.That(sim.Cells.Any(c => c.TypeId == parentType), Is.True,
            "Both daughter cells keep the parent phenotype without an explicit child type.");
        var diag = sim.CellSystem.DivisionLog.Single();
        Assert.That(diag.Inherited, Is.True, "Legacy division is marked as inherited.");
        Assert.That(diag.ChildTypeId, Is.EqualTo(parentType));
        Assert.That(diag.RuleGeneId, Is.EqualTo("div"));
    }

    [Test]
    public void DivideWithChildType_DaughterGetsExplicitPhenotype_ParentKeepsOwn()
    {
        var setup = TestFactory.Setup(
            initialCells: 1,
            genome: TestFactory.Gene("div", Div(1, 100.0)));
        TestFactory.AddType(setup, 1, "worker");
        var sim = new Simulation(setup);
        int parentType = sim.Cells[0].TypeId;

        sim.Step(0.05);

        Assert.That(sim.Cells.Count, Is.EqualTo(2));
        Assert.That(sim.Cells.Any(c => c.TypeId == 1), Is.True,
            "Daughter is born as the requested phenotype.");
        var diag = sim.CellSystem.DivisionLog.Single();
        Assert.That(diag.Inherited, Is.False);
        Assert.That(diag.ParentTypeId, Is.EqualTo(parentType));
        Assert.That(diag.ChildTypeId, Is.EqualTo(1));
        Assert.That(diag.RuleGeneId, Is.EqualTo("div"));
        Assert.That(diag.GenomeId, Is.EqualTo(0));
    }

    [Test]
    public void DivisionIntent_IsCleared_AfterDivision()
    {
        var setup = TestFactory.Setup(
            initialCells: 1,
            genome: TestFactory.Gene("div", Div(1, 100.0)));
        var sim = new Simulation(setup);
        int parentId = sim.Cells[0].Id;

        sim.Step(0.05);

        var parent = sim.Cells.First(c => c.Id == parentId);
        Assert.That(parent.PendingChildTypeId, Is.Null, "The per-step intent is cleared when the division is resolved.");
        Assert.That(parent.PendingDivideGene, Is.Null);
    }

    [Test]
    public void GradientDifferentiation_BirthsWorkerAndSkinChildren_Deterministically()
    {
        var sim = SetupGradient(high: 5.0, low: 0.0);

        sim.Step(0.05);

        Assert.That(sim.Cells.Count, Is.EqualTo(4), "Both stem cells divide once in the seeded field.");

        var log = sim.CellSystem.DivisionLog.ToList();
        Assert.That(log.Count, Is.EqualTo(2), "Two divisions are recorded.");
        Assert.That(log.Select(d => d.RuleGeneId), Is.EquivalentTo(new[] { "div_worker", "div_skin" }),
            "Each zone applies its own division rule.");

        foreach (var diag in log)
        {
            var child = sim.Cells.First(c => c.Id == diag.ChildId);
            Assert.That(child.TypeId, Is.EqualTo(diag.ChildTypeId));
            Assert.That(child.Radius, Is.GreaterThan(0.0), "A born daughter is a full cell.");
            Assert.That(diag.ParentTypeId, Is.EqualTo(0), "The stem parent itself did not differentiate.");
        }

        var worker = sim.Cells.First(c => c.TypeId == 1);
        var skin = sim.Cells.First(c => c.TypeId == 2);
        Assert.That(worker, Is.Not.Null);
        Assert.That(skin, Is.Not.Null);
    }

    [Test]
    public void Daughters_ParticipateFully_AndDivideUnderTheirOwnZone()
    {
        // High-signal zone: a stem child keeps dividing and every division is
        // governed by the local rule (worker), so the colony is tissue-like.
        var sim = SetupGradient(high: 5.0, low: 5.0);

        sim.Step(0.05);
        sim.Step(0.05);

        Assert.That(sim.Cells.Count, Is.GreaterThan(2));
        var log = sim.CellSystem.DivisionLog.ToList();
        Assert.That(log.Select(d => d.RuleGeneId).Distinct(), Is.EqualTo(new[] { "div_worker" }),
            "All divisions in the high-signal colony follow the worker rule.");
        foreach (var diag in log)
            Assert.That(diag.ChildTypeId, Is.EqualTo(1));
    }

    [Test]
    public void LastAppliedDivideEffect_Wins_Deterministically()
    {
        // Two Divide effects in one gene disagree about the child phenotype.
        // The runtime resolves deterministically (last effect wins); the config
        // validator simultaneously rejects such a gene.
        var setup = TestFactory.Setup(
            initialCells: 1,
            genome: new GeneConfig
            {
                Id = "mixed",
                Effects =
                {
                    Div(2, 100.0),
                    Div(1, 100.0)
                }
            });
        TestFactory.AddType(setup, 1, "worker");
        TestFactory.AddType(setup, 2, "skin");
        var sim = new Simulation(setup);

        sim.Step(0.05);

        var child = sim.Cells.First(c => c.Id != sim.Cells[0].Id);
        Assert.That(child.TypeId, Is.EqualTo(1), "The last-applied Divide effect wins (deterministic order).");
        Assert.That(sim.CellSystem.DivisionLog.Single().ChildTypeId, Is.EqualTo(1));

        var issues = ConfigValidator.Validate(setup);
        Assert.That(issues.Any(i => i.IsError && i.Message.Contains("разными фенотипами потомка")), Is.True,
            "Conflicting divide effects in one gene are flagged by the validator.");
    }

    [Test]
    public void MissingChildPhenotypeInCellTypes_FallsBackToParent()
    {
        // Core safety: if the config was edited between apply and run so the
        // child phenotype no longer exists, the division must not crash and the
        // daughter inherits the parent type instead.
        var setup = TestFactory.Setup(
            initialCells: 1,
            genome: TestFactory.Gene("div", Div(1, 100.0)));
        var sim = new Simulation(setup);

        sim.Step(0.05);

        Assert.That(sim.Cells.Count, Is.EqualTo(2), "Division still happens; the fallback keeps the world consistent.");
        Assert.That(sim.Cells.All(c => c.TypeId == 0), Is.True,
            "When the requested child phenotype is missing the daughter inherits the parent type.");
    }
}