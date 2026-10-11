namespace SimulationCore;

/// <summary>
/// Authoritative world state and fixed-step orchestration. This class has no
/// dependency on Godot; the front-end only reads its state and submits commands.
///
/// Fixed update order (deterministic):
///   1. spatial index rebuild + neighbour counts
///   2. ageing
///   3. genome evaluation (emission, growth, movement, division progress)
///   4. morphogen emission -> diffusion -> decay
///   5. cell mechanics (overlap resolution with tissue adhesion, world clamping)
///   6. division & apoptosis resolution
/// </summary>
public sealed class Simulation : GeneContext
{
    public SimulationConfig Config { get; }
    public IReadOnlyDictionary<int, MorphogenConfig> MorphogensById { get; }
    public IReadOnlyDictionary<int, GenomeConfig> GenomesById { get; }
    public IReadOnlyDictionary<int, CellTypeConfig> CellTypesById { get; }

    public List<Cell> Cells { get; } = new();
    public MorphogenSystem Morphogens { get; }
    public SpatialHash SpatialHash { get; }
    public CellSystem CellSystem { get; }
    public CellMechanics Mechanics { get; } = new();
    public Rng Rng { get; }

    public double Time { get; private set; }
    public long Tick { get; private set; }

    private readonly List<int> _queryScratch = new();
    private readonly Vector _worldSize;
    private readonly Func<int, int, double> _adhesion;
    private readonly Func<double, double, (double, double)>? _divisionGradient;
    private int _nextId;
    private double _maxRadius = 32.0;

    private readonly struct Vector
    {
        public readonly double X;
        public readonly double Y;
        public Vector(double x, double y) { X = x; Y = y; }
    }

    public Simulation(SimulationSetup setup)
    {
        Config = setup.Simulation;

        var morphogens = new Dictionary<int, MorphogenConfig>();
        foreach (var m in setup.Morphogens) morphogens[m.Id] = m;
        MorphogensById = morphogens;

        var genomes = new Dictionary<int, GenomeConfig>();
        foreach (var g in setup.Genomes) genomes[g.Id] = g;
        GenomesById = genomes;

        var types = new Dictionary<int, CellTypeConfig>();
        foreach (var t in setup.CellTypes)
        {
            types[t.Id] = t;
            _maxRadius = Math.Max(_maxRadius, t.MaxRadius);
        }
        CellTypesById = types;

        int morphCount = 0;
        foreach (var id in morphogens.Keys)
        {
            if (!morphogens[id].IsInternal)
                morphCount = Math.Max(morphCount, id + 1);
        }

        Morphogens = new MorphogenSystem(
            Config.WorldWidth, Config.WorldHeight,
            Config.MorphogenResolution, Math.Max(1, morphCount));

        _adhesion = BuildAdhesion(setup.Adhesion);
        Mechanics.Adhesion = (a, b) => _adhesion(a, b);

        _divisionGradient = BuildDivisionGradient(Config.DivisionGradientMorphogen);

        SpatialHash = new SpatialHash(_maxRadius * 2.0 + 2.0);
        CellSystem = new CellSystem(Cells);
        Rng = new Rng(Config.RngSeed);
        _worldSize = new Vector(Config.WorldWidth, Config.WorldHeight);

        Reset();
    }

    private Func<int, int, double> BuildAdhesion(AdhesionConfig adhesion)
    {
        var pairs = new Dictionary<(int, int), double>();
        foreach (var p in adhesion.Pairs) pairs[(Math.Min(p.A, p.B), Math.Max(p.A, p.B))] = p.Strength;
        Mechanics.MaxOverlap = adhesion.MaxOverlap;

        return (a, b) =>
        {
            (int lo, int hi) = a <= b ? (a, b) : (b, a);
            return pairs.TryGetValue((lo, hi), out double strength) ? strength : adhesion.Default;
        };
    }

    private Func<double, double, (double, double)>? BuildDivisionGradient(int morph) =>
        morph >= 0 && MorphogensById.ContainsKey(morph)
            ? (x, y) => Morphogens.Gradient(morph, x, y)
            : null;

    /// <summary>Clears the world and spawns the initial founder cell(s) at the centre.</summary>
    public void Reset()
    {
        Cells.Clear();
        _nextId = 0;
        Time = 0.0;
        Tick = 0;
        Rng.SetSeed(Config.RngSeed);
        Morphogens.Clear();

        int initial = Math.Max(1, Config.InitialCells);
        int typeId = Config.FounderTypeId;
        if (!CellTypesById.TryGetValue(typeId, out var type))
            typeId = FirstTypeId();
        if (!CellTypesById.TryGetValue(typeId, out type)) return;

        double cx = Config.WorldWidth * 0.5;
        double cy = Config.WorldHeight * 0.5;

        for (int i = 0; i < initial; i++)
        {
            double angle = initial == 1 ? 0.0 : (Math.PI * 2.0 * i) / initial;
            double spacing = initial == 1 ? 0.0 : type.BaseRadius * 2.2;
            var cell = new Cell
            {
                Id = _nextId++,
                X = cx + Math.Cos(angle) * spacing,
                Y = cy + Math.Sin(angle) * spacing,
                Radius = type.BaseRadius,
                TypeId = typeId,
                GenomeId = type.GenomeId,
                State = CellState.Alive
            };
            ApplyInitialInternalMorphogens(cell);
            Cells.Add(cell);
        }

        SeedInitialExternalMorphogens();
    }

    public GenomeConfig? GenomeOf(int genomeId)
    {
        GenomesById.TryGetValue(genomeId, out var genome);
        return genome;
    }

    private void ApplyInitialInternalMorphogens(Cell cell)
    {
        foreach (var kv in Config.InitialInternalMorphogens)
        {
            if (!MorphogensById.TryGetValue(kv.Key, out var cfg)) continue;
            if (!cfg.IsInternal) continue;
            double v = kv.Value;
            if (double.IsNaN(v) || v < 0.0) v = 0.0;
            cell.InternalMorphogenConcentrations[kv.Key] = v;
        }

        foreach (var cfg in MorphogensById.Values)
        {
            if (!cfg.IsInternal) continue;
            if (cfg.InitialConcentration > 0.0)
                cell.InternalMorphogenConcentrations[cfg.Id] = cfg.InitialConcentration;
        }
    }

    private void SeedInitialExternalMorphogens()
    {
        foreach (var source in Config.InitialExternalSources)
        {
            if (source.MorphogenId < 0) continue;
            if (!MorphogensById.TryGetValue(source.MorphogenId, out var cfg)) continue;
            if (cfg.IsInternal) continue;

            double r2 = source.Radius * source.Radius;
            double cx = source.X;
            double cy = source.Y;
            double res = Morphogens.Resolution;
            int gw = Morphogens.GridWidth;
            int gh = Morphogens.GridHeight;

            for (int gy = 0; gy < gh; gy++)
            {
                double y = (gy + 0.5) * res;
                double dy = y - cy;
                for (int gx = 0; gx < gw; gx++)
                {
                    double x = (gx + 0.5) * res;
                    double dx = x - cx;
                    double d2 = dx * dx + dy * dy;
                    if (d2 > r2) continue;

                    double value = source.Concentration;
                    if (string.Equals(source.Profile, "gaussian", StringComparison.OrdinalIgnoreCase))
                    {
                        double sigma = source.Radius / 3.0;
                        value *= Math.Exp(-d2 / (2.0 * sigma * sigma));
                    }
                    Morphogens.SetField(source.MorphogenId, gx, gy, value);
                }
            }
        }
    }

    private void ApplyInternalDecay(double dt)
    {
        foreach (var cell in Cells)
        {
            if (cell.InternalMorphogenConcentrations.Count == 0) continue;
            foreach (var id in cell.InternalMorphogenConcentrations.Keys.ToArray())
            {
                if (!MorphogensById.TryGetValue(id, out var cfg)) continue;
                if (!cfg.IsInternal) continue;

                double v = cell.InternalMorphogenConcentrations[id];
                v *= Math.Exp(-cfg.Decay * dt);
                if (v <= 1e-12 || double.IsNaN(v))
                    cell.InternalMorphogenConcentrations.Remove(id);
                else
                    cell.InternalMorphogenConcentrations[id] = v;
            }
        }
    }

    private int FirstTypeId()
    {
        int best = int.MaxValue;
        foreach (var id in CellTypesById.Keys) best = Math.Min(best, id);
        return best == int.MaxValue ? 0 : best;
    }

    /// <summary>Advance the simulation by one fixed step.</summary>
    public void Step(double dt)
    {
        if (dt <= 0) return;

        // 1. Spatial index + neighbour density.
        SpatialHash.Rebuild(Cells);
        UpdateNeighbourCounts();

        // 2. Ageing (gene effects fully control life and death).
        CellSystem.AgeCells(dt);

        // Division blocking is re-armed by the genome evaluation each step.
        for (int i = 0; i < Cells.Count; i++)
            Cells[i].DivisionBlocked = false;

        // 3. Genome evaluation.
        for (int i = 0; i < Cells.Count; i++)
        {
            var cell = Cells[i];
            if (cell.State != CellState.Alive) continue;
            if (!CellTypesById.TryGetValue(cell.TypeId, out var type)) continue;
            var genome = GenomeOf(type.GenomeId);
            if (genome == null) continue;
            GenomeSystem.Evaluate(cell, type, genome, this, dt);
        }

        // 4. Morphogen dynamics (external field + per-cell internal decay).
        Morphogens.Update(dt, MorphogensById);
        ApplyInternalDecay(dt);

        // 5. Mechanics.
        Mechanics.Resolve(Cells, SpatialHash, _maxRadius);
        ClampToWorld();

        // 6. Division & apoptosis.
        SpatialHash.Rebuild(Cells);
        CellSystem.ResolveDivisions(SpatialHash, CellTypesById, Rng, CreateCell, _maxRadius, _divisionGradient);
        CellSystem.Cleanup(dt);

        // Rebuild once more so the next step and the renderer see fresh data.
        SpatialHash.Rebuild(Cells);

        Time += dt;
        Tick++;
    }

    private void UpdateNeighbourCounts()
    {
        for (int i = 0; i < Cells.Count; i++)
        {
            var cell = Cells[i];
            SpatialHash.QueryCircle(cell.X, cell.Y, cell.Radius * 3.0, _queryScratch);
            int count = 0;
            for (int k = 0; k < _queryScratch.Count; k++)
            {
                int j = _queryScratch[k];
                if (j == i) continue;
                var other = Cells[j];
                if (other.State == CellState.Dying) continue;
                double dx = other.X - cell.X;
                double dy = other.Y - cell.Y;
                double r = cell.Radius + other.Radius;
                if (dx * dx + dy * dy <= r * r * 6.25)
                    count++;
            }
            cell.NeighbourCount = count;
        }
    }

    private void ClampToWorld()
    {
        double w = _worldSize.X;
        double h = _worldSize.Y;
        for (int i = 0; i < Cells.Count; i++)
        {
            var c = Cells[i];
            c.X = Num.Clamp(c.X, c.Radius, w - c.Radius);
            c.Y = Num.Clamp(c.Y, c.Radius, h - c.Radius);
        }
    }

    // --- Manual interaction -------------------------------------------------

    public Cell CreateCell() => new() { Id = _nextId++ };

    public int NextCellId => _nextId;

    /// <summary>Restores mutable runtime fields after loading a saved world.</summary>
    public void RestoreRuntime(double time, long tick, int nextId, ulong rngState)
    {
        Time = time;
        Tick = tick;
        _nextId = nextId;
        Rng.State = rngState;
        SpatialHash.Rebuild(Cells);
    }

    /// <summary>Add a cell of the given type at a position (used by the UI).</summary>
    public Cell AddCell(double x, double y, int typeId)
    {
        if (!CellTypesById.TryGetValue(typeId, out var type))
            typeId = FirstTypeId();
        CellTypesById.TryGetValue(typeId, out type);

        var cell = new Cell
        {
            Id = _nextId++,
            X = x,
            Y = y,
            Radius = type?.BaseRadius ?? Config.CellRadius,
            TypeId = typeId,
            GenomeId = type?.GenomeId ?? 0,
            State = CellState.Alive
        };
        Cells.Add(cell);
        return cell;
    }

    public bool RemoveCell(int id)
    {
        for (int i = 0; i < Cells.Count; i++)
        {
            if (Cells[i].Id == id)
            {
                Cells.RemoveAt(i);
                SpatialHash.Rebuild(Cells);
                return true;
            }
        }
        return false;
    }

    /// <summary>Finds the cell under a world-space point (topmost/nearest).</summary>
    public Cell? PickCell(double x, double y, double tolerance = 0.0)
    {
        Cell? best = null;
        double bestDistSq = double.MaxValue;
        for (int i = 0; i < Cells.Count; i++)
        {
            var c = Cells[i];
            double dx = c.X - x;
            double dy = c.Y - y;
            double distSq = dx * dx + dy * dy;
            double r = c.Radius + tolerance;
            if (distSq <= r * r && distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = c;
            }
        }
        return best;
    }

    // --- GeneContext --------------------------------------------------------

    Rng GeneContext.Rng => Rng;

    public double SampleMorphogen(int morphogen, double x, double y)
        => morphogen < 0 || morphogen >= Morphogens.MorphogenCount ? 0.0 : Morphogens.Sample(morphogen, x, y);

    public (double Dx, double Dy) MorphogenGradient(int morphogen, double x, double y)
    {
        if (morphogen < 0 || morphogen >= Morphogens.MorphogenCount) return (0.0, 0.0);
        return Morphogens.Gradient(morphogen, x, y);
    }

    public void EmitMorphogen(int morphogen, double x, double y, double amountPerSecond)
    {
        if (morphogen < 0 || morphogen >= Morphogens.MorphogenCount) return;
        Morphogens.Emit(morphogen, x, y, amountPerSecond);
    }

    public bool IsMorphogenInternal(int morphogen)
        => MorphogensById.TryGetValue(morphogen, out var cfg) && cfg.IsInternal;

    public double SampleInternalMorphogen(Cell cell, int morphogen)
        => cell.InternalMorphogenConcentrations.TryGetValue(morphogen, out double v) ? v : 0.0;

    public void EmitInternalMorphogen(Cell cell, int morphogen, double amountPerSecond, double dt)
    {
        double current = cell.InternalMorphogenConcentrations.TryGetValue(morphogen, out double v) ? v : 0.0;
        double next = Num.Clamp(current + amountPerSecond * dt, 0.0, 1e12);
        cell.InternalMorphogenConcentrations[morphogen] = next;
    }

    public void Kill(Cell cell) => CellSystem.Kill(cell);
}