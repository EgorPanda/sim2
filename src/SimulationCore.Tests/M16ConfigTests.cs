namespace SimulationCore.Tests;

using NUnit.Framework;

[TestFixture]
public class M16ConfigTests
{
    [Test]
    public void MorphogenType_ExternalByDefault_MigratesOldConfigs()
    {
        string json = "[{ \"id\": 0, \"name\": \"old\" }]";
        var morphogens = ConfigLoader.ParseMorphogens(json);
        Assert.That(morphogens[0].IsExternal, Is.True);
    }

    [Test]
    public void MorphogenType_Internal_IsParsed()
    {
        string json = "[{ \"id\": 0, \"name\": \"mem\", \"type\": \"Internal\" }]";
        var morphogens = ConfigLoader.ParseMorphogens(json);
        Assert.That(morphogens[0].IsInternal, Is.True);
    }

    [Test]
    public void Validator_FlagsInternalMorphogenDiffusion()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[]
        {
            new MorphogenConfig { Id = 0, Type = "Internal", Diffusion = 5.0, Decay = 0.1 }
        };
        var issues = ConfigValidator.Validate(setup);
        Assert.That(issues.Any(i => i.Section == "Морфогены" && i.Message.Contains("диффузии")), Is.True);
    }

    [Test]
    public void Validator_FlagsMoveOfInternalMorphogen()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[] { new MorphogenConfig { Id = 0, Type = "Internal" } };
        setup.Genomes = new[]
        {
            new GenomeConfig
            {
                Id = 0,
                Genes = new()
                {
                    new()
                    {
                        Id = "bad",
                        Effects = new() { new() { Kind = "Move", Morphogen = 0, Rate = 1.0 } }
                    }
                }
            }
        };
        setup.CellTypes = new[] { new CellTypeConfig { Id = 0, GenomeId = 0 } };

        var issues = ConfigValidator.Validate(setup);
        Assert.That(issues.Any(i => i.Section == "Геномы" && i.Message.Contains("внутреннего морфогена не поддерживается")), Is.True);
    }

    [Test]
    public void Validator_FlagsGeneWithMissingRequiredTrigger()
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
                        Id = "broken",
                        RequireMorphogenTrigger = true,
                        RequiredMorphogenId = -1
                    }
                }
            }
        };
        setup.CellTypes = new[] { new CellTypeConfig { Id = 0, GenomeId = 0 } };

        var issues = ConfigValidator.Validate(setup);
        Assert.That(issues.Any(i => i.Section == "Геномы" && i.Message.Contains("Обязательный морфоген")), Is.True);
    }

    [Test]
    public void ConfigDraft_RemoveMorphogen_ClearsRequiredTrigger()
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
                        Id = "g",
                        RequireMorphogenTrigger = true,
                        RequiredMorphogenId = 0
                    }
                }
            }
        };

        ConfigDraft.RemoveMorphogen(setup, 0);
        var gene = setup.Genomes[0].Genes[0];

        Assert.That(gene.RequireMorphogenTrigger, Is.True);
        Assert.That(gene.RequiredMorphogenId, Is.EqualTo(-1));
    }

    [Test]
    public void ConfigDraft_CopyMorphogen_PreservesType()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[] { new MorphogenConfig { Id = 0, Type = "Internal" } };
        var copy = ConfigDraft.CopyMorphogen(setup, setup.Morphogens[0]);
        Assert.That(copy.IsInternal, Is.True);
    }

    [Test]
    public void Localization_KnowsMorphogenTypes()
    {
        Assert.That(Localization.IsKnownMorphogenType("External"), Is.True);
        Assert.That(Localization.IsKnownMorphogenType("Internal"), Is.True);
        Assert.That(Localization.MorphogenType("Internal"), Is.EqualTo("Внутренний"));
    }

    [Test]
    public void HelpCatalog_ContainsNewArticles()
    {
        Assert.That(HelpCatalog.Get("concept.internal_morphogen"), Is.Not.Null);
        Assert.That(HelpCatalog.Get("param.requiredTrigger"), Is.Not.Null);
        Assert.That(HelpCatalog.Get("param.morphogenType"), Is.Not.Null);
        Assert.That(HelpCatalog.Get("experiment.initialSignals"), Is.Not.Null);
    }
}
