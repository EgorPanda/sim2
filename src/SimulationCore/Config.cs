namespace SimulationCore;

/// <summary>World / experiment settings. Loaded from config/simulation.json.</summary>
public sealed class SimulationConfig
{
    public double WorldWidth { get; set; } = 2560.0;
    public double WorldHeight { get; set; } = 1440.0;
    public double MorphogenResolution { get; set; } = 8.0;
    public double FixedTimeStep { get; set; } = 0.05;
    public double DefaultSpeed { get; set; } = 1.0;
    public double CellRadius { get; set; } = 12.0;
    public int InitialCells { get; set; } = 1;
    public int RngSeed { get; set; } = 1337;

    /// <summary>Cell type id used for the initial founder cell(s).</summary>
    public int FounderTypeId { get; set; } = 0;

    /// <summary>Morphogen whose gradient biases division direction. -1 disables it.</summary>
    public int DivisionGradientMorphogen { get; set; } = 0;
}

/// <summary>Definition of a single morphogen (config/morphogens.json).</summary>
public sealed class MorphogenConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = "morphogen";
    public double Diffusion { get; set; } = 0.0;
    public double Decay { get; set; } = 0.0;
    public string Color { get; set; } = "#ffffff";
}

/// <summary>
/// A genome: a shared, immutable list of genes (config/genomes.json). Many cells
/// and phenotypes reference the same genome by <see cref="Id"/>; no phenotype
/// keeps a private copy.
/// </summary>
public sealed class GenomeConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = "genome";
    public List<GeneConfig> Genes { get; set; } = new();
}

/// <summary>
/// A cell phenotype (config/celltypes.json). Holds only phenotypic parameters
/// and a reference to the shared genome; per-cell gene state lives on the cell.
/// </summary>
public sealed class CellTypeConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = "cell";
    public string Color { get; set; } = "#ffffff";

    /// <summary>Shared genome this phenotype expresses.</summary>
    public int GenomeId { get; set; } = 0;

    public double BaseRadius { get; set; } = 12.0;
    public double MaxRadius { get; set; } = 20.0;
    public double MaxEnergy { get; set; } = 120.0;
    public double MaintenanceCost { get; set; } = 1.5;
    public double DivisionEnergyCost { get; set; } = 50.0;
    public double GrowEnergyCostPerRadius { get; set; } = 1.0;
    public double StarveGracePeriod { get; set; } = 2.0;
}

/// <summary>A gene: regulators gate its activation, effects run while active.</summary>
public sealed class GeneConfig
{
    /// <summary>Stable identifier used for per-cell gene state and silencing.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "gene";
    public double ActivationDelay { get; set; } = 0.0;
    public List<RegulatorConfig> Regulators { get; set; } = new();
    public List<GeneEffectConfig> Effects { get; set; } = new();

    /// <summary>Effective id: explicit <see cref="Id"/>, otherwise the name.</summary>
    public string Key => string.IsNullOrEmpty(Id) ? Name : Id;
}

/// <summary>
/// Condition that gates a gene. All regulators of a gene are AND-combined;
/// each regulator can be negated with <see cref="Invert"/>.
/// </summary>
public sealed class RegulatorConfig
{
    /// <summary>"Property", "Morphogen", "Phenotype" or "Timer".</summary>
    public string Kind { get; set; } = "Property";

    /// <summary>Logical NOT applied to this regulator's result.</summary>
    public bool Invert { get; set; } = false;

    /// <summary>Property name when Kind == "Property": energy, radius, age, neighbours.</summary>
    public string Property { get; set; } = "";

    /// <summary>Morphogen index when Kind == "Morphogen".</summary>
    public int Morphogen { get; set; } = -1;

    /// <summary>Cell type id required when Kind == "Phenotype".</summary>
    public int TypeId { get; set; } = -1;

    /// <summary>Named timer required to be running when Kind == "Timer".</summary>
    public string Timer { get; set; } = "";

    public double Min { get; set; } = double.NegativeInfinity;
    public double Max { get; set; } = double.PositiveInfinity;
}

/// <summary>An effect applied while the owning gene is active.</summary>
public sealed class GeneEffectConfig
{
    /// <summary>
    /// "EmitMorphogen", "Absorb", "Grow", "Divide", "SetCellType", "StartTimer",
    /// "Apoptosis", "SilenceGene" or "BlockDivision".
    /// </summary>
    public string Kind { get; set; } = "";

    /// <summary>Run only once when the gene activates, instead of every step.</summary>
    public bool Once { get; set; } = false;

    public int Morphogen { get; set; } = -1;

    /// <summary>Target cell type id for "SetCellType".</summary>
    public int TypeId { get; set; } = -1;

    /// <summary>Timer name for "StartTimer" and the "Timer" regulator.</summary>
    public string Timer { get; set; } = "";

    /// <summary>Gene id silenced by "SilenceGene".</summary>
    public string Gene { get; set; } = "";

    /// <summary>Rate: emission/absorb/grow/divide amount, or timer duration.</summary>
    public double Rate { get; set; } = 0.0;

    /// <summary>Duration for "StartTimer" (seconds).</summary>
    public double Duration { get; set; } = 0.0;

    /// <summary>Per-second apoptosis probability; 0 or less kills deterministically.</summary>
    public double Probability { get; set; } = 0.0;
}

/// <summary>
/// Adhesion strengths between phenotype pairs (config/adhesion.json). Strength is
/// in [0,1]: 0 separates fully, 1 allows the maximum configured overlap. Cells
/// that adhere form cohesive tissue; non-adhering pairs keep their distance.
/// </summary>
public sealed class AdhesionConfig
{
    /// <summary>Default strength for pairs not listed explicitly.</summary>
    public double Default { get; set; } = 0.0;

    /// <summary>Maximum overlap (fraction of touching distance) at strength 1.</summary>
    public double MaxOverlap { get; set; } = 0.4;

    public List<AdhesionPair> Pairs { get; set; } = new();
}

/// <summary>Adhesion strength for a symmetric pair of phenotypes.</summary>
public sealed class AdhesionPair
{
    public int A { get; set; }
    public int B { get; set; }
    public double Strength { get; set; }
}

/// <summary>
/// A named experiment profile (config/experiments/*.json). Points at the config
/// files to use and optionally overrides world-level settings.
/// </summary>
public sealed class ExperimentConfig
{
    public string Name { get; set; } = "experiment";
    public string? Description { get; set; }

    public string SimulationFile { get; set; } = "simulation.json";
    public string MorphogenFile { get; set; } = "morphogens.json";
    public string GenomeFile { get; set; } = "genomes.json";
    public string CellTypeFile { get; set; } = "celltypes.json";
    public string AdhesionFile { get; set; } = "adhesion.json";

    // Optional overrides (null => keep the value from the simulation file).
    public double? WorldWidth { get; set; }
    public double? WorldHeight { get; set; }
    public int? InitialCells { get; set; }
    public int? RngSeed { get; set; }
}
