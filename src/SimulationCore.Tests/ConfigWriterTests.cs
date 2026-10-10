using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

[TestFixture]
public class ConfigWriterTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sim2_cfg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Test]
    public void SaveSetupToDirectory_RoundTrips()
    {
        string dir = TempDir();
        try
        {
            var setup = TestFactory.Setup(
                morphogenCount: 2,
                genome: new GeneConfig
                {
                    Id = "g",
                    Name = "g",
                    DisplayName = "Ген G",
                    Description = "Тестовое описание",
                    RegulatorGroups = new List<RegulatorGroupConfig>
                    {
                        new() { Operator = "Or", Regulators = { new RegulatorConfig { Kind = "Property", Property = "energy", Min = 1 } } }
                    },
                    Effects = { TestFactory.Emit(0, 5) }
                });
            setup.Morphogens[0].DisplayName = "Питательное вещество";

            ConfigWriter.SaveSetupToDirectory(dir, setup);
            var loaded = ConfigLoader.LoadFromDirectory(dir);

            Assert.That(loaded.Morphogens.Length, Is.EqualTo(2));
            Assert.That(loaded.Morphogens[0].DisplayName, Is.EqualTo("Питательное вещество"));
            Assert.That(loaded.Genomes[0].Genes[0].DisplayName, Is.EqualTo("Ген G"));
            Assert.That(loaded.Genomes[0].Genes[0].Description, Is.EqualTo("Тестовое описание"));
            Assert.That(loaded.Genomes[0].Genes[0].RegulatorGroups, Has.Count.EqualTo(1));
            Assert.That(loaded.Genomes[0].Genes[0].RegulatorGroups![0].Operator, Is.EqualTo("Or"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void SaveExperiment_DoesNotTouchAnotherExperiment()
    {
        string root = TempDir();
        try
        {
            // Two experiment profiles referencing their own per-experiment folders.
            string dirA = Path.Combine(root, "a");
            string dirB = Path.Combine(root, "b");
            var setupA = TestFactory.Setup(genome: TestFactory.Gene("g", TestFactory.Emit(0, 1)));
            ConfigWriter.SaveSetupToDirectory(dirA, setupA);
            ConfigWriter.SaveSetupToDirectory(dirB, setupA);
            File.WriteAllText(Path.Combine(root, "a.json"),
                "{\"name\":\"a\",\"simulationFile\":\"a/simulation.json\",\"morphogenFile\":\"a/morphogens.json\",\"genomeFile\":\"a/genomes.json\",\"cellTypeFile\":\"a/celltypes.json\",\"adhesionFile\":\"a/adhesion.json\"}");
            File.WriteAllText(Path.Combine(root, "b.json"),
                "{\"name\":\"b\",\"simulationFile\":\"b/simulation.json\",\"morphogenFile\":\"b/morphogens.json\",\"genomeFile\":\"b/genomes.json\",\"cellTypeFile\":\"b/celltypes.json\",\"adhesionFile\":\"b/adhesion.json\"}");

            string beforeB = File.ReadAllText(Path.Combine(dirB, "genomes.json"));

            var edited = ConfigLoader.LoadExperiment(Path.Combine(root, "a.json"));
            edited.Genomes[0].Genes[0].DisplayName = "Изменённое имя";
            ConfigWriter.SaveExperiment(Path.Combine(root, "a.json"), edited);

            var reloadedA = ConfigLoader.LoadExperiment(Path.Combine(root, "a.json"));
            Assert.That(reloadedA.Genomes[0].Genes[0].DisplayName, Is.EqualTo("Изменённое имя"));
            Assert.That(File.ReadAllText(Path.Combine(dirB, "genomes.json")), Is.EqualTo(beforeB),
                "Saving experiment A must not modify experiment B.");
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void DeepClone_IsIndependent()
    {
        var setup = TestFactory.Setup(genome: TestFactory.Gene("g", TestFactory.Emit(0, 1)));
        var clone = ConfigLoader.DeepClone(setup);
        clone.Genomes[0].Genes[0].DisplayName = "changed";
        clone.Simulation.WorldWidth = 1.0;

        Assert.That(setup.Genomes[0].Genes[0].DisplayName, Is.Empty);
        Assert.That(setup.Simulation.WorldWidth, Is.Not.EqualTo(1.0));
    }

    [Test]
    public void CopyGene_GetsUniqueId()
    {
        var genome = new GenomeConfig { Id = 0, Genes = { TestFactory.Gene("g", TestFactory.Emit(0, 1)) } };
        var copy = ConfigDraft.CopyGene(genome, genome.Genes[0]);
        genome.Genes.Add(copy);

        Assert.That(copy.Key, Is.Not.EqualTo("g"));
        Assert.That(ConfigDraft.GeneIdExists(genome, copy.Key), Is.True);
        Assert.That(genome.Genes.Select(g => g.Key).Distinct().Count(), Is.EqualTo(genome.Genes.Count));
    }

    [Test]
    public void SaveExperimentProfile_UnusualName_ProducesValidJson()
    {
        string root = TempDir();
        try
        {
            string filesDir = Path.Combine(root, "x");
            var setup = TestFactory.Setup(morphogenCount: 2, genome: TestFactory.Gene("g", TestFactory.Emit(0, 1)));
            ConfigWriter.SaveSetupToDirectory(filesDir, setup);

            // Exactly the serialization used by Main.SaveExperimentTo: profile
            // JSON is produced by the serializer, not by raw string building.
            string weird = "профиль \"с\" \\кавычками\nи 'переносом' + ☂";
            string profilePath = Path.Combine(root, "x.json");
            var profile = new ExperimentConfig
            {
                Name = weird,
                Description = "Сохранено из лаборатории Sim2.",
                SimulationFile = "x/simulation.json",
                MorphogenFile = "x/morphogens.json",
                GenomeFile = "x/genomes.json",
                CellTypeFile = "x/celltypes.json",
                AdhesionFile = "x/adhesion.json"
            };
            File.WriteAllText(profilePath, ConfigLoader.SerializeExperiment(profile));

            var parsed = ConfigLoader.ParseExperiment(File.ReadAllText(profilePath));
            Assert.That(parsed.Name, Is.EqualTo(weird),
                "Name with quotes/backslashes/newlines must survive JSON round-trip.");

            var loaded = ConfigLoader.LoadExperiment(profilePath);
            Assert.That(loaded.Morphogens.Length, Is.EqualTo(2));
            Assert.That(loaded.Genomes[0].Genes[0].Key, Is.EqualTo("g"));
            Assert.That(loaded.CellTypes.Length, Is.EqualTo(setup.CellTypes.Length));
        }
        finally { Directory.Delete(root, true); }
    }
}
