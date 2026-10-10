namespace SimulationCore;

/// <summary>
/// Computational grid used only for morphogen diffusion/decay and sampling.
/// It never constrains cell positions (cells live in continuous space) and is
/// purely an internal aid. Stores one scalar field per morphogen; the layout is
/// ready for ~12 morphogens without further structural change.
/// </summary>
public sealed class MorphogenSystem
{
    public readonly int GridWidth;
    public readonly int GridHeight;
    public readonly double Resolution;
    public readonly int MorphogenCount;

    private readonly double[,] _field;   // [morph, index]
    private readonly double[,] _next;    // scratch for diffusion
    private readonly double[,] _source;  // accumulated emission (per second)

    public MorphogenSystem(double worldWidth, double worldHeight, double resolution, int morphogenCount)
    {
        Resolution = resolution;
        GridWidth = Math.Max(1, (int)Math.Ceiling(worldWidth / resolution));
        GridHeight = Math.Max(1, (int)Math.Ceiling(worldHeight / resolution));
        MorphogenCount = morphogenCount;

        _field = new double[morphogenCount, GridWidth * GridHeight];
        _next = new double[morphogenCount, GridWidth * GridHeight];
        _source = new double[morphogenCount, GridWidth * GridHeight];
    }

    public int CellCount => GridWidth * GridHeight;

    public double WorldWidth => GridWidth * Resolution;
    public double WorldHeight => GridHeight * Resolution;

    public double GetField(int morph, int index) => _field[morph, index];
    public double GetField(int morph, int gx, int gy) => _field[morph, gy * GridWidth + gx];

    /// <summary>Emit a morphogen (units per second) at a continuous world position.</summary>
    public void Emit(int morph, double x, double y, double amountPerSecond)
    {
        int gx = (int)Math.Floor(x / Resolution);
        int gy = (int)Math.Floor(y / Resolution);
        if (gx < 0 || gy < 0 || gx >= GridWidth || gy >= GridHeight) return;
        _source[morph, gy * GridWidth + gx] += amountPerSecond;
    }

    /// <summary>Directly set a field value (used by tests and world seeding).</summary>
    public void SetField(int morph, int gx, int gy, double value)
    {
        if (gx < 0 || gy < 0 || gx >= GridWidth || gy >= GridHeight) return;
        _field[morph, gy * GridWidth + gx] = value;
    }

    public double TotalMass(int morph)
    {
        double sum = 0;
        for (int i = 0; i < CellCount; i++) sum += _field[morph, i];
        return sum;
    }

    /// <summary>
    /// Apply accumulated sources and advance diffusion + decay by <paramref name="dt"/>.
    /// Runs an integer number of stable explicit sub-steps
    /// (stability requires D*sub/dt/dx^2 &lt;= 0.25 per axis).
    /// </summary>
    public void Update(double dt, IReadOnlyDictionary<int, MorphogenConfig> configs)
    {
        for (int m = 0; m < MorphogenCount; m++)
        {
            configs.TryGetValue(m, out var cfg);
            double diffusion = cfg?.Diffusion ?? 0.0;
            double decay = cfg?.Decay ?? 0.0;

            // Integrate emission over the full step.
            if (diffusion <= 0 && decay <= 0)
            {
                ApplySourcesAndDecay(m, dt, 0.0);
                continue;
            }

            double dx2 = Resolution * Resolution;
            int substeps = 1;
            if (diffusion > 0)
                substeps = (int)Math.Ceiling(diffusion * dt / (0.24 * dx2));
            if (substeps < 1) substeps = 1;
            if (substeps > 64) substeps = 64;

            double sub = dt / substeps;
            for (int s = 0; s < substeps; s++)
            {
                if (s == 0) ApplySources(m, sub);
                DiffuseStep(m, diffusion, decay, sub);
            }
        }
    }

    private void ApplySources(int m, double dt)
    {
        var field = _field;
        var src = _source;
        for (int i = 0; i < CellCount; i++)
        {
            field[m, i] += src[m, i] * dt;
            src[m, i] = 0.0;
        }
    }

    private void ApplySourcesAndDecay(int m, double dt, double decay)
    {
        var field = _field;
        var src = _source;
        double factor = Math.Exp(-decay * dt);
        for (int i = 0; i < CellCount; i++)
        {
            double v = (field[m, i] + src[m, i] * dt) * factor;
            field[m, i] = v;
            src[m, i] = 0.0;
        }
    }

    private void DiffuseStep(int m, double diffusion, double decay, double dt)
    {
        double coeff = diffusion * dt / (Resolution * Resolution);
        double decayFactor = Math.Exp(-decay * dt);
        var f = _field;
        var n = _next;

        for (int y = 0; y < GridHeight; y++)
        {
            for (int x = 0; x < GridWidth; x++)
            {
                int i = y * GridWidth + x;
                double c = f[m, i];

                // Clamped (zero-flux) boundaries.
                double left = x > 0 ? f[m, i - 1] : c;
                double right = x < GridWidth - 1 ? f[m, i + 1] : c;
                double up = y > 0 ? f[m, i - GridWidth] : c;
                double down = y < GridHeight - 1 ? f[m, i + GridWidth] : c;

                double laplacian = left + right + up + down - 4.0 * c;
                n[m, i] = (c + coeff * laplacian) * decayFactor;
            }
        }

        // Copy the diffused row back into the field.
        for (int i = 0; i < CellCount; i++)
            f[m, i] = n[m, i];
    }

    /// <summary>Bilinear sample of a morphogen field at a continuous world position.</summary>
    public double Sample(int morph, double x, double y)
    {
        double gx = x / Resolution - 0.5;
        double gy = y / Resolution - 0.5;

        int x0 = (int)Math.Floor(gx);
        int y0 = (int)Math.Floor(gy);
        double fx = gx - x0;
        double fy = gy - y0;

        double v00 = At(morph, x0, y0);
        double v10 = At(morph, x0 + 1, y0);
        double v01 = At(morph, x0, y0 + 1);
        double v11 = At(morph, x0 + 1, y0 + 1);

        double top = v00 + (v10 - v00) * fx;
        double bottom = v01 + (v11 - v01) * fx;
        return top + (bottom - top) * fy;
    }

    private double At(int morph, int gx, int gy)
    {
        if (gx < 0) gx = 0;
        if (gy < 0) gy = 0;
        if (gx >= GridWidth) gx = GridWidth - 1;
        if (gy >= GridHeight) gy = GridHeight - 1;
        return _field[morph, gy * GridWidth + gx];
    }
}
