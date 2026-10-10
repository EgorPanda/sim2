using Godot;
using SimulationCore;
using System;

namespace Sim2;

/// <summary>
/// Analytical display layer of the world. A layer changes how real simulation
/// data is visualised (cell type, morphogen concentration, gradient, gene
/// activity, silenced genes, energy, neighbour density) and never mutates the
/// simulation state.
/// </summary>
public enum WorldLayer
{
    CellTypes,
    Morphogen,
    Gradient,
    GeneActivity,
    GeneState,
    Energy,
    Neighbours
}

/// <summary>
/// Draws the world. Seven display layers over the cell bodies; concentration
/// heat-map (throttled) and gradient arrows. Purely presentational — it only
/// reads core state and never mutates it.
/// </summary>
public partial class WorldView : Node2D
{
    public Simulation? Sim { get; set; }
    public int? SelectedCellId { get; set; }

    public WorldLayer Layer { get; set; } = WorldLayer.CellTypes;
    public bool ShowCells { get; set; } = true;

    /// <summary>Heat-map on top when Layer == Morphogen; gradient/analysis otherwise.</summary>
    public bool ShowField => Layer == WorldLayer.Morphogen;

    public int MorphogenId { get; set; }

    /// <summary>Gene id highlighted by GeneActivity / GeneState layers.</summary>
    public string GeneId { get; set; } = "";

    /// <summary>Overlay cell tinting while the heat map is visible.</summary>
    public bool TintCellsOverField { get; set; } = true;

    /// <summary>Normalise the concentration heat map against its own peak.</summary>
    public bool NormalizeConcentration { get; set; } = true;

    public double FixedMin { get; set; }
    public double FixedMax { get; set; } = 1.0;

    // Gradient arrows.
    public int GradientDensity { get; set; } = 4;
    public double GradientLength { get; set; } = 16.0;
    public float GradientAlpha { get; set; } = 0.7f;
    public double GradientThreshold { get; set; } = 1e-4;

    // Legend data exposed to the HUD.
    public string LegendTitle { get; private set; } = "";
    public string LegendNote { get; private set; } = "";
    public double LegendMin { get; private set; }
    public double LegendMax { get; private set; }

    private Dictionary<int, Color> _typeColors = new();
    private Dictionary<int, double> _maxEnergyByType = new();
    private Color _morphogenColor = Colors.DeepSkyBlue;
    public Color MorphogenUiColor => _morphogenColor;
    private Image? _morphogenImage;
    private ImageTexture? _morphogenTexture;
    private double _morphogenScale = 1.0;

    public void SetCellColors(IEnumerable<CellTypeConfig> types, int morphogenId)
    {
        _typeColors = new Dictionary<int, Color>();
        _maxEnergyByType = new Dictionary<int, double>();
        foreach (var t in types)
        {
            _typeColors[t.Id] = ParseColor(t.Color);
            _maxEnergyByType[t.Id] = t.MaxEnergy;
        }
    }

    public void SetMorphogenStyle(string hexColor, double scale)
    {
        _morphogenColor = ParseColor(hexColor);
        _morphogenScale = scale > 0 ? scale : 1.0;
    }

    private static Color ParseColor(string hex)
    {
        var c = Color.FromHtml(hex);
        if (c.A <= 0) c = Colors.White;
        return c;
    }

    public bool HasFieldState()
    {
        if (Sim == null) return false;
        return MorphogenId >= 0 && MorphogenId < Sim.Morphogens.MorphogenCount;
    }

    /// <summary>
    /// Rebuilds the concentration heat-map texture at a throttled rate and
    /// updates the legend. Returns peak/min used for the current scale.
    /// </summary>
    public (double Min, double Max, bool Relative) RefreshOverlay(int everyNFrames = 3)
    {
        var m = Sim?.Morphogens;
        if (m == null) return (0, 1, true);

        int mid = MorphogenId >= 0 && MorphogenId < m.MorphogenCount ? MorphogenId : 0;

        double peak = 0.0;
        double minVal = double.MaxValue;
        for (int gy = 0; gy < m.GridHeight; gy++)
        {
            for (int gx = 0; gx < m.GridWidth; gx++)
            {
                double v = m.GetField(mid, gx, gy);
                if (v > peak) peak = v;
                if (v < minVal) minVal = v;
            }
        }
        if (minVal > peak) minVal = 0.0;

        bool relative = NormalizeConcentration || (!NormalizeConcentration && peak <= FixedMax && minVal >= FixedMin);
        double scaleMax = NormalizeConcentration ? (peak > 1e-9 ? peak : 1.0) : FixedMax;
        double scaleMin = NormalizeConcentration ? 0.0 : FixedMin;
        if (scaleMax <= scaleMin) scaleMax = scaleMin + 1e-9;

        LegendMin = scaleMin;
        LegendMax = scaleMax;
        LegendTitle = $"Концентрация «{Localization.MorphogenName(MorphogenOf(mid))}»";
        LegendNote = relative ? "относительная шкала (нормировка к максимуму)" : "абсолютная шкала";

        if ((Layer == WorldLayer.Morphogen) && (_morphogenImage == null
            || _morphogenImage.GetWidth() != m.GridWidth
            || _morphogenImage.GetHeight() != m.GridHeight))
        {
            _morphogenImage = Image.CreateEmpty(m.GridWidth, m.GridHeight, false, Image.Format.Rgba8);
            _morphogenTexture = ImageTexture.CreateFromImage(_morphogenImage);
        }

        if (Layer == WorldLayer.Morphogen && _morphogenImage != null)
        {
            for (int gy = 0; gy < m.GridHeight; gy++)
            {
                for (int gx = 0; gx < m.GridWidth; gx++)
                {
                    double v = (m.GetField(mid, gx, gy) - scaleMin) / (scaleMax - scaleMin);
                    if (v < 0.0) v = 0.0;
                    if (v > 1.0) v = 1.0;
                    _morphogenImage.SetPixel(gx, gy, new Color(_morphogenColor, (float)v * 0.55f));
                }
            }
            _morphogenTexture?.Update(_morphogenImage);
        }

        QueueRedraw();
        return (scaleMin, scaleMax, relative);
    }

    private MorphogenConfig MorphogenOf(int id)
    {
        if (Sim?.MorphogensById.TryGetValue(id, out var cfg) == true) return cfg;
        return new MorphogenConfig { Id = id, Name = $"morphogen {id}" };
    }

    public override void _Draw()
    {
        if (Sim == null) return;

        if (Layer == WorldLayer.Morphogen && _morphogenTexture != null)
        {
            var rect = new Rect2(0, 0, (float)Sim.Morphogens.WorldWidth, (float)Sim.Morphogens.WorldHeight);
            DrawTextureRect(_morphogenTexture, rect, false);
        }

        if (Layer == WorldLayer.Gradient)
        {
            DrawGradientVectors();
            if (!ShowCells) return;
        }

        for (int i = 0; i < Sim.Cells.Count; i++)
            DrawCell(Sim.Cells[i]);
    }

    private void DrawGradientVectors()
    {
        if (Sim == null) return;
        var m = Sim.Morphogens;
        int stride = Math.Max(1, GradientDensity);
        var color = new Color(1.0f, 1.0f, 1.0f, GradientAlpha);

        for (int gy = 0; gy < m.GridHeight; gy += stride)
        {
            for (int gx = 0; gx < m.GridWidth; gx += stride)
            {
                double x = (gx + 0.5) * m.Resolution;
                double y = (gy + 0.5) * m.Resolution;
                var (dx, dy) = m.Gradient(MorphogenId, x, y);
                if (!double.IsFinite(dx)) dx = 0.0;
                if (!double.IsFinite(dy)) dy = 0.0;
                double mag = Math.Sqrt(dx * dx + dy * dy);
                if (mag <= GradientThreshold) continue;

                double scale = GradientLength / Math.Max(mag, 1e-9);
                var from = new Vector2((float)x, (float)y);
                var to = new Vector2((float)(x + dx * scale), (float)(y + dy * scale));

                DrawLine(from, to, color, 1.5f);

                // Arrow head.
                if ((to - from).Length() > 1.0f)
                {
                    Vector2 dir = (to - from).Normalized();
                    Vector2 perp = new(-dir.Y, dir.X);
                    float head = 3.5f;
                    DrawLine(to, to - dir * head + perp * (head * 0.6f), color, 1.2f);
                    DrawLine(to, to - dir * head - perp * (head * 0.6f), color, 1.2f);
                }
            }
        }
    }

    private void DrawCell(Cell cell)
    {
        var center = new Vector2((float)cell.X, (float)cell.Y);
        float radius = (float)cell.Radius;

        _typeColors.TryGetValue(cell.TypeId, out var typeColor);
        var fill = typeColor;
        bool analysis = false;

        switch (Layer)
        {
            case WorldLayer.GeneActivity:
                fill = GeneActivityColor(cell);
                analysis = true;
                break;
            case WorldLayer.GeneState:
                fill = GeneStateColor(cell);
                analysis = true;
                break;
            case WorldLayer.Energy:
                fill = EnergyColor(cell);
                analysis = true;
                break;
            case WorldLayer.Neighbours:
                fill = NeighboursColor(cell);
                analysis = true;
                break;
            case WorldLayer.Morphogen:
                if (TintCellsOverField && Sim != null)
                {
                    double v = Sim.SampleMorphogen(MorphogenId, cell.X, cell.Y);
                    double t = Math.Clamp((v - LegendMin) / (LegendMax - LegendMin), 0.0, 1.0);
                    fill = typeColor.Lerp(Colors.White, (float)t * 0.55f);
                }
                analysis = true;
                break;
        }

        if (!ShowCells && Layer != WorldLayer.Morphogen) return;

        // State / dying tint.
        if (cell.State == CellState.Dying)
        {
            float fade = Mathf.Clamp((float)(1.0 - cell.DeathTimer / 0.6), 0.0f, 1.0f);
            fill = new Color(fill.R, fill.G * 0.3f, fill.B * 0.3f, fade * 0.8f);
        }
        else if (Layer == WorldLayer.CellTypes && cell.Energy <= 0.0)
        {
            fill = new Color(0.7f, 0.2f, 0.25f);
        }
        else if (Layer == WorldLayer.CellTypes && cell.DivisionProgress > 0.0)
        {
            fill = fill.Lerp(new Color(1.0f, 0.9f, 0.4f), (float)Num.Clamp(cell.DivisionProgress, 0.0, 1.0) * 0.5f);
        }

        DrawCircle(center, radius, fill);

        // Outline: dark for normal cells, white when a gene layer draws on top.
        Color outline = analysis ? Colors.White : fill.Darkened(0.35f);
        DrawArc(center, radius, 0, Mathf.Tau, 28, outline, analysis ? 1.0f : 1.5f, true);

        if (SelectedCellId == cell.Id)
            DrawArc(center, radius + 3.0f, 0, Mathf.Tau, 32, Colors.White, 2.0f, true);
    }

    private Color GeneActivityColor(Cell cell)
    {
        bool active = IsGeneActive(cell);
        if (active) return new Color(0.35f, 0.95f, 0.45f, 0.9f);
        if (cell.IsSilenced(GeneId)) return new Color(0.9f, 0.25f, 0.25f, 0.75f);
        return new Color(0.30f, 0.32f, 0.34f, 0.7f);
    }

    private Color GeneStateColor(Cell cell)
    {
        bool active = IsGeneActive(cell);
        if (cell.IsSilenced(GeneId)) return new Color(0.85f, 0.2f, 0.2f, 0.9f);
        if (active) return new Color(0.3f, 0.9f, 0.4f, 0.9f);
        return new Color(0.35f, 0.35f, 0.38f, 0.65f);
    }

    private bool IsGeneActive(Cell cell) => !string.IsNullOrEmpty(GeneId) && cell.StateOf(GeneId).Active;

    private Color EnergyColor(Cell cell)
    {
        _maxEnergyByType.TryGetValue(cell.TypeId, out double max);
        if (max <= 0) max = 100.0;
        double t = Math.Clamp(cell.Energy / max, 0.0, 1.0);
        return new Color((float)(0.4 + 0.6 * t), (float)(0.15 + 0.8 * t), (float)(0.15 + 0.2 * t), 0.9f);
    }

    private Color NeighboursColor(Cell cell)
    {
        int n = (int)cell.NeighbourCount;
        double t = Math.Clamp(n / 8.0, 0.0, 1.0);
        return new Color((float)(0.2 + 0.5 * t), (float)(0.3 + 0.5 * (1.0 - t)), (float)(0.45 + 0.4 * t), 0.9f);
    }
}