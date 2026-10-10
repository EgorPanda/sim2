using SimulationCore;

namespace SimulationCore.Tests;

/// <summary>Shared helpers for building minimal simulations in tests.</summary>
internal static class TestFactory
{
    public static SimulationSetup Setup(
        int morphogenCount = 1,
        double diffusion = 0.0,
        double decay = 0.0,
        double world = 256.0,
        double resolution = 8.0,
        int initialCells = 1,
        int seed = 123,
        int founderTypeId = 0,
        int genomeId = 0,
        AdhesionConfig? adhesion = null,
        params GeneConfig[] genome)
    {
        var sim = new SimulationConfig
        {
            WorldWidth = world,
            WorldHeight = world,
            MorphogenResolution = resolution,
            FixedTimeStep = 0.05,
            CellRadius = 8.0,
            InitialCells = initialCells,
            RngSeed = seed,
            FounderTypeId = founderTypeId,
            DivisionGradientMorphogen = -1
        };

        var morphogens = new MorphogenConfig[morphogenCount];
        for (int i = 0; i < morphogenCount; i++)
            morphogens[i] = new MorphogenConfig { Id = i, Name = $"m{i}", Diffusion = diffusion, Decay = decay };

        var genomes = new[]
        {
            new GenomeConfig { Id = genomeId, Name = "g0", Genes = new List<GeneConfig>(genome) }
        };

        var type = new CellTypeConfig
        {
            Id = 0,
            Name = "test",
            BaseRadius = 8.0,
            MaxRadius = 12.0,
            MaxEnergy = 100.0,
            MaintenanceCost = 0.0,
            DivisionEnergyCost = 10.0,
            GrowEnergyCostPerRadius = 0.0,
            StarveGracePeriod = 1.0,
            GenomeId = genomeId
        };

        return new SimulationSetup(sim, morphogens, genomes, new[] { type }, adhesion);
    }

    public static CellTypeConfig AddType(
        SimulationSetup setup,
        int typeId,
        string name,
        string color = "#ffffff",
        int genomeId = 0,
        double? baseRadius = null,
        double? maxRadius = null,
        double? maxEnergy = null,
        double? divisionCost = null)
    {
        var existing = setup.CellTypes.ToList();
        var newType = new CellTypeConfig
        {
            Id = typeId,
            Name = name,
            Color = color,
            BaseRadius = baseRadius ?? 8.0,
            MaxRadius = maxRadius ?? 12.0,
            MaxEnergy = maxEnergy ?? 100.0,
            MaintenanceCost = 0.0,
            DivisionEnergyCost = divisionCost ?? 10.0,
            GrowEnergyCostPerRadius = 0.0,
            StarveGracePeriod = 1.0,
            GenomeId = genomeId
        };
        existing.Add(newType);
        setup.CellTypes = existing.ToArray();
        return newType;
    }

    public static SimulationSetup CloneSetup(SimulationSetup setup)
        => new(setup.Simulation, setup.Morphogens, setup.Genomes, setup.CellTypes, setup.Adhesion);

    public static GeneConfig Gene(string name, params GeneEffectConfig[] effects)
        => new() { Name = name, Id = name, Effects = new List<GeneEffectConfig>(effects) };

    public static GeneEffectConfig Emit(int morph, double rate) =>
        new() { Kind = "EmitMorphogen", Morphogen = morph, Rate = rate };

    public static GeneEffectConfig Absorb(int morph, double rate) =>
        new() { Kind = "Absorb", Morphogen = morph, Rate = rate };

    public static GeneEffectConfig Grow(double rate) =>
        new() { Kind = "Grow", Rate = rate };

    public static GeneEffectConfig Divide(double rate) =>
        new() { Kind = "Divide", Rate = rate };

    public static GeneEffectConfig Divide(int? childTypeId, double rate) =>
        new() { Kind = "Divide", Rate = rate, ChildTypeId = childTypeId };
}