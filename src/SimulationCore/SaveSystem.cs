using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimulationCore;

/// <summary>
/// Saves and loads world state (cells + morphogen fields) and experiment
/// settings as JSON. Config loading lives in <see cref="ConfigLoader"/>;
/// this handles the mutable runtime snapshot.
/// </summary>
public static class SaveSystem
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public sealed class CellDto
    {
        public int Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Radius { get; set; }
        public int TypeId { get; set; }
        public int State { get; set; }
        public double Energy { get; set; }
        public double Age { get; set; }
        public double DivisionProgress { get; set; }
        public double StarveTimer { get; set; }
        public double DeathTimer { get; set; }
    }

    public sealed class WorldDto
    {
        public int Version { get; set; } = 1;
        public double Time { get; set; }
        public long Tick { get; set; }
        public int NextId { get; set; }
        public ulong RngState { get; set; }
        public double WorldWidth { get; set; }
        public double WorldHeight { get; set; }
        public double MorphogenResolution { get; set; }
        public int MorphogenCount { get; set; }
        public List<CellDto> Cells { get; set; } = new();
        public List<double[]> MorphogenFields { get; set; } = new();
    }

    public static void Save(string path, Simulation sim)
    {
        var dto = new WorldDto
        {
            Time = sim.Time,
            Tick = sim.Tick,
            NextId = sim.NextCellId,
            RngState = sim.Rng.State,
            WorldWidth = sim.Morphogens.WorldWidth,
            WorldHeight = sim.Morphogens.WorldHeight,
            MorphogenResolution = sim.Morphogens.Resolution,
            MorphogenCount = sim.Morphogens.MorphogenCount
        };

        foreach (var c in sim.Cells)
        {
            dto.Cells.Add(new CellDto
            {
                Id = c.Id,
                X = c.X,
                Y = c.Y,
                Radius = c.Radius,
                TypeId = c.TypeId,
                State = (int)c.State,
                Energy = c.Energy,
                Age = c.Age,
                DivisionProgress = c.DivisionProgress,
                StarveTimer = c.StarveTimer,
                DeathTimer = c.DeathTimer
            });
        }

        for (int m = 0; m < sim.Morphogens.MorphogenCount; m++)
        {
            var field = new double[sim.Morphogens.CellCount];
            for (int i = 0; i < field.Length; i++)
                field[i] = sim.Morphogens.GetField(m, i);
            dto.MorphogenFields.Add(field);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(dto, Options));
    }

    public static void Load(string path, Simulation sim)
    {
        var dto = JsonSerializer.Deserialize<WorldDto>(File.ReadAllText(path), Options)
                  ?? throw new InvalidDataException($"Failed to parse {path}");

        sim.Cells.Clear();
        foreach (var d in dto.Cells)
        {
            var cell = new Cell
            {
                Id = d.Id,
                X = d.X,
                Y = d.Y,
                Radius = d.Radius,
                TypeId = d.TypeId,
                State = (CellState)d.State,
                Energy = d.Energy,
                Age = d.Age,
                DivisionProgress = d.DivisionProgress,
                StarveTimer = d.StarveTimer,
                DeathTimer = d.DeathTimer
            };
            if (sim.CellTypesById.TryGetValue(cell.TypeId, out var type))
                cell.EnsureGeneState(type.Genome.Count);
            sim.Cells.Add(cell);
        }

        sim.RestoreRuntime(dto.Time, dto.Tick, dto.NextId, dto.RngState);

        int m = Math.Min(dto.MorphogenFields.Count, sim.Morphogens.MorphogenCount);
        for (int k = 0; k < m; k++)
        {
            var field = dto.MorphogenFields[k];
            for (int i = 0; i < field.Length && i < sim.Morphogens.CellCount; i++)
            {
                int gx = i % sim.Morphogens.GridWidth;
                int gy = i / sim.Morphogens.GridWidth;
                sim.Morphogens.SetField(k, gx, gy, field[i]);
            }
        }
    }
}
