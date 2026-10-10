using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

[TestFixture]
public class ConfigValidatorTests
{
    private static string FindConfigDir()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "config");
            if (File.Exists(Path.Combine(candidate, "simulation.json")))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the 'config' directory.");
    }

    private static string FindExperimentsDir()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "config", "experiments");
            if (File.Exists(Path.Combine(candidate, "multicellular.json")))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the 'config/experiments' directory.");
    }

    [Test]
    public void ShippedConfigs_HaveNoErrors()
    {
        var baseSetup = ConfigLoader.LoadFromDirectory(FindConfigDir());
        Assert.That(ConfigValidator.HasErrors(ConfigValidator.Validate(baseSetup)), Is.False,
            "The shipped base config must validate without errors.");

        var experiment = ConfigLoader.LoadExperiment(Path.Combine(FindExperimentsDir(), "multicellular.json"));
        Assert.That(ConfigValidator.HasErrors(ConfigValidator.Validate(experiment)), Is.False,
            "The shipped multicellular experiment must validate without errors.");
    }

    [Test]
    public void DuplicateGeneIds_AreFlagged()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: new[]
            {
                TestFactory.Gene("dup", TestFactory.Emit(0, 1)),
                TestFactory.Gene("dup", TestFactory.Emit(0, 1))
            });
        var issues = ConfigValidator.Validate(setup);
        Assert.That(issues.Any(i => i.IsError && i.Message.Contains("Повторяющийся идентификатор гена")), Is.True);
    }

    [Test]
    public void UnknownEffect_IsFlagged()
    {
        var setup = TestFactory.Setup(genome: TestFactory.Gene("g", new GeneEffectConfig { Kind = "Teleport" }));
        var issues = ConfigValidator.Validate(setup);
        Assert.That(issues.Any(i => i.IsError && i.Message.Contains("Неизвестный эффект")), Is.True);
    }

    [Test]
    public void UnknownRegulator_IsFlagged()
    {
        var g = TestFactory.Gene("g", TestFactory.Emit(0, 1));
        g.Regulators.Add(new RegulatorConfig { Kind = "Vibes" });
        var issues = ConfigValidator.Validate(TestFactory.Setup(genome: g));
        Assert.That(issues.Any(i => i.IsError && i.Message.Contains("Неизвестный регулятор")), Is.True);
    }

    [Test]
    public void MissingMorphogenReference_IsFlagged()
    {
        var setup = TestFactory.Setup(morphogenCount: 1, genome: TestFactory.Gene("g", TestFactory.Emit(9, 1)));
        var issues = ConfigValidator.Validate(setup);
        Assert.That(issues.Any(i => i.IsError && i.Message.Contains("несуществующий морфоген")), Is.True);
    }

    [Test]
    public void EmptyGroup_IsWarning_NotError()
    {
        var g = new GeneConfig
        {
            Id = "g",
            RegulatorGroups = new List<RegulatorGroupConfig> { new() { Operator = "And" } },
            Effects = { TestFactory.Emit(0, 1) }
        };
        var issues = ConfigValidator.Validate(TestFactory.Setup(genome: g));
        Assert.That(issues.Any(i => !i.IsError && i.Message.Contains("Пустая группа")), Is.True);
        Assert.That(ConfigValidator.HasErrors(issues), Is.False);
    }

    [Test]
    public void BadRange_IsFlagged()
    {
        var g = TestFactory.Gene("g", TestFactory.Emit(0, 1));
        g.Regulators.Add(new RegulatorConfig { Kind = "Property", Property = "energy", Min = 10, Max = 5 });
        var issues = ConfigValidator.Validate(TestFactory.Setup(genome: g));
        Assert.That(issues.Any(i => i.IsError && i.Message.Contains("больше максимума")), Is.True);
    }

    [Test]
    public void BadCellTypeRange_IsFlagged()
    {
        var setup = TestFactory.Setup(genome: TestFactory.Gene("g", TestFactory.Emit(0, 1)));
        foreach (var t in setup.CellTypes)
        {
            t.BaseRadius = 20;
            t.MaxRadius = 10;
        }
        var issues = ConfigValidator.Validate(setup);
        Assert.That(issues.Any(i => i.IsError && i.Message.Contains("Максимальный радиус")), Is.True);
    }

    [Test]
    public void ReferenceDiscovery_FindsDependents()
    {
        var setup = TestFactory.Setup(genome: new[]
        {
            TestFactory.Gene("target", TestFactory.Emit(0, 1)),
            new GeneConfig
            {
                Id = "silencer",
                Effects = { new GeneEffectConfig { Kind = "SilenceGene", Gene = "target" } }
            }
        });

        Assert.That(ConfigDraft.FindGeneReferences(setup, "target"), Is.Not.Empty);
        Assert.That(ConfigDraft.FindMorphogenReferences(setup, 0), Is.Not.Empty);
    }
}
