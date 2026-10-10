namespace SimulationCore;

/// <summary>
/// Authoritative world state and fixed-step orchestration. This class has no
/// dependency on Godot; the front-end only reads its state and submits commands.
///
/// Fixed update order (deterministic):
///   1. spatial index rebuild + neighbour counts
///   2. metabolism / ageing / starvation
///   3. genome evaluation (emission, absorption, growth, division progress)
///   4. morphogen emission -> diffusion -> decay
///   5. cell mechanics (overlap resolution, world clamping)
///   6. division & apoptosis resolution
/// </summary>
public sealed class Simulation : GeneContext
{
    public SimulationConfig Config { get; }
    public IReadOnlyDictionary<int, MorphogenConfig> MorphogensById { get; }
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

        var types = new Dictionary<int, CellTypeConfig>();
        foreach (var t in setup.CellTypes)
        {
            types[t.Id] = t;
            _maxRadius = Math.Max(_maxRadius, t.MaxRadius);
        }
        CellTypesById = types;

        Morphogens = new MorphogenSystem(
            Config.WorldWidth, Config.WorldHeight,
            Config.MorphogenResolution, Math.Max(1, morphogens.Count));

        SpatialHash = new SpatialHash(_maxRadius * 2.0 + 2.0);
        CellSystem = new CellSystem(Cells);
        Rng = new Rng(Config.RngSeed);
        _worldSize = new Vector(Config.WorldWidth, Config.WorldHeight);

        Reset();
    }

    /// <summary>Clears the world and spawns the initial founder cell(s) at the centre.</summary>
    public void Reset()
    {
        Cells.Clear();
        _nextId = 0;
        Time = 0.0;
        Tick = 0;
        Rng.SetSeed(Config.RngSeed);

        int initial = Math.Max(1, Config.InitialCells);
        int typeId = CellTypesById.Count > 0 ? FirstTypeId() : 0;
        if (!CellTypesById.TryGetValue(typeId, out var type)) return;

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
                Energy = type.MaxEnergy * 0.6,
                State = CellState.Alive
            };
            cell.EnsureGeneState(type.Genome.Count);
            Cells.Add(cell);
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

        // 2. Metabolism / ageing / starvation.
        CellSystem.Metabolism(dt, CellTypesById);

        // 3. Genome evaluation.
        for (int i = 0; i < Cells.Count; i++)
        {
            var cell = Cells[i];
            if (cell.State != CellState.Alive) continue;
            if (!CellTypesById.TryGetValue(cell.TypeId, out var type)) continue;
            GenomeSystem.Evaluate(cell, type, this, dt);
        }

        // 4. Morphogen dynamics.
        Morphogens.Update(dt, MorphogensById);

        // 5. Mechanics.
        Mechanics.Resolve(Cells, SpatialHash, _maxRadius);
        ClampToWorld();

        // 6. Division & apoptosis.
        SpatialHash.Rebuild(Cells);
        CellSystem.ResolveDivisions(SpatialHash, CellTypesById, Rng, CreateCell, _maxRadius);
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
            Energy = (type?.MaxEnergy ?? 100.0) * 0.6,
            State = CellState.Alive
        };
        cell.EnsureGeneState(type?.Genome.Count ?? 0);
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

    public double SampleMorphogen(int morphogen, double x, double y)
        => Morphogens.Sample(morphogen, x, y);

    public void EmitMorphogen(int morphogen, double x, double y, double amountPerSecond)
        => Morphogens.Emit(morphogen, x, y, amountPerSecond);
}
