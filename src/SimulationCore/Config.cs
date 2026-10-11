namespace SimulationCore;

using System.Text.Json.Serialization;

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
    public int DivisionGradientMorphogen { get; set; } = -1;

    /// <summary>Initial local sources of external morphogens (seeded on reset).</summary>
    public List<MorphogenSourceConfig> InitialExternalSources { get; set; } = new();
}

/// <summary>Definition of a single morphogen (config/morphogens.json).</summary>
public sealed class MorphogenConfig
{
    public int Id { get; set; }

    /// <summary>Human-readable name shown in the UI.</summary>
    public string Name { get; set; } = "morphogen";

    /// <summary>Short biological role shown in the UI.</summary>
    public string Role { get; set; } = "";

    /// <summary>Optional detailed description shown in the UI.</summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// "External" (default) — diffuses through the extracellular field;
    /// "Internal" — stored per-cell, no diffusion, provides cellular memory.
    /// </summary>
    public string Type { get; set; } = "External";

    public bool IsExternal => !IsInternal;

    public bool IsInternal =>
        string.Equals(Type, "Internal", StringComparison.OrdinalIgnoreCase);

    public double Diffusion { get; set; } = 0.0;
    public double Decay { get; set; } = 0.0;
    public string Color { get; set; } = "#ffffff";

    /// <summary>
    /// How an internal morphogen is inherited by the daughter cell during division.
    /// "Reset" (default) — daughter starts with 0.
    /// "Copy" — daughter receives the same concentration as the parent.
    /// "Split" — parent and daughter each get half of the original concentration.
    /// Ignored for external morphogens.
    /// </summary>
    public string DivisionInheritance { get; set; } = "Reset";
}

/// <summary>A local initial source of an external morphogen.</summary>
public sealed class MorphogenSourceConfig
{
    public int MorphogenId { get; set; } = -1;
    public double X { get; set; }
    public double Y { get; set; }
    public double Radius { get; set; }
    public double Concentration { get; set; }

    /// <summary>"uniform" or "gaussian".</summary>
    public string Profile { get; set; } = "uniform";
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

    /// <summary>Human-readable name shown in the UI.</summary>
    public string Name { get; set; } = "cell";

    /// <summary>Optional detailed description shown in the UI.</summary>
    public string Description { get; set; } = "";

    public string Color { get; set; } = "#ffffff";

    /// <summary>Shared genome this phenotype expresses.</summary>
    public int GenomeId { get; set; } = 0;

    public double BaseRadius { get; set; } = 12.0;
    public double MaxRadius { get; set; } = 20.0;
}

/// <summary>A gene: regulators gate its activation, effects run while active.</summary>
public sealed class GeneConfig
{
    /// <summary>Stable identifier used for per-cell gene state and silencing.</summary>
    public string Id { get; set; } = "";

    /// <summary>Human-readable name shown in the UI.</summary>
    public string Name { get; set; } = "gene";

    /// <summary>Short biological role shown in the UI.</summary>
    public string Role { get; set; } = "";

    /// <summary>Optional detailed description shown in the UI.</summary>
    public string Description { get; set; } = "";

    public double ActivationDelay { get; set; } = 0.0;

    /// <summary>
    /// When true, the gene can only activate if the required morphogen concentration
    /// reaches the threshold. The trigger is checked in addition to the regulator groups.
    /// For newly-created genes this is enabled automatically; legacy genes without the
    /// field keep the historical always-on behaviour.
    /// </summary>
    public bool RequireMorphogenTrigger { get; set; } = false;

    /// <summary>Morphogen id for the required trigger (-1 = not configured).</summary>
    public int RequiredMorphogenId { get; set; } = -1;

    /// <summary>Concentration threshold the required morphogen must reach.</summary>
    public double RequiredMorphogenThreshold { get; set; } = 0.0;

    /// <summary>
    /// Legacy, single AND-group of regulators. Kept for backward compatibility:
    /// when <see cref="RegulatorGroups"/> is empty this list is treated as one
    /// AND-group, exactly reproducing pre-M1.5 behaviour.
    /// </summary>
    public List<RegulatorConfig> Regulators { get; set; } = new();

    /// <summary>
    /// Optional list of regulator groups (M1.5). Groups are OR-combined, and the
    /// regulators inside each group are AND-combined. When empty, the legacy
    /// <see cref="Regulators"/> list is used instead so old JSON files keep working.
    /// </summary>
    public List<RegulatorGroupConfig>? RegulatorGroups { get; set; }

    public List<GeneEffectConfig> Effects { get; set; } = new();

    /// <summary>Effective id: explicit <see cref="Id"/>, otherwise the name.</summary>
    [JsonIgnore]
    public string Key => string.IsNullOrEmpty(Id) ? Name : Id;

    /// <summary>
    /// Returns the effective evaluation groups. Legacy <see cref="Regulators"/>
    /// become a single AND-group when no explicit groups are present.
    /// </summary>
    public IReadOnlyList<RegulatorGroupConfig> EffectiveGroups()
    {
        if (RegulatorGroups is { Count: > 0 }) return RegulatorGroups;
        return new[] { new RegulatorGroupConfig { Operator = "And", Regulators = Regulators } };
    }
}

/// <summary>
/// A group of regulators combined with AND (default) or OR. Groups of one gene
/// are themselves OR-combined at the top level, so a gene activates when any of
/// its groups is satisfied.
/// </summary>
public sealed class RegulatorGroupConfig
{
    /// <summary>How the regulators inside this group are combined: "And" or "Or".</summary>
    public string Operator { get; set; } = "And";

    public List<RegulatorConfig> Regulators { get; set; } = new();
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

    /// <summary>Property name when Kind == "Property": radius, age, neighbours.</summary>
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
    /// "EmitMorphogen", "Grow", "Divide", "SetCellType", "StartTimer",
    /// "Apoptosis", "SilenceGene" or "BlockDivision".
    /// </summary>
    public string Kind { get; set; } = "";

    /// <summary>Run only once when the gene activates, instead of every step.</summary>
    public bool Once { get; set; } = false;

    public int Morphogen { get; set; } = -1;

    /// <summary>Target cell type id for "SetCellType".</summary>
    public int TypeId { get; set; } = -1;

    /// <summary>
    /// Child phenotype for "Divide". Null means "inherit the parent's phenotype"
    /// (legacy behaviour); when set, the daughter is created with this phenotype
    /// while the parent keeps its own. Not serialized when null so old JSON
    /// configs load without manual edits.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ChildTypeId { get; set; }

    /// <summary>Timer name for "StartTimer" and the "Timer" regulator.</summary>
    public string Timer { get; set; } = "";

    /// <summary>Gene id silenced by "SilenceGene".</summary>
    public string Gene { get; set; } = "";

    /// <summary>Rate: emission/grow/divide/move amount, or timer duration.</summary>
    public double Rate { get; set; } = 0.0;

    /// <summary>Duration for "StartTimer" (seconds).</summary>
    public double Duration { get; set; } = 0.0;

    /// <summary>Per-second apoptosis probability; 0 or less kills deterministically.</summary>
    public double Probability { get; set; } = 0.0;

    /// <summary>For "EmitMorphogen": spatial emission radius in world units (0 = point source).</summary>
    public double EmissionRadius { get; set; } = 0.0;

    /// <summary>For "Move": when true, move opposite to the morphogen gradient (repel).</summary>
    public bool Repel { get; set; } = false;
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
