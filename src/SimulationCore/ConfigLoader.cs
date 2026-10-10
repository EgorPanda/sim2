using System.Text.Json;

namespace SimulationCore;

/// <summary>Loads simulation settings, morphogen definitions, genomes, cell types, adhesion and experiments from JSON.</summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static SimulationConfig LoadSimulationConfig(string file)
        => JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText(file), Options)
           ?? throw new InvalidDataException($"Failed to parse {file}");

    public static MorphogenConfig[] LoadMorphogens(string file)
        => JsonSerializer.Deserialize<MorphogenConfig[]>(File.ReadAllText(file), Options)
           ?? throw new InvalidDataException($"Failed to parse {file}");

    public static GenomeConfig[] LoadGenomes(string file)
        => JsonSerializer.Deserialize<GenomeConfig[]>(File.ReadAllText(file), Options)
           ?? throw new InvalidDataException($"Failed to parse {file}");

    public static CellTypeConfig[] LoadCellTypes(string file)
        => JsonSerializer.Deserialize<CellTypeConfig[]>(File.ReadAllText(file), Options)
           ?? throw new InvalidDataException($"Failed to parse {file}");

    public static AdhesionConfig LoadAdhesion(string file)
        => JsonSerializer.Deserialize<AdhesionConfig>(File.ReadAllText(file), Options)
           ?? new AdhesionConfig();

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

        return new SimulationSetup(sim, morphogens, genomes, cellTypes, adhesion);
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

        return new SimulationSetup(sim, morphogens, genomes, cellTypes, adhesion);
    }

    public static JsonSerializerOptions SerializerOptions => Options;
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