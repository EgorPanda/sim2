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

/// <summary>Definition of a cell type together with its genome (config/genomes.json).</summary>
public sealed class CellTypeConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = "cell";
    public string Color { get; set; } = "#ffffff";
    public double BaseRadius { get; set; } = 12.0;
    public double MaxRadius { get; set; } = 20.0;
    public double MaxEnergy { get; set; } = 120.0;
    public double MaintenanceCost { get; set; } = 1.5;
    public double DivisionEnergyCost { get; set; } = 50.0;
    public double GrowEnergyCostPerRadius { get; set; } = 1.0;
    public double StarveGracePeriod { get; set; } = 2.0;
    public List<GeneConfig> Genome { get; set; } = new();
}

/// <summary>A gene: regulators gate its activation, effects run while active.</summary>
public sealed class GeneConfig
{
    public string Name { get; set; } = "gene";
    public double ActivationDelay { get; set; } = 0.0;
    public List<RegulatorConfig> Regulators { get; set; } = new();
    public List<GeneEffectConfig> Effects { get; set; } = new();
}

/// <summary>Condition that gates a gene. All regulators of a gene are AND-combined.</summary>
public sealed class RegulatorConfig
{
    /// <summary>"Property" or "Morphogen".</summary>
    public string Kind { get; set; } = "Property";

    /// <summary>Property name when Kind == "Property": energy, radius, age, neighbours.</summary>
    public string Property { get; set; } = "";

    /// <summary>Morphogen index when Kind == "Morphogen".</summary>
    public int Morphogen { get; set; } = -1;

    public double Min { get; set; } = double.NegativeInfinity;
    public double Max { get; set; } = double.PositiveInfinity;
}

/// <summary>An effect applied every step while the owning gene is active.</summary>
public sealed class GeneEffectConfig
{
    /// <summary>"EmitMorphogen", "Absorb", "Grow" or "Divide".</summary>
    public string Kind { get; set; } = "";

    public int Morphogen { get; set; } = -1;
    public double Rate { get; set; } = 0.0;
}
