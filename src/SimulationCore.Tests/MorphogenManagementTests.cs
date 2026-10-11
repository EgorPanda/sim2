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
            TestFactory.Gene("moved", new GeneEffectConfig { Kind = "Move", Morphogen = 0, Rate = 1.0 }),
            GeneReg("gated", 0),
            TestFactory.Gene("untouched", TestFactory.Emit(1, 1.0))
        });
        setup.Simulation.DivisionGradientMorphogen = 0;

        var affected = ConfigDraft.RemoveMorphogen(setup, 0);

        Assert.That(affected, Is.EquivalentTo(new[]
        {
            "ген «sent»", "ген «moved»", "ген «gated»", "эксперимент (морфоген направления деления)"
        }));
        Assert.That(setup.Morphogens.Select(m => m.Id), Is.EqualTo(new[] { 1 }));
        Assert.That(setup.Genomes[0].Genes.First(g => g.Key == "sent").Effects, Is.Empty);
        Assert.That(setup.Genomes[0].Genes.First(g => g.Key == "moved").Effects, Is.Empty);
        Assert.That(setup.Genomes[0].Genes.First(g => g.Key == "gated").Regulators, Is.Empty);
        Assert.That(setup.Genomes[0].Genes.First(g => g.Key == "untouched").Effects.Select(e => e.Morphogen), Is.EqualTo(new[] { 1 }),
            "Unrelated genes are not modified.");
        Assert.That(setup.Simulation.DivisionGradientMorphogen, Is.EqualTo(-1),
            "The division-gradient selector is reset when the morphogen is removed.");
    }

    [Test]
    public void RemoveMorphogen_CleansInitialSignals()
    {
        var setup = TestFactory.Setup(morphogenCount: 2, genome: Array.Empty<GeneConfig>());
        setup.Simulation.InitialExternalSources.Add(new MorphogenSourceConfig
        {
            MorphogenId = 0,
            X = 10, Y = 10, Radius = 20.0, Concentration = 1.5,
            Profile = "uniform"
        });
        setup.Simulation.InitialExternalSources.Add(new MorphogenSourceConfig
        {
            MorphogenId = 1,
            X = 40, Y = 40, Radius = 20.0, Concentration = 0.5,
            Profile = "uniform"
        });
        var affected = ConfigDraft.RemoveMorphogen(setup, 0);

        Assert.That(affected, Does.Contain("начальные внешние источники"));
        Assert.That(setup.Simulation.InitialExternalSources.Select(s => s.MorphogenId),
            Is.EqualTo(new[] { 1 }), "Only references to the deleted morphogen are removed.");

        var issues = ConfigValidator.Validate(setup);
        Assert.That(issues.Where(i => i.IsError), Is.Empty,
            "After the cleanup the configuration must validate without errors.");
    }

    [Test]
    public void ReconcileInitialSignals_MatchesMorphogenType()
    {
        var setup = TestFactory.Setup(morphogenCount: 2, genome: Array.Empty<GeneConfig>());
        setup.Simulation.InitialExternalSources.Add(new MorphogenSourceConfig
        {
            MorphogenId = 0,
            X = 10, Y = 10, Radius = 20.0, Concentration = 1.0,
            Profile = "uniform"
        });
        // Morphogen 0 becomes internal: its external source must go.
        setup.Morphogens[0].Type = "Internal";
        ConfigDraft.ReconcileInitialSignals(setup, 0);
        Assert.That(setup.Simulation.InitialExternalSources.Select(s => s.MorphogenId), Is.Empty);

        var issues = ConfigValidator.Validate(setup);
        Assert.That(issues.Where(i => i.IsError), Is.Empty);
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
                    new() { Operator = "And", Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 1.0 } } },
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
        setup.Morphogens[1].Name = "Фактор роста";
        setup.Morphogens[1].Role = "роль";
        setup.Morphogens[1].Description = "Описание";
        setup.Morphogens[1].Diffusion = 1.5;
        setup.Morphogens[1].Decay = 0.3;
        setup.Morphogens[1].Color = "#ffcc00";

        var copy = ConfigDraft.CopyMorphogen(setup, setup.Morphogens[1]);

        Assert.That(copy.Id, Is.EqualTo(2));
        Assert.That(setup.Morphogens.Select(m => m.Id), Is.Not.Contains(copy.Id),
            "The copy gets an id not yet used in the working set.");
        Assert.That(copy.Name, Is.EqualTo("Фактор роста (копия)"));
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

    // --- Genome management ---------------------------------------------------

    [Test]
    public void NewGenome_GetsUniqueId_AndStarterGene()
    {
        var setup = TestFactory.Setup(morphogenCount: 1);

        var genome = ConfigDraft.NewGenome(setup);
        setup.Genomes = setup.Genomes.Append(genome).ToArray();

        Assert.That(genome.Id, Is.EqualTo(1));
        Assert.That(genome.Genes, Has.Count.EqualTo(1));
        var issues = ConfigValidator.Validate(setup);
        Assert.That(issues.Where(i => i.IsError), Is.Empty,
            "A freshly created genome must validate cleanly.");
    }

    [Test]
    public void CopyGenome_DeepCopiesGenes_IncludingRepel()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: new[]
        {
            TestFactory.Gene("move", new GeneEffectConfig { Kind = "Move", Morphogen = 0, Rate = 3.0, Repel = true }),
            TestFactory.Gene("emit", TestFactory.Emit(0, 5.0))
        });
        var source = setup.Genomes[0];

        var copy = ConfigDraft.CopyGenome(setup, source);
        setup.Genomes = setup.Genomes.Append(copy).ToArray();

        Assert.That(copy.Id, Is.EqualTo(1));
        Assert.That(copy.Genes.Count, Is.EqualTo(source.Genes.Count));
        Assert.That(copy.Genes[0].Effects[0].Repel, Is.True,
            "The Repel flag must survive genome copy.");
        Assert.That(ReferenceEquals(copy.Genes[0], source.Genes[0]), Is.False,
            "Genome copy must deep-copy genes.");

        // Editing the copy must not leak into the source.
        copy.Genes[0].Effects[0].Repel = false;
        Assert.That(source.Genes[0].Effects[0].Repel, Is.True);
    }

    [Test]
    public void RemoveGenome_BlocksLastGenomeAndReferencedGenome()
    {
        var setup = TestFactory.Setup(morphogenCount: 1);
        var only = setup.Genomes[0];

        Assert.That(ConfigDraft.RemoveGenome(setup, only), Is.EqualTo("нельзя удалить последний геном"));
        Assert.That(setup.Genomes, Has.Length.EqualTo(1));

        var other = ConfigDraft.NewGenome(setup);
        setup.Genomes = setup.Genomes.Append(other).ToArray();

        // Referenced by a cell type -> blocked.
        Assert.That(ConfigDraft.RemoveGenome(setup, only), Is.EqualTo("на геном ссылаются типы клеток"));
        Assert.That(setup.Genomes, Has.Length.EqualTo(2));

        // Unreferenced -> removable.
        Assert.That(ConfigDraft.RemoveGenome(setup, other), Is.Null);
        Assert.That(setup.Genomes, Has.Length.EqualTo(1));
    }

    [Test]
    public void SanitizeMorphogenReferences_HealsDanglingExternalReferences()
    {
        // A profile like the user's ex1: gradient points at morphogen 0 which was
        // deleted; initial signals may still point at it.
        var setup = TestFactory.Setup(morphogenCount: 1, genome: TestFactory.Gene("g", TestFactory.Emit(1, 1.0)));
        setup.Morphogens = new[] { new MorphogenConfig { Id = 1, Name = "m1", Diffusion = 1.0, Decay = 0.1 } };
        setup.Simulation.DivisionGradientMorphogen = 0;
        setup.Simulation.InitialExternalSources.Add(new MorphogenSourceConfig
        {
            MorphogenId = 0,
            X = 10, Y = 10, Radius = 20, Concentration = 1.0,
            Profile = "uniform"
        });
        ConfigDraft.SanitizeMorphogenReferences(setup);

        Assert.That(setup.Simulation.DivisionGradientMorphogen, Is.EqualTo(-1));
        Assert.That(setup.Simulation.InitialExternalSources, Is.Empty);

        var issues = ConfigValidator.Validate(setup);
        Assert.That(issues.Where(i => i.IsError), Is.Empty,
            "After sanitizing, a hand-edited configuration must validate cleanly.");
    }

    [Test]
    public void SanitizeMorphogenReferences_KeepsValidReferences()
    {
        var setup = TestFactory.Setup(morphogenCount: 2, genome: TestFactory.Gene("g", TestFactory.Emit(0, 1.0)));
        setup.Morphogens[1].Type = "Internal";
        setup.Simulation.DivisionGradientMorphogen = 0;
        setup.Simulation.InitialExternalSources.Add(new MorphogenSourceConfig
        {
            MorphogenId = 0,
            X = 10, Y = 10, Radius = 20, Concentration = 1.0,
            Profile = "uniform"
        });
        ConfigDraft.SanitizeMorphogenReferences(setup);

        Assert.That(setup.Simulation.DivisionGradientMorphogen, Is.EqualTo(0));
        Assert.That(setup.Simulation.InitialExternalSources, Has.Count.EqualTo(1));
    }
}
