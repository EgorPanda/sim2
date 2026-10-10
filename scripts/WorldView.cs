using Godot;
using SimulationCore;

namespace Sim2;

/// <summary>
/// Draws the world: cells (coloured by type and state) and an optional
/// morphogen heat-map overlay. Purely presentational — it only reads core
/// state and never mutates it.
/// </summary>
public partial class WorldView : Node2D
{
    public Simulation? Sim { get; set; }
    public bool ShowMorphogens { get; set; }
    public int? SelectedCellId { get; set; }
    public int MorphogenId { get; set; }

    private Dictionary<int, Color> _typeColors = new();
    private Color _morphogenColor = Colors.DeepSkyBlue;
    private Image? _morphogenImage;
    private ImageTexture? _morphogenTexture;
    private double _morphogenScale = 1.0;

    public void SetCellColors(IEnumerable<CellTypeConfig> types, int morphogenId)
    {
        _typeColors = types.ToDictionary(t => t.Id, t => ParseColor(t.Color));
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

    /// <summary>Rebuilds the morphogen overlay texture at a throttled rate.</summary>
    public void MaybeRefreshOverlay(int everyNFrames = 3)
    {
        if (Sim == null || Engine.GetProcessFrames() % (ulong)everyNFrames != 0) return;

        var m = Sim.Morphogens;
        if (_morphogenImage == null || _morphogenImage.GetWidth() != m.GridWidth || _morphogenImage.GetHeight() != m.GridHeight)
        {
            _morphogenImage = Image.CreateEmpty(m.GridWidth, m.GridHeight, false, Image.Format.Rgba8);
            _morphogenTexture = ImageTexture.CreateFromImage(_morphogenImage);
        }

        int mid = MorphogenId >= 0 && MorphogenId < m.MorphogenCount ? MorphogenId : 0;

        // Auto-normalise against the current peak so any morphogen stays visible.
        double peak = 0.0;
        for (int gy = 0; gy < m.GridHeight; gy++)
            for (int gx = 0; gx < m.GridWidth; gx++)
                peak = Math.Max(peak, m.GetField(mid, gx, gy));
        double scale = peak > 1e-9 ? peak : (_morphogenScale > 0 ? _morphogenScale : 1.0);

        for (int gy = 0; gy < m.GridHeight; gy++)
        {
            for (int gx = 0; gx < m.GridWidth; gx++)
            {
                double v = m.GetField(mid, gx, gy) / scale;
                if (v > 1.0) v = 1.0;
                if (v < 0.0) v = 0.0;
                _morphogenImage.SetPixel(gx, gy, new Color(_morphogenColor, (float)v * 0.55f));
            }
        }

        _morphogenTexture?.Update(_morphogenImage);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Sim == null) return;

        if (ShowMorphogens && _morphogenTexture != null)
        {
            var rect = new Rect2(0, 0, (float)Sim.Morphogens.WorldWidth, (float)Sim.Morphogens.WorldHeight);
            DrawTextureRect(_morphogenTexture, rect, false);
        }

        for (int i = 0; i < Sim.Cells.Count; i++)
            DrawCell(Sim.Cells[i]);
    }

    private void DrawCell(Cell cell)
    {
        var center = new Vector2((float)cell.X, (float)cell.Y);
        float radius = (float)cell.Radius;

        // Base colour by type.
        _typeColors.TryGetValue(cell.TypeId, out var color);

        // State tint: Dying -> red, Dividing -> yellow, else type colour.
        if (cell.State == CellState.Dying)
        {
            float fade = (float)(1.0 - cell.DeathTimer / 0.6);
            color = new Color(color.R, color.G * 0.3f, color.B * 0.3f, fade * 0.8f);
        }
        else if (cell.Energy <= 0.0)
        {
            color = new Color(0.7f, 0.2f, 0.25f);
        }
        else if (cell.DivisionProgress > 0.0)
        {
            color = color.Lerp(new Color(1.0f, 0.9f, 0.4f), (float)Mathf.Clamp(cell.DivisionProgress, 0.0, 1.0) * 0.5f);
        }

        DrawCircle(center, radius, color);
        DrawArc(center, radius, 0, Mathf.Tau, 28, color.Darkened(0.35f), 1.5f, true);

        // Selection ring.
        if (SelectedCellId == cell.Id)
            DrawArc(center, radius + 3.0f, 0, Mathf.Tau, 32, Colors.White, 2.0f, true);
    }
}