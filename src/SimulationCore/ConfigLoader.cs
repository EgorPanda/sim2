using System.Text.Json;

namespace SimulationCore;

/// <summary>Loads simulation settings, morphogen definitions and genomes from JSON files.</summary>
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

    public static CellTypeConfig[] LoadCellTypes(string file)
        => JsonSerializer.Deserialize<CellTypeConfig[]>(File.ReadAllText(file), Options)
           ?? throw new InvalidDataException($"Failed to parse {file}");

    // Text-based overloads so any host (e.g. Godot's FileAccess) can supply the JSON.

    public static SimulationConfig ParseSimulationConfig(string json)
        => JsonSerializer.Deserialize<SimulationConfig>(json, Options)
           ?? throw new InvalidDataException("Failed to parse simulation config");

    public static MorphogenConfig[] ParseMorphogens(string json)
        => JsonSerializer.Deserialize<MorphogenConfig[]>(json, Options)
           ?? throw new InvalidDataException("Failed to parse morphogen config");

    public static CellTypeConfig[] ParseCellTypes(string json)
        => JsonSerializer.Deserialize<CellTypeConfig[]>(json, Options)
           ?? throw new InvalidDataException("Failed to parse genome config");

    /// <summary>Loads the three standard config files from a directory.</summary>
    public static SimulationSetup LoadFromDirectory(string directory)
    {
        var sim = LoadSimulationConfig(Path.Combine(directory, "simulation.json"));
        var morphogens = LoadMorphogens(Path.Combine(directory, "morphogens.json"));
        var cellTypes = LoadCellTypes(Path.Combine(directory, "genomes.json"));
        return new SimulationSetup(sim, morphogens, cellTypes);
    }

    public static JsonSerializerOptions SerializerOptions => Options;
}

/// <summary>Bundles all configuration needed to construct a <see cref="Simulation"/>.</summary>
public sealed class SimulationSetup
{
    public SimulationConfig Simulation { get; }
    public MorphogenConfig[] Morphogens { get; }
    public CellTypeConfig[] CellTypes { get; }

    public SimulationSetup(SimulationConfig simulation, MorphogenConfig[] morphogens, CellTypeConfig[] cellTypes)
    {
        Simulation = simulation;
        Morphogens = morphogens;
        CellTypes = cellTypes;
    }
}
