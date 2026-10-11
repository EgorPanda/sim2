using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

[TestFixture]
public class HelpCatalogTests
{
    [Test]
    public void EveryRegulator_HasArticle()
    {
        foreach (var kind in Localization.RegulatorKinds)
            Assert.That(HelpCatalog.Get($"regulator.{kind}"), Is.Not.Null, $"No help article for regulator {kind}.");
    }

    [Test]
    public void EveryEffect_HasArticle()
    {
        foreach (var kind in Localization.EffectKinds)
            Assert.That(HelpCatalog.Get($"effect.{kind}"), Is.Not.Null, $"No help article for effect {kind}.");
    }

    [Test]
    public void EveryProperty_HasArticle()
    {
        foreach (var prop in Localization.PropertyNames)
            Assert.That(HelpCatalog.Get($"property.{prop}"), Is.Not.Null, $"No help article for property {prop}.");
    }

    [Test]
    public void CoreConceptsAndParameters_HaveArticles()
    {
        string[] keys =
        {
            "logic.and", "logic.or", "logic.not", "op.compare", "op.range", "timer",
            "concept.morphogen", "concept.concentration", "concept.gradient",
            "concept.diffusion", "concept.decay", "concept.apoptosis",
            "concept.differentiation", "concept.silencing", "concept.division",
            "concept.growth", "concept.adhesion", "concept.gene_regulation",
            "param.activationDelay", "param.morphogenDiffusion", "param.morphogenDecay",
            "param.emissionRate", "param.growRate", "param.divisionRate",
            "param.timerDuration", "param.apoptosisProbability",
            "experiment.worldWidth", "experiment.worldHeight", "experiment.morphogenResolution",
            "experiment.fixedTimeStep", "experiment.defaultSpeed", "experiment.cellRadius",
            "experiment.initialCells", "experiment.rngSeed", "experiment.founderTypeId",
            "experiment.divisionGradientMorphogen",
            "celltype.baseRadius", "celltype.maxRadius",
            "celltype.color", "celltype.genomeId"
        };
        foreach (var key in keys)
            Assert.That(HelpCatalog.Get(key), Is.Not.Null, $"Missing help article: {key}");
    }

    [Test]
    public void UnknownKey_ReturnsMarkedPlaceholder()
    {
        var a = HelpCatalog.GetOrFallback("does.not.exist", "Некий параметр");
        Assert.That(a.IsPlaceholder, Is.True);
        Assert.That(a.Title, Is.EqualTo("Некий параметр"));
        Assert.That(a.Summary, Is.Not.Empty);
    }

    [Test]
    public void UiButtonsAndF1Keys_Exist()
    {
        // The exact keys used by HUD buttons, the F1 route and editor "?" links.
        string[] keys = { "concept.gene_regulation", "logic.and" };
        foreach (var key in keys)
            Assert.That(HelpCatalog.Get(key), Is.Not.Null, $"UI uses help key '{key}' that does not exist.");
    }

    [Test]
    public void Search_FindsByRussianAndKeyword()
    {
        Assert.That(HelpCatalog.Search("морфоген"), Is.Not.Empty);
        Assert.That(HelpCatalog.Search("diffusion").Any(a => a.Key.Contains("diffusion")), Is.True);
        Assert.That(HelpCatalog.Search("апоптоз"), Is.Not.Empty);
    }

    [Test]
    public void RelatedLinks_AllResolve()
    {
        foreach (var article in HelpCatalog.Articles)
        {
            foreach (var key in HelpCatalog.RelatedKeys(article))
                Assert.That(HelpCatalog.Get(key), Is.Not.Null,
                    $"Article {article.Key} references missing help key '{key}'.");
        }
    }

    [Test]
    public void Articles_HaveCoreSections()
    {
        foreach (var a in HelpCatalog.Articles)
        {
            Assert.That(a.Title, Is.Not.Empty, $"Article {a.Key} has no title.");
            Assert.That(a.Summary, Is.Not.Empty, $"Article {a.Key} has no summary.");
        }
    }
}
