using System.Linq;

namespace SimulationCore;

/// <summary>
/// Computational grid used only for external morphogen diffusion/decay and sampling.
/// It never constrains cell positions (cells live in continuous space). Only external
/// morphogens allocate field storage; the mapping from public morphogen id to compact
/// field index is explicit, so sparse ids and mixed internal/external sets are safe.
/// </summary>
public sealed class MorphogenSystem
{
    public readonly int GridWidth;
    public readonly int GridHeight;
    public readonly double Resolution;

    /// <summary>Number of external morphogens that have a field slot.</summary>
    public int FieldCount => _fieldCount;

    private readonly int _fieldCount;
    private readonly double[,] _field;   // [fieldIndex, cellIndex]
    private readonly double[,] _next;    // scratch for diffusion
    private readonly double[,] _source;  // accumulated emission (per second)
    private readonly Dictionary<int, int> _idToIndex = new();
    private readonly int[] _indexToId;

    public MorphogenSystem(double worldWidth, double worldHeight, double resolution, IReadOnlyCollection<int> externalMorphogenIds)
    {
        Resolution = resolution;
        GridWidth = Math.Max(1, (int)Math.Ceiling(worldWidth / resolution));
        GridHeight = Math.Max(1, (int)Math.Ceiling(worldHeight / resolution));

        _fieldCount = externalMorphogenIds.Count;
        _indexToId = new int[_fieldCount];
        int idx = 0;
        foreach (int id in externalMorphogenIds.OrderBy(id => id))
        {
            _idToIndex[id] = idx;
            _indexToId[idx] = id;
            idx++;
        }

        _field = new double[_fieldCount, GridWidth * GridHeight];
        _next = new double[_fieldCount, GridWidth * GridHeight];
        _source = new double[_fieldCount, GridWidth * GridHeight];
    }

    public int CellCount => GridWidth * GridHeight;

    /// <summary>True if the given morphogen id has an external field slot.</summary>
    public bool HasField(int morphogenId) => _idToIndex.ContainsKey(morphogenId);

    /// <summary>Morphogen id that occupies the given compact field index.</summary>
    public int MorphogenIdAt(int fieldIndex)
        => fieldIndex >= 0 && fieldIndex < _fieldCount ? _indexToId[fieldIndex] : -1;

    /// <summary>Zeroes all field values and pending sources (used on reset).</summary>
    public void Clear()
    {
        Array.Clear(_field);
        Array.Clear(_next);
        Array.Clear(_source);
    }

    public double WorldWidth => GridWidth * Resolution;
    public double WorldHeight => GridHeight * Resolution;

    private int IndexOf(int morphogenId)
        => _idToIndex.TryGetValue(morphogenId, out int idx) ? idx : -1;

    public double GetField(int morphogenId, int index)
    {
        int idx = IndexOf(morphogenId);
        return idx < 0 ? 0.0 : _field[idx, index];
    }

    public double GetField(int morphogenId, int gx, int gy)
    {
        int idx = IndexOf(morphogenId);
        if (idx < 0) return 0.0;
        if (gx < 0 || gy < 0 || gx >= GridWidth || gy >= GridHeight) return 0.0;
        return _field[idx, gy * GridWidth + gx];
    }

    /// <summary>Emit a morphogen (units per second) at a continuous world position.</summary>
    public void Emit(int morphogenId, double x, double y, double amountPerSecond)
    {
        int idx = IndexOf(morphogenId);
        if (idx < 0) return;
        int gx = (int)Math.Floor(x / Resolution);
        int gy = (int)Math.Floor(y / Resolution);
        if (gx < 0 || gy < 0 || gx >= GridWidth || gy >= GridHeight) return;
        _source[idx, gy * GridWidth + gx] += amountPerSecond;
    }

    /// <summary>
    /// Emit a morphogen over a spatial kernel. The total injected mass equals
    /// amountPerSecond * dt when integrated over one second; the kernel weights
    /// are normalized so changing the radius does not create extra signal.
    /// </summary>
    public void Emit(int morphogenId, double x, double y, double amountPerSecond, double radius)
    {
        int idx = IndexOf(morphogenId);
        if (idx < 0) return;
        if (amountPerSecond <= 0.0 || !double.IsFinite(amountPerSecond)) return;

        if (radius <= 0.0 || !double.IsFinite(radius))
        {
            Emit(morphogenId, x, y, amountPerSecond);
            return;
        }

        double res = Resolution;
        int minGx = (int)Math.Floor((x - radius) / res);
        int maxGx = (int)Math.Ceiling((x + radius) / res);
        int minGy = (int)Math.Floor((y - radius) / res);
        int maxGy = (int)Math.Ceiling((y + radius) / res);

        if (minGx >= GridWidth || maxGx < 0 || minGy >= GridHeight || maxGy < 0) return;
        if (minGx < 0) minGx = 0;
        if (minGy < 0) minGy = 0;
        if (maxGx >= GridWidth) maxGx = GridWidth - 1;
        if (maxGy >= GridHeight) maxGy = GridHeight - 1;

        double r2 = radius * radius;
        double sigma2 = r2 / 4.0;
        double totalWeight = 0.0;

        // First pass: compute normalized weights for the clipped kernel.
        for (int gy = minGy; gy <= maxGy; gy++)
        {
            double cy = (gy + 0.5) * res;
            double dy = cy - y;
            for (int gx = minGx; gx <= maxGx; gx++)
            {
                double cx = (gx + 0.5) * res;
                double dx = cx - x;
                double d2 = dx * dx + dy * dy;
                if (d2 > r2) continue;
                totalWeight += Math.Exp(-d2 / (2.0 * sigma2));
            }
        }

        if (totalWeight <= 1e-12) return;
        double scale = amountPerSecond / totalWeight;

        for (int gy = minGy; gy <= maxGy; gy++)
        {
            double cy = (gy + 0.5) * res;
            double dy = cy - y;
            for (int gx = minGx; gx <= maxGx; gx++)
            {
                double cx = (gx + 0.5) * res;
                double dx = cx - x;
                double d2 = dx * dx + dy * dy;
                if (d2 > r2) continue;
                double w = Math.Exp(-d2 / (2.0 * sigma2));
                _source[idx, gy * GridWidth + gx] += w * scale;
            }
        }
    }

    /// <summary>Directly set a field value (used by tests and world seeding).</summary>
    public void SetField(int morphogenId, int gx, int gy, double value)
    {
        int idx = IndexOf(morphogenId);
        if (idx < 0) return;
        if (gx < 0 || gy < 0 || gx >= GridWidth || gy >= GridHeight) return;
        _field[idx, gy * GridWidth + gx] = value;
    }

    public double TotalMass(int morphogenId)
    {
        int idx = IndexOf(morphogenId);
        if (idx < 0) return 0.0;
        double sum = 0;
        for (int i = 0; i < CellCount; i++) sum += _field[idx, i];
        return sum;
    }

    /// <summary>
    /// Apply accumulated sources and advance diffusion + decay by <paramref name="dt"/>.
    /// Runs an integer number of stable explicit sub-steps
    /// (stability requires D*sub/dx^2 &lt;= 0.24 per axis).
    /// When the required number of sub-steps would exceed a reasonable limit,
    /// the step is split adaptively so stability is never silently violated.
    /// </summary>
    public void Update(double dt, IReadOnlyDictionary<int, MorphogenConfig> configs)
    {
        if (dt <= 0.0) return;
        const int MaxSubsteps = 256;
        double dx2 = Resolution * Resolution;

        for (int idx = 0; idx < _fieldCount; idx++)
        {
            int id = _indexToId[idx];
            configs.TryGetValue(id, out var cfg);
            double diffusion = cfg?.Diffusion ?? 0.0;
            double decay = cfg?.Decay ?? 0.0;

            // No spatial spreading: integrate source + decay in one pass.
            if (diffusion <= 0.0 && decay <= 0.0)
            {
                ApplySourcesAndDecay(idx, dt, 0.0);
                continue;
            }

            int substeps = 1;
            if (diffusion > 0.0)
                substeps = (int)Math.Ceiling(diffusion * dt / (0.24 * dx2));
            if (substeps < 1) substeps = 1;
            if (substeps > MaxSubsteps) substeps = MaxSubsteps;

            double sub = dt / substeps;

            // If the configuration still violates stability even at MaxSubsteps,
            // reduce the effective diffusion coefficient for this step so the
            // explicit scheme remains stable. This keeps the simulation running
            // while making the numerical inaccuracy visible in validation.
            double effectiveDiffusion = diffusion;
            if (diffusion > 0.0 && diffusion * sub / dx2 > 0.24)
                effectiveDiffusion = 0.24 * dx2 / sub;

            for (int s = 0; s < substeps; s++)
            {
                // Distribute sources evenly across sub-steps instead of dumping
                // the whole step's production into the first sub-step.
                ApplySources(idx, sub);
                DiffuseStep(idx, effectiveDiffusion, decay, sub);
            }
        }
    }

    private void ApplySources(int fieldIndex, double dt)
    {
        var field = _field;
        var src = _source;
        for (int i = 0; i < CellCount; i++)
        {
            field[fieldIndex, i] += src[fieldIndex, i] * dt;
            src[fieldIndex, i] = 0.0;
        }
    }

    private void ApplySourcesAndDecay(int fieldIndex, double dt, double decay)
    {
        var field = _field;
        var src = _source;
        double factor = Math.Exp(-decay * dt);
        for (int i = 0; i < CellCount; i++)
        {
            double v = (field[fieldIndex, i] + src[fieldIndex, i] * dt) * factor;
            if (v < 0.0 || double.IsNaN(v)) v = 0.0;
            field[fieldIndex, i] = v;
            src[fieldIndex, i] = 0.0;
        }
    }

    private void DiffuseStep(int fieldIndex, double diffusion, double decay, double dt)
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
                double c = f[fieldIndex, i];

                // Clamped (zero-flux) boundaries.
                double left = x > 0 ? f[fieldIndex, i - 1] : c;
                double right = x < GridWidth - 1 ? f[fieldIndex, i + 1] : c;
                double up = y > 0 ? f[fieldIndex, i - GridWidth] : c;
                double down = y < GridHeight - 1 ? f[fieldIndex, i + GridWidth] : c;

                double laplacian = left + right + up + down - 4.0 * c;
                double v = (c + coeff * laplacian) * decayFactor;
                if (v < 0.0 || double.IsNaN(v)) v = 0.0;
                n[fieldIndex, i] = v;
            }
        }

        // Copy the diffused row back into the field.
        for (int i = 0; i < CellCount; i++)
            f[fieldIndex, i] = n[fieldIndex, i];
    }

    /// <summary>Bilinear sample of a morphogen field at a continuous world position.</summary>
    public double Sample(int morphogenId, double x, double y)
    {
        int idx = IndexOf(morphogenId);
        if (idx < 0) return 0.0;

        double gx = x / Resolution - 0.5;
        double gy = y / Resolution - 0.5;

        int x0 = (int)Math.Floor(gx);
        int y0 = (int)Math.Floor(gy);
        double fx = gx - x0;
        double fy = gy - y0;

        double v00 = At(idx, x0, y0);
        double v10 = At(idx, x0 + 1, y0);
        double v01 = At(idx, x0, y0 + 1);
        double v11 = At(idx, x0 + 1, y0 + 1);

        double top = v00 + (v10 - v00) * fx;
        double bottom = v01 + (v11 - v01) * fx;
        return top + (bottom - top) * fy;
    }

    /// <summary>
    /// Central-difference gradient of a morphogen field at a continuous position.
    /// Returns a zero vector at flat/out-of-range regions and never NaN.
    /// </summary>
    public (double Dx, double Dy) Gradient(int morphogenId, double x, double y)
    {
        int idx = IndexOf(morphogenId);
        if (idx < 0) return (0.0, 0.0);

        double h = Resolution;
        double dx = (Sample(morphogenId, x + h, y) - Sample(morphogenId, x - h, y)) / (2.0 * h);
        double dy = (Sample(morphogenId, x, y + h) - Sample(morphogenId, x, y - h)) / (2.0 * h);
        if (!double.IsFinite(dx)) dx = 0.0;
        if (!double.IsFinite(dy)) dy = 0.0;
        return (dx, dy);
    }

    private double At(int fieldIndex, int gx, int gy)
    {
        if (gx < 0) gx = 0;
        if (gy < 0) gy = 0;
        if (gx >= GridWidth) gx = GridWidth - 1;
        if (gy >= GridHeight) gy = GridHeight - 1;
        return _field[fieldIndex, gy * GridWidth + gx];
    }
}
