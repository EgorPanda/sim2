namespace SimulationCore;

/// <summary>
/// Writes edited configurations back to JSON. Used by the research UI to save a
/// draft genome/morphogen/cell-type/experiment configuration to the files of the
/// selected experiment (never silently overwriting an unrelated experiment).
/// </summary>
public static class ConfigWriter
{
    /// <summary>Writes the standard config files of a setup into a directory.</summary>
    public static void SaveSetupToDirectory(string directory, SimulationSetup setup)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "simulation.json"), ConfigLoader.SerializeSimulationConfig(setup.Simulation));
        File.WriteAllText(Path.Combine(directory, "morphogens.json"), ConfigLoader.SerializeMorphogens(setup.Morphogens));
        File.WriteAllText(Path.Combine(directory, "genomes.json"), ConfigLoader.SerializeGenomes(setup.Genomes));
        File.WriteAllText(Path.Combine(directory, "celltypes.json"), ConfigLoader.SerializeCellTypes(setup.CellTypes));
        File.WriteAllText(Path.Combine(directory, "adhesion.json"), ConfigLoader.SerializeAdhesion(setup.Adhesion));
    }

    /// <summary>
    /// Saves a setup to the files referenced by an experiment profile. Paths are
    /// resolved relative to the experiment's own directory (matching
    /// <see cref="ConfigLoader.LoadExperiment"/>), so editing one experiment
    /// never touches another.
    /// </summary>
    public static void SaveExperiment(string experimentFile, SimulationSetup setup)
    {
        var experiment = ConfigLoader.ParseExperiment(File.ReadAllText(experimentFile));
        string expDir = Path.GetDirectoryName(Path.GetFullPath(experimentFile))!;
        string Resolve(string name) => Path.IsPathRooted(name) ? name : Path.GetFullPath(Path.Combine(expDir, name));

        WriteFile(Resolve(experiment.SimulationFile), ConfigLoader.SerializeSimulationConfig(setup.Simulation));
        WriteFile(Resolve(experiment.MorphogenFile), ConfigLoader.SerializeMorphogens(setup.Morphogens));
        WriteFile(Resolve(experiment.GenomeFile), ConfigLoader.SerializeGenomes(setup.Genomes));
        WriteFile(Resolve(experiment.CellTypeFile), ConfigLoader.SerializeCellTypes(setup.CellTypes));
        WriteFile(Resolve(experiment.AdhesionFile), ConfigLoader.SerializeAdhesion(setup.Adhesion));
    }

    /// <summary>Applies the experiment's world-level overrides onto a saved simulation file.</summary>
    public static void ApplyExperimentOverrides(ExperimentConfig experiment, SimulationConfig sim)
    {
        if (experiment.WorldWidth is double w) sim.WorldWidth = w;
        if (experiment.WorldHeight is double h) sim.WorldHeight = h;
        if (experiment.InitialCells is int ic) sim.InitialCells = ic;
        if (experiment.RngSeed is int seed) sim.RngSeed = seed;
    }

    private static void WriteFile(string path, string content)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
    }
}
