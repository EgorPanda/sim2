using NUnit.Framework;
using SimulationCore;
using System.IO;

namespace SimulationCore.Tests;

[TestFixture]
public class ConfigMigrationTests
{
    private static string TempPath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sim2_mig_{Guid.NewGuid()}.json");
        return path;
    }

    [Test]
    public void LoadMorphogens_MigratesDisplayNameToName()
    {
        string path = TempPath();
        try
        {
            File.WriteAllText(path, @"[
  { ""id"": 1, ""name"": ""morphogen1"", ""displayName"": ""Сигнал деления"", ""type"": ""External"" }
]");
            var loaded = ConfigLoader.LoadMorphogens(path);
            Assert.That(loaded, Has.Length.EqualTo(1));
            Assert.That(loaded[0].Name, Is.EqualTo("Сигнал деления"));
            Assert.That(loaded[0].Role, Is.EqualTo(""));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void LoadMorphogens_IgnoresInitialConcentration()
    {
        string path = TempPath();
        try
        {
            File.WriteAllText(path, @"[
  { ""id"": 1, ""name"": ""morphogen1"", ""type"": ""Internal"", ""initialConcentration"": 5.0 }
]");
            var loaded = ConfigLoader.LoadMorphogens(path);
            Assert.That(loaded, Has.Length.EqualTo(1));
            Assert.That(loaded[0].DivisionInheritance, Is.EqualTo("Reset"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void LoadGenomes_MigratesDisplayNameToName()
    {
        string path = TempPath();
        try
        {
            File.WriteAllText(path, @"[
  {
    ""id"": 0,
    ""genes"": [
      { ""id"": ""g"", ""name"": ""gene"", ""displayName"": ""Ген G"" }
    ]
  }
]");
            var loaded = ConfigLoader.LoadGenomes(path);
            Assert.That(loaded, Has.Length.EqualTo(1));
            Assert.That(loaded[0].Genes[0].Name, Is.EqualTo("Ген G"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void LoadCellTypes_MigratesDisplayNameToName()
    {
        string path = TempPath();
        try
        {
            File.WriteAllText(path, @"[
  { ""id"": 0, ""name"": ""cell"", ""displayName"": ""Эпителиальная"", ""genomeId"": 0 }
]");
            var loaded = ConfigLoader.LoadCellTypes(path);
            Assert.That(loaded, Has.Length.EqualTo(1));
            Assert.That(loaded[0].Name, Is.EqualTo("Эпителиальная"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void LoadSimulation_IgnoresInitialInternalMorphogens()
    {
        string path = TempPath();
        try
        {
            File.WriteAllText(path, @"{
  ""worldWidth"": 100,
  ""worldHeight"": 100,
  ""initialInternalMorphogens"": { ""1"": 3.0 }
}");
            var loaded = ConfigLoader.LoadSimulationConfig(path);
            Assert.That(loaded.WorldWidth, Is.EqualTo(100));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void ConfigValidator_InvalidDivisionInheritance_IsError()
    {
        var setup = TestFactory.Setup();
        setup.Morphogens = new[]
        {
            new MorphogenConfig { Id = 0, Type = "Internal", DivisionInheritance = "Magic" }
        };
        var issues = ConfigValidator.Validate(setup);
        Assert.That(issues.Any(i => i.IsError && i.Message.Contains("наследования")), Is.True);
    }
}
