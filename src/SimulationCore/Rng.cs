namespace SimulationCore;

/// <summary>
/// Deterministic 64-bit PRNG (xorshift64*). Independent of the .NET runtime so
/// runs are reproducible across machines and framework versions.
/// </summary>
public sealed class Rng
{
    private ulong _state;

    public Rng(long seed)
    {
        SetSeed(seed);
    }

    public ulong State
    {
        get => _state;
        set => _state = value == 0 ? 0x9E3779B97F4A7C15UL : value;
    }

    public void SetSeed(long seed)
    {
        ulong s = (ulong)seed;
        State = s == 0 ? 0x9E3779B97F4A7C15UL : s;
    }

    public ulong NextULong()
    {
        ulong x = _state;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        _state = x;
        return x * 2685821657736338717UL;
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Uniform double in [min, max).</summary>
    public double Range(double min, double max) => min + (max - min) * NextDouble();

    /// <summary>Uniform int in [0, maxExclusive).</summary>
    public int NextInt(int maxExclusive) => maxExclusive <= 0 ? 0 : (int)(NextDouble() * maxExclusive);
}
