using Godot;
using SimulationCore;
using System;

namespace Sim2;

/// <summary>
/// Analytical display layer of the world. A layer changes how real simulation
/// data is visualised (cell type, morphogen concentration, gradient, gene
/// activity, silenced genes, neighbour density) and never mutates the
/// simulation state.
/// </summary>
public enum WorldLayer
{
    CellTypes,
    Morphogen,
    MultiMorphogen,
    Gradient,
    InternalMorphogen,
    GeneActivity,
    GeneState,
    Neighbours
}

/// <summary>
/// Draws the world. Cellular layers over the cell bodies; concentration
/// heat-map (throttled) and gradient arrows. Purely presentational — it only
/// reads core state and never mutates it.
/// </summary>
public partial class WorldView : Node2D
{
    public Simulation? Sim { get; set; }
    public int? SelectedCellId { get; set; }

    public WorldLayer Layer { get; set; } = WorldLayer.CellTypes;

    /// <summary>Heat-map on top when Layer == Morphogen or MultiMorphogen.</summary>
    public bool ShowField => Layer == WorldLayer.Morphogen || Layer == WorldLayer.MultiMorphogen;

    public int MorphogenId { get; set; }

    /// <summary>All morphogens selected for visualisation (external fields + internal tinting).</summary>
    public HashSet<int> SelectedMorphogenIds { get; } = new();

    /// <summary>Ids of external morphogens shown in the multi-morphogen overlay.</summary>
    public HashSet<int> VisibleMorphogenIds { get; } = new();

    /// <summary>Global toggle for morphogen overlay visibility.</summary>
    public bool ShowMorphogens { get; set; } = true;

    /// <summary>When true, cell fill is drawn semi-transparent so fields underneath are visible.</summary>
    public bool SeeThrough { get; set; } = false;

    /// <summary>Normalize each morphogen channel to its own peak in the multi overlay.</summary>
    public bool MultiMorphogenNormalize { get; set; } = true;

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
    private Color _morphogenColor = Colors.DeepSkyBlue;
    public Color MorphogenUiColor => _morphogenColor;
    private Dictionary<int, Color> _morphogenColors = new();
    private Image? _morphogenImage;
    private ImageTexture? _morphogenTexture;
    private double _morphogenScale = 1.0;

    /// <summary>Releases the heat-map Image/ImageTexture owned by this node.</summary>
    private void ReleaseFieldTexture()
    {
        if (_morphogenTexture != null)
        {
            _morphogenTexture.Dispose();
            _morphogenTexture = null;
        }
        if (_morphogenImage != null)
        {
            _morphogenImage.Dispose();
            _morphogenImage = null;
        }
    }

    public override void _ExitTree()
    {
        ReleaseFieldTexture();
        base._ExitTree();
    }

    public void SetCellColors(IEnumerable<CellTypeConfig> types, int morphogenId)
    {
        _typeColors = new Dictionary<int, Color>();
        foreach (var t in types)
        {
            _typeColors[t.Id] = ParseColor(t.Color);
        }
    }

    public void SetMorphogenStyle(string hexColor, double scale)
    {
        _morphogenColor = ParseColor(hexColor);
        _morphogenScale = scale > 0 ? scale : 1.0;
    }

    public void SetMorphogenColors(IEnumerable<MorphogenConfig> morphogens)
    {
        _morphogenColors = new Dictionary<int, Color>();
        foreach (var m in morphogens)
            _morphogenColors[m.Id] = ParseColor(m.Color);
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
        return Sim.Morphogens.HasField(MorphogenId);
    }

    /// <summary>
    /// Rebuilds the concentration heat-map texture at a throttled rate and
    /// updates the legend. Returns peak/min used for the current scale.
    /// </summary>
    public (double Min, double Max, bool Relative) RefreshOverlay(int everyNFrames = 3)
    {
        var m = Sim?.Morphogens;
        if (m == null) return (0, 1, true);

        if (Layer == WorldLayer.InternalMorphogen)
            return RefreshInternalOverlay();

        if (Layer == WorldLayer.MultiMorphogen)
            return RefreshMultiMorphogenOverlay();

        if (Sim == null || !Sim.Morphogens.HasField(MorphogenId))
        {
            LegendTitle = "Морфоген не выбран";
            LegendNote = "выберите внешний морфоген";
            QueueRedraw();
            return (0, 1, true);
        }

        int mid = MorphogenId;

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

        EnsureFieldTexture(m.GridWidth, m.GridHeight);
        if (Layer == WorldLayer.Morphogen && _morphogenImage != null)
        {
            for (int gy = 0; gy < m.GridHeight; gy++)
            {
                for (int gx = 0; gx < m.GridWidth; gx++)
                {
                    double v = (m.GetField(mid, gx, gy) - scaleMin) / (scaleMax - scaleMin);
                    if (v < 0.0) v = 0.0;
                    if (v > 1.0) v = 1.0;
                    _morphogenImage.SetPixel(gx, gy, new Color(_morphogenColor, (float)v * 0.75f));
                }
            }
            _morphogenTexture?.Update(_morphogenImage);
        }

        QueueRedraw();
        return (scaleMin, scaleMax, relative);
    }

    private void EnsureFieldTexture(int width, int height)
    {
        if (_morphogenImage == null
            || _morphogenImage.GetWidth() != width
            || _morphogenImage.GetHeight() != height)
        {
            ReleaseFieldTexture();
            _morphogenImage = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
            _morphogenTexture = ImageTexture.CreateFromImage(_morphogenImage);
        }
    }

    private (double Min, double Max, bool Relative) RefreshMultiMorphogenOverlay()
    {
        var m = Sim!.Morphogens;
        var active = Sim.MorphogensById.Values
            .Where(cfg => cfg.IsExternal && VisibleMorphogenIds.Contains(cfg.Id))
            .OrderBy(cfg => cfg.Id)
            .ToList();

        EnsureFieldTexture(m.GridWidth, m.GridHeight);

        if (active.Count == 0)
        {
            LegendMin = 0;
            LegendMax = 1;
            LegendTitle = "Смесь морфогенов";
            LegendNote = "нет выбранных морфогенов";
            if (_morphogenImage != null)
            {
                for (int gy = 0; gy < m.GridHeight; gy++)
                    for (int gx = 0; gx < m.GridWidth; gx++)
                        _morphogenImage.SetPixel(gx, gy, new Color(0, 0, 0, 0));
                _morphogenTexture?.Update(_morphogenImage);
            }
            QueueRedraw();
            return (0, 1, true);
        }

        var scales = new Dictionary<int, double>();
        foreach (var cfg in active)
        {
            double peak = 0.0;
            for (int gy = 0; gy < m.GridHeight; gy++)
                for (int gx = 0; gx < m.GridWidth; gx++)
                {
                    double v = m.GetField(cfg.Id, gx, gy);
                    if (v > peak) peak = v;
                }
            scales[cfg.Id] = MultiMorphogenNormalize
                ? (peak > 1e-9 ? peak : 1.0)
                : (FixedMax > FixedMin ? FixedMax : 1.0);
        }

        LegendMin = 0;
        LegendMax = 1;
        LegendTitle = "Смесь морфогенов";
        LegendNote = string.Join("  ", active.Select(cfg => $"● {Localization.MorphogenName(cfg)}"));

        if (_morphogenImage != null)
        {
            for (int gy = 0; gy < m.GridHeight; gy++)
            {
                for (int gx = 0; gx < m.GridWidth; gx++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    foreach (var cfg in active)
                    {
                        double v = m.GetField(cfg.Id, gx, gy) / scales[cfg.Id];
                        if (v < 0.0) v = 0.0;
                        if (v > 1.0) v = 1.0;
                        var c = _morphogenColors.TryGetValue(cfg.Id, out var col) ? col : _morphogenColor;
                        r += c.R * (float)v;
                        g += c.G * (float)v;
                        b += c.B * (float)v;
                        a += (float)v * 0.55f;
                    }
                    r = Mathf.Clamp(r, 0, 1);
                    g = Mathf.Clamp(g, 0, 1);
                    b = Mathf.Clamp(b, 0, 1);
                    a = Mathf.Clamp(a, 0, 0.75f);
                    _morphogenImage.SetPixel(gx, gy, new Color(r, g, b, a));
                }
            }
            _morphogenTexture?.Update(_morphogenImage);
        }

        QueueRedraw();
        return (0, 1, true);
    }

    private (double Min, double Max, bool Relative) RefreshInternalOverlay()
    {
        if (Sim == null) return (0, 1, true);

        double peak = 0.0;
        foreach (var cell in Sim.Cells)
        {
            double v = cell.InternalMorphogenConcentrations.TryGetValue(MorphogenId, out double c) ? c : 0.0;
            if (v > peak) peak = v;
        }

        double scaleMax = NormalizeConcentration ? (peak > 1e-9 ? peak : 1.0) : FixedMax;
        double scaleMin = NormalizeConcentration ? 0.0 : FixedMin;
        if (scaleMax <= scaleMin) scaleMax = scaleMin + 1e-9;

        LegendMin = scaleMin;
        LegendMax = scaleMax;
        LegendTitle = $"Внутриклеточная «{Localization.MorphogenName(MorphogenOf(MorphogenId))}»";
        LegendNote = NormalizeConcentration ? "относительная шкала" : "абсолютная шкала";

        QueueRedraw();
        return (scaleMin, scaleMax, NormalizeConcentration);
    }

    private MorphogenConfig MorphogenOf(int id)
    {
        if (Sim?.MorphogensById.TryGetValue(id, out var cfg) == true) return cfg;
        return new MorphogenConfig { Id = id, Name = $"morphogen {id}" };
    }

    public override void _Draw()
    {
        if (Sim == null) return;

        bool showMorphogens = ShowMorphogens &&
            (Layer == WorldLayer.Morphogen || Layer == WorldLayer.MultiMorphogen)
            && _morphogenTexture != null;

        if (showMorphogens)
        {
            var rect = new Rect2(0, 0, (float)Sim.Morphogens.WorldWidth, (float)Sim.Morphogens.WorldHeight);
            DrawTextureRect(_morphogenTexture, rect, false);
        }

        if (Layer == WorldLayer.Gradient)
            DrawGradientVectors();

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
            case WorldLayer.InternalMorphogen:
            {
                double v = cell.InternalMorphogenConcentrations.TryGetValue(MorphogenId, out double c) ? c : 0.0;
                double t = Math.Clamp((v - LegendMin) / (LegendMax - LegendMin), 0.0, 1.0);
                fill = typeColor.Lerp(Colors.White, (float)t * 0.55f);
                analysis = true;
                break;
            }
        }

        // State / dying tint only: the fill itself is always the phenotype colour.
        if (cell.State == CellState.Dying)
        {
            float fade = Mathf.Clamp((float)(1.0 - cell.DeathTimer / 0.6), 0.0f, 1.0f);
            fill = new Color(fill.R, fill.G * 0.3f, fill.B * 0.3f, fade * 0.8f);
        }
        else if (SeeThrough)
        {
            fill = new Color(fill.R, fill.G, fill.B, 0.30f);
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

    private Color NeighboursColor(Cell cell)
    {
        int n = (int)cell.NeighbourCount;
        double t = Math.Clamp(n / 8.0, 0.0, 1.0);
        return new Color((float)(0.2 + 0.5 * t), (float)(0.3 + 0.5 * (1.0 - t)), (float)(0.45 + 0.4 * t), 0.9f);
    }
}