using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

[TestFixture]
public class LocalizationTests
{
    [Test]
    public void EveryRegulatorKind_HasRussianName()
    {
        foreach (var kind in Localization.RegulatorKinds)
        {
            Assert.That(Localization.IsKnownRegulator(kind), Is.True);
            string name = Localization.Regulator(kind);
            Assert.That(name, Is.Not.Contains("Регулятор «"), $"Missing Russian name for regulator {kind}.");
            Assert.That(name, Is.Not.Empty);
        }
    }

    [Test]
    public void EveryEffectKind_HasRussianName()
    {
        foreach (var kind in Localization.EffectKinds)
        {
            Assert.That(Localization.IsKnownEffect(kind), Is.True);
            string name = Localization.Effect(kind);
            Assert.That(name, Is.Not.Contains("Эффект «"), $"Missing Russian name for effect {kind}.");
            Assert.That(Localization.EffectExplanation(kind), Is.Not.Contains("неизвестный"));
        }
    }

    [Test]
    public void EveryProperty_HasRussianName()
    {
        foreach (var prop in Localization.PropertyNames)
        {
            Assert.That(Localization.IsKnownProperty(prop), Is.True);
            Assert.That(Localization.Property(prop), Is.Not.Contains("Свойство «"));
        }
    }

    [Test]
    public void UnknownIdentifiers_GetSafeFallback()
    {
        Assert.That(Localization.Regulator("Mystery"), Does.Contain("Mystery"));
        Assert.That(Localization.Effect("Mystery"), Does.Contain("Mystery"));
        Assert.That(Localization.IsKnownRegulator("Mystery"), Is.False);
        Assert.That(Localization.IsKnownEffect("Mystery"), Is.False);
    }

    [Test]
    public void GeneName_PrefersName_AndFlagsMissing()
    {
        var named = new GeneConfig { Id = "g", Name = "Секретор" };
        Assert.That(Localization.GeneName(named), Is.EqualTo("Секретор"));

        var unnamed = new GeneConfig { Id = "g", Name = "" };
        Assert.That(Localization.GeneName(unnamed), Does.Contain("g"));
        Assert.That(Localization.GeneName(unnamed), Does.Contain("без названия"));
    }

    [Test]
    public void DescribeRange_CoversOperators()
    {
        Assert.That(Localization.DescribeRange(5, double.PositiveInfinity), Does.StartWith("≥"));
        Assert.That(Localization.DescribeRange(double.NegativeInfinity, 5), Does.StartWith("≤"));
        Assert.That(Localization.DescribeRange(1, 3), Does.Contain("…"));
        Assert.That(Localization.DescribeRange(2, 2), Does.StartWith("="));
        Assert.That(Localization.DescribeRange(double.NegativeInfinity, double.PositiveInfinity), Does.Contain("любое"));
    }
}
