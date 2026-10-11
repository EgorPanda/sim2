using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SimulationCore;

/// <summary>Loads simulation settings, morphogen definitions, genomes, cell types, adhesion and experiments from JSON.</summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static SimulationConfig LoadSimulationConfig(string file)
        => ParseSimulationConfig(MigrateSimulationJson(File.ReadAllText(file)));

    public static MorphogenConfig[] LoadMorphogens(string file)
        => ParseMorphogens(MigrateMorphogensJson(File.ReadAllText(file)));

    public static GenomeConfig[] LoadGenomes(string file)
        => ParseGenomes(MigrateGenomesJson(File.ReadAllText(file)));

    public static CellTypeConfig[] LoadCellTypes(string file)
        => ParseCellTypes(MigrateCellTypesJson(File.ReadAllText(file)));

    public static AdhesionConfig LoadAdhesion(string file)
        => JsonSerializer.Deserialize<AdhesionConfig>(File.ReadAllText(file), Options)
           ?? new AdhesionConfig();

    // --- JSON migration (M1.6: unified names, no founder internal concentrations) ---

    private static string MigrateSimulationJson(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is not JsonObject obj) return json;
        obj.Remove("initialInternalMorphogens");
        return node.ToJsonString(Options);
    }

    private static string MigrateMorphogensJson(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject obj) continue;
                MigrateDisplayNameToName(obj);
                obj.Remove("initialConcentration");
                if (!obj.ContainsKey("role")) obj["role"] = "";
                if (!obj.ContainsKey("divisionInheritance")) obj["divisionInheritance"] = "Reset";
            }
        }
        return node!.ToJsonString(Options);
    }

    private static string MigrateGenomesJson(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is JsonArray genomes)
        {
            foreach (var genome in genomes)
            {
                if (genome is not JsonObject gObj || gObj["genes"] is not JsonArray genes) continue;
                foreach (var item in genes)
                {
                    if (item is not JsonObject obj) continue;
                    MigrateDisplayNameToName(obj);
                    if (!obj.ContainsKey("role")) obj["role"] = "";
                }
            }
        }
        return node!.ToJsonString(Options);
    }

    private static string MigrateCellTypesJson(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject obj) continue;
                MigrateDisplayNameToName(obj);
            }
        }
        return node!.ToJsonString(Options);
    }

    private static void MigrateDisplayNameToName(JsonObject obj)
    {
        if (obj.TryGetPropertyValue("displayName", out var displayNode) && displayNode is JsonValue displayValue)
        {
            string display = displayValue.GetValue<string>();
            if (obj.TryGetPropertyValue("name", out var nameNode) && nameNode is JsonValue nameValue)
            {
                string name = nameValue.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(display))
                    obj["name"] = display;
                else if (!string.IsNullOrWhiteSpace(display))
                    obj["name"] = display; // canonical name is the human-readable one
            }
            else if (!string.IsNullOrWhiteSpace(display))
            {
                obj["name"] = display;
            }
            obj.Remove("displayName");
        }
    }

    // Text-based overloads so any host (e.g. Godot's FileAccess) can supply the JSON.

    public static SimulationConfig ParseSimulationConfig(string json)
        => JsonSerializer.Deserialize<SimulationConfig>(json, Options)
           ?? throw new InvalidDataException("Failed to parse simulation config");

    public static MorphogenConfig[] ParseMorphogens(string json)
        => JsonSerializer.Deserialize<MorphogenConfig[]>(json, Options)
           ?? throw new InvalidDataException("Failed to parse morphogen config");

    public static GenomeConfig[] ParseGenomes(string json)
        => JsonSerializer.Deserialize<GenomeConfig[]>(json, Options)
           ?? throw new InvalidDataException("Failed to parse genome config");

    public static CellTypeConfig[] ParseCellTypes(string json)
        => JsonSerializer.Deserialize<CellTypeConfig[]>(json, Options)
           ?? throw new InvalidDataException("Failed to parse cell type config");

    /// <summary>Loads the standard config files from a directory.</summary>
    public static SimulationSetup LoadFromDirectory(string directory)
    {
        var sim = LoadSimulationConfig(Path.Combine(directory, "simulation.json"));
        var morphogens = LoadMorphogens(Path.Combine(directory, "morphogens.json"));
        var genomes = LoadGenomes(Path.Combine(directory, "genomes.json"));
        var cellTypes = LoadCellTypes(Path.Combine(directory, "celltypes.json"));

        var adhesion = new AdhesionConfig();
        string adhesionPath = Path.Combine(directory, "adhesion.json");
        if (File.Exists(adhesionPath)) adhesion = LoadAdhesion(adhesionPath);

        var setup = new SimulationSetup(sim, morphogens, genomes, cellTypes, adhesion);
        ConfigDraft.SanitizeMorphogenReferences(setup);
        return setup;
    }

    /// <summary>
    /// Loads an experiment profile from <paramref name="experimentFile"/>. File
    /// references inside the profile are resolved relative to the experiment's
    /// own directory unless they are absolute.
    /// </summary>
    public static SimulationSetup LoadExperiment(string experimentFile)
    {
        var experiment = JsonSerializer.Deserialize<ExperimentConfig>(File.ReadAllText(experimentFile), Options)
                         ?? throw new InvalidDataException($"Failed to parse {experimentFile}");

        string expDir = Path.GetDirectoryName(Path.GetFullPath(experimentFile))!;

        string Resolve(string name) => Path.IsPathRooted(name) ? name : Path.GetFullPath(Path.Combine(expDir, name));

        var sim = LoadSimulationConfig(Resolve(experiment.SimulationFile));
        if (experiment.WorldWidth is double w) sim.WorldWidth = w;
        if (experiment.WorldHeight is double h) sim.WorldHeight = h;
        if (experiment.InitialCells is int ic) sim.InitialCells = ic;
        if (experiment.RngSeed is int seed) sim.RngSeed = seed;

        var morphogens = LoadMorphogens(Resolve(experiment.MorphogenFile));
        var genomes = LoadGenomes(Resolve(experiment.GenomeFile));
        var cellTypes = LoadCellTypes(Resolve(experiment.CellTypeFile));

        var adhesion = new AdhesionConfig();
        string adhesionPath = Resolve(experiment.AdhesionFile);
        if (File.Exists(adhesionPath)) adhesion = LoadAdhesion(adhesionPath);

        var setup = new SimulationSetup(sim, morphogens, genomes, cellTypes, adhesion);
        ConfigDraft.SanitizeMorphogenReferences(setup);
        return setup;
    }

    public static JsonSerializerOptions SerializerOptions => Options;

    // --- Serialization (M1.5: the UI saves edited configurations) -----------

    public static string SerializeSimulationConfig(SimulationConfig config)
        => JsonSerializer.Serialize(config, Options);

    public static string SerializeMorphogens(MorphogenConfig[] morphogens)
        => JsonSerializer.Serialize(morphogens, Options);

    public static string SerializeGenomes(GenomeConfig[] genomes)
        => JsonSerializer.Serialize(genomes, Options);

    public static string SerializeCellTypes(CellTypeConfig[] cellTypes)
        => JsonSerializer.Serialize(cellTypes, Options);

    public static string SerializeAdhesion(AdhesionConfig adhesion)
        => JsonSerializer.Serialize(adhesion, Options);

    public static string SerializeExperiment(ExperimentConfig experiment)
        => JsonSerializer.Serialize(experiment, Options);

    public static ExperimentConfig ParseExperiment(string json)
        => JsonSerializer.Deserialize<ExperimentConfig>(json, Options)
           ?? throw new InvalidDataException("Failed to parse experiment config");

    public static AdhesionConfig ParseAdhesion(string json)
        => JsonSerializer.Deserialize<AdhesionConfig>(json, Options)
           ?? new AdhesionConfig();

    /// <summary>Deep, independent copy of a setup (draft editing never mutates the original).</summary>
    public static SimulationSetup DeepClone(SimulationSetup setup)
        => new(
            ParseSimulationConfig(SerializeSimulationConfig(setup.Simulation)),
            ParseMorphogens(SerializeMorphogens(setup.Morphogens)),
            ParseGenomes(SerializeGenomes(setup.Genomes)),
            ParseCellTypes(SerializeCellTypes(setup.CellTypes)),
            ParseAdhesion(SerializeAdhesion(setup.Adhesion)));
}

/// <summary>Bundles all configuration needed to construct a <see cref="Simulation"/>.</summary>
public sealed class SimulationSetup
{
    public SimulationConfig Simulation { get; }
    public MorphogenConfig[] Morphogens { get; set; }
    public GenomeConfig[] Genomes { get; set; }
    public CellTypeConfig[] CellTypes { get; set; }
    public AdhesionConfig Adhesion { get; set; }

    public SimulationSetup(
        SimulationConfig simulation,
        MorphogenConfig[] morphogens,
        GenomeConfig[] genomes,
        CellTypeConfig[] cellTypes,
        AdhesionConfig? adhesion = null)
    {
        Simulation = simulation;
        Morphogens = morphogens;
        Genomes = genomes;
        CellTypes = cellTypes;
        Adhesion = adhesion ?? new AdhesionConfig();
    }

    public SimulationSetup Clone() =>
        new(Simulation, Morphogens, Genomes, CellTypes.ToArray(), Adhesion);
}