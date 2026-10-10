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
            RngSeed = seed
        };

        var morphogens = new MorphogenConfig[morphogenCount];
        for (int i = 0; i < morphogenCount; i++)
            morphogens[i] = new MorphogenConfig { Id = i, Name = $"m{i}", Diffusion = diffusion, Decay = decay };

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
            Genome = new List<GeneConfig>(genome)
        };

        return new SimulationSetup(sim, morphogens, new[] { type });
    }

    public static GeneConfig Gene(string name, params GeneEffectConfig[] effects)
        => new() { Name = name, Effects = new List<GeneEffectConfig>(effects) };

    public static GeneEffectConfig Emit(int morph, double rate) =>
        new() { Kind = "EmitMorphogen", Morphogen = morph, Rate = rate };

    public static GeneEffectConfig Absorb(int morph, double rate) =>
        new() { Kind = "Absorb", Morphogen = morph, Rate = rate };

    public static GeneEffectConfig Grow(double rate) =>
        new() { Kind = "Grow", Rate = rate };

    public static GeneEffectConfig Divide(double rate) =>
        new() { Kind = "Divide", Rate = rate };
}
