using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

/// <summary>
/// M1.5+: morphogen management in the working draft (creation, copy, delete with
/// dependency cleanup) plus the validator regressions around the new "Divide"
/// child phenotype and numeric NaN guards.
/// </summary>
[TestFixture]
public class MorphogenManagementTests
{
    private static GeneConfig GeneReg(string id, int morphogen, bool regulator = true)
    {
        var g = TestFactory.Gene(id);
        if (regulator)
            g.Regulators.Add(new RegulatorConfig { Kind = "Morphogen", Morphogen = morphogen, Min = 0.0 });
        return g;
    }

    [Test]
    public void RemoveMorphogen_RemovesEffectsConditionsAndGradient()
    {
        var setup = TestFactory.Setup(morphogenCount: 2, genome: new[]
        {
            TestFactory.Gene("sent", TestFactory.Emit(0, 1.0)),
            TestFactory.Gene("taken", TestFactory.Absorb(0, 1.0)),
            GeneReg("gated", 0),
            TestFactory.Gene("untouched", TestFactory.Emit(1, 1.0))
        });
        setup.Simulation.DivisionGradientMorphogen = 0;

        var affected = ConfigDraft.RemoveMorphogen(setup, 0);

        Assert.That(affected, Is.EquivalentTo(new[] { "ген «sent»", "ген «taken»", "ген «gated»" }));
        Assert.That(setup.Morphogens.Select(m => m.Id), Is.EqualTo(new[] { 1 }));
        Assert.That(setup.Genomes[0].Genes.First(g => g.Key == "sent").Effects, Is.Empty);
        Assert.That(setup.Genomes[0].Genes.First(g => g.Key == "taken").Effects, Is.Empty);
        Assert.That(setup.Genomes[0].Genes.First(g => g.Key == "gated").Regulators, Is.Empty);
        Assert.That(setup.Genomes[0].Genes.First(g => g.Key == "untouched").Effects.Select(e => e.Morphogen), Is.EqualTo(new[] { 1 }),
            "Unrelated genes are not modified.");
        Assert.That(setup.Simulation.DivisionGradientMorphogen, Is.EqualTo(-1),
            "The division-gradient selector is reset when the morphogen is removed.");
    }

    [Test]
    public void RemoveMorphogen_DropsEmptyRegulatorGroups()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: new[]
        {
            new GeneConfig
            {
                Id = "grouped",
                RegulatorGroups = new List<RegulatorGroupConfig>
                {
                    new() { Operator = "And", Regulators = { new RegulatorConfig { Kind = "Property", Property = "energy", Min = 1.0 } } },
                    new() { Operator = "Or", Regulators = { new RegulatorConfig { Kind = "Morphogen", Morphogen = 0 } } }
                },
                Effects = { TestFactory.Emit(0, 1.0) }
            }
        });

        ConfigDraft.RemoveMorphogen(setup, 0);

        var gene = setup.Genomes[0].Genes[0];
        Assert.That(gene.RegulatorGroups!.Count, Is.EqualTo(1), "The group left without conditions is dropped.");
        Assert.That(gene.RegulatorGroups![0].Regulators.Count, Is.EqualTo(1));
        Assert.That(gene.RegulatorGroups[0].Regulators[0].Kind, Is.EqualTo("Property"));
    }

    [Test]
    public void FindCellTypeReferences_IncludesDivideChildPhenotype()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: TestFactory.Gene("splitter", TestFactory.Divide(1, 1.0)));
        var refs = ConfigDraft.FindCellTypeReferences(setup, 1);

        Assert.That(refs, Is.EquivalentTo(new[] { "ген «splitter»" }),
            "A Divide child phenotype counts as a reference to the phenotype.");
    }

    [Test]
    public void FindMorphogenReferences_IncludesDivisionGradient()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: Array.Empty<GeneConfig>());
        setup.Simulation.DivisionGradientMorphogen = 0;

        var refs = ConfigDraft.FindMorphogenReferences(setup, 0);

        Assert.That(refs, Is.EquivalentTo(new[] { "эксперимент (морфоген направления деления)" }));
    }

    [Test]
    public void CopyMorphogen_CreatesUniqueIdAndPreservesParameters()
    {
        var setup = TestFactory.Setup(morphogenCount: 2);
        setup.Morphogens[1].DisplayName = "Фактор роста";
        setup.Morphogens[1].Description = "Описание";
        setup.Morphogens[1].Diffusion = 1.5;
        setup.Morphogens[1].Decay = 0.3;
        setup.Morphogens[1].Color = "#ffcc00";

        var copy = ConfigDraft.CopyMorphogen(setup, setup.Morphogens[1]);

        Assert.That(copy.Id, Is.EqualTo(2));
        Assert.That(setup.Morphogens.Select(m => m.Id), Is.Not.Contains(copy.Id),
            "The copy gets an id not yet used in the working set.");
        Assert.That(copy.Name, Is.EqualTo(setup.Morphogens[1].Name));
        Assert.That(copy.Diffusion, Is.EqualTo(1.5));
        Assert.That(copy.Decay, Is.EqualTo(0.3));
        Assert.That(copy.Color, Is.EqualTo("#ffcc00"));
        Assert.That(copy.Description, Is.EqualTo("Описание"));
    }

    [Test]
    public void UniqueMorphogenId_SkipsUsedIds()
    {
        var setup = TestFactory.Setup(morphogenCount: 3); // ids 0,1,2
        Assert.That(ConfigDraft.UniqueMorphogenId(setup, 2), Is.EqualTo(3));
        Assert.That(ConfigDraft.UniqueMorphogenId(setup, 0), Is.EqualTo(3));
    }

    [Test]
    public void LegacyDivideEffect_SerializesWithoutChildTypeId()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sim2_legacy_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var setup = TestFactory.Setup(genome: TestFactory.Gene("div", TestFactory.Divide(1.0)));
            ConfigWriter.SaveSetupToDirectory(dir, setup);

            string json = File.ReadAllText(Path.Combine(dir, "genomes.json"));
            Assert.That(json, Does.Not.Contain("childTypeId"),
                "Legacy Divide effects must not gain a field on save (null values are omitted).");

            var setup2 = TestFactory.Setup(genome: TestFactory.Gene("div", TestFactory.Divide(1, 1.0)));
            ConfigWriter.SaveSetupToDirectory(dir, setup2);
            string json2 = File.ReadAllText(Path.Combine(dir, "genomes.json"));
            Assert.That(json2, Does.Contain("childTypeId"), "Explicit child phenotypes are persisted.");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void DivideWithMissingChildPhenotype_IsAnError()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: TestFactory.Gene("div", TestFactory.Divide(7, 1.0)));
        var issues = ConfigValidator.Validate(setup);

        Assert.That(issues.Count(i => i.IsError && i.Message.Contains("несуществующий фенотип потомка")), Is.EqualTo(1));
    }

    [Test]
    public void ConflictingDivideEffects_AreFlaggedByValidator()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: TestFactory.Gene("mixed",
            TestFactory.Divide(1, 1.0), TestFactory.Divide(null, 1.0)));
        var issues = ConfigValidator.Validate(setup);

        Assert.That(issues.Count(i => i.IsError && i.Message.Contains("разными фенотипами потомка")), Is.EqualTo(1));
    }

    [Test]
    public void SeparateDivideGenesWithDifferentPhenotypes_AreValid()
    {
        // Different rules in different genes are fine — only one gene is active
        // at a time for a given cell.
        var setup = TestFactory.Setup(morphogenCount: 1, genome: new[]
        {
            TestFactory.Gene("a", TestFactory.Divide(1, 1.0)),
            TestFactory.Gene("b", TestFactory.Divide(2, 1.0))
        });
        TestFactory.AddType(setup, 1, "worker");
        TestFactory.AddType(setup, 2, "skin");

        var issues = ConfigValidator.Validate(setup);
        Assert.That(ConfigValidator.HasErrors(issues), Is.False);
    }

    [Test]
    public void NaN_EffectRate_IsAnError()
    {
        var setup = TestFactory.Setup(genome: TestFactory.Gene("g",
            new GeneEffectConfig { Kind = "Divide", Rate = double.NaN }));
        var issues = ConfigValidator.Validate(setup);

        Assert.That(issues.Any(i => i.IsError && i.Message.Contains("должна быть числом")), Is.True);
    }

    [Test]
    public void NaN_MorphogenParameters_AreAnError()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: TestFactory.Gene("g", TestFactory.Emit(0, 1)));
        setup.Morphogens[0].Diffusion = double.NaN;

        var issues = ConfigValidator.Validate(setup);
        Assert.That(issues.Any(i => i.IsError && i.Message.Contains("диффузии")), Is.True);
    }

    [Test]
    public void NaN_RegulatorBounds_AreAnError()
    {
        var g = TestFactory.Gene("g", TestFactory.Emit(0, 1));
        g.Regulators.Add(new RegulatorConfig { Kind = "Morphogen", Morphogen = 0, Min = double.NaN });
        var issues = ConfigValidator.Validate(TestFactory.Setup(morphogenCount: 1, genome: g));

        Assert.That(issues.Any(i => i.IsError && i.Message.Contains("NaN")), Is.True);
    }

    [Test]
    public void LegacyDivisionGradientMorphogen_0_IsValid()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: TestFactory.Gene("g", TestFactory.Emit(0, 1)));
        setup.Simulation.DivisionGradientMorphogen = 0;

        var issues = ConfigValidator.Validate(setup);
        Assert.That(ConfigValidator.HasErrors(issues), Is.False,
            "A legacy config pointing the division gradient at morphogen 0 stays valid.");
    }

    [Test]
    public void DivisionGradientMorphogen_Missing_IsAnError()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: TestFactory.Gene("g", TestFactory.Emit(0, 1)));
        setup.Simulation.DivisionGradientMorphogen = 9;

        var issues = ConfigValidator.Validate(setup);
        Assert.That(issues.Any(i => i.IsError
            && (i.Location?.Contains("divisionGradientMorphogen") == true
                || i.Message.Contains("divisionGradientMorphogen"))), Is.True);
    }
}