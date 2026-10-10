using Godot;
using SimulationCore;

namespace Sim2;

/// <summary>
/// On-screen controls and read-outs. The left panel holds transport, speed,
/// initial conditions, the morphogen field selector and toggles. The right
/// panel shows per-phenotype statistics and the selected cell's regulation
/// state (active/silenced genes, timers, local morphogen levels). It only
/// formats data supplied by <see cref="Main"/>; all biology stays in the core.
/// </summary>
public partial class Hud : CanvasLayer
{
    public event Action? PauseRequested;
    public event Action? StepRequested;
    public event Action<int, int, double>? ResetRequested;
    public event Action<double>? SpeedChanged;
    public event Action<double>? RadiusChanged;
    public event Action<bool>? OverlayToggled;
    public event Action<bool>? DeleteModeToggled;
    public event Action<int>? MorphogenSelected;

    private Button _pauseButton = null!;
    private Button _stepButton = null!;
    private Button _resetButton = null!;
    private HSlider _speedSlider = null!;
    private HSlider _radiusSlider = null!;
    private SpinBox _founderSpinner = null!;
    private SpinBox _seedSpinner = null!;
    private OptionButton _morphogenSelect = null!;
    private CheckBox _overlayCheck = null!;
    private CheckBox _deleteCheck = null!;
    private Label _statsLabel = null!;
    private Label _phenotypeLabel = null!;
    private Label _selectionLabel = null!;
    private bool _suppressSignals;

    public void Build(SimulationConfig config, MorphogenConfig[] morphogens, CellTypeConfig[] types)
    {
        BuildControlPanel(config, morphogens);
        BuildStatsPanel(types);
    }

    private void BuildControlPanel(SimulationConfig config, MorphogenConfig[] morphogens)
    {
        var panel = new PanelContainer
        {
            Position = new Vector2(12, 12)
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.55f),
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8
        };
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        var box = new VBoxContainer();
        panel.AddChild(box);

        // Row 1: transport.
        var transport = new HBoxContainer();
        box.AddChild(transport);

        _pauseButton = new Button { Text = "Pause" };
        _pauseButton.Pressed += () => PauseRequested?.Invoke();
        transport.AddChild(_pauseButton);

        _stepButton = new Button { Text = "Step" };
        _stepButton.Pressed += () => StepRequested?.Invoke();
        transport.AddChild(_stepButton);

        _resetButton = new Button { Text = "Reset" };
        _resetButton.Pressed += OnResetPressed;
        transport.AddChild(_resetButton);

        // Row 2: speed.
        var speedRow = new HBoxContainer();
        box.AddChild(speedRow);
        speedRow.AddChild(new Label { Text = "Speed  ", CustomMinimumSize = new Vector2(56, 0), VerticalAlignment = VerticalAlignment.Center });
        _speedSlider = new HSlider { MinValue = 0.1, MaxValue = 8.0, Step = 0.1, Value = config.DefaultSpeed, CustomMinimumSize = new Vector2(180, 0) };
        _speedSlider.ValueChanged += v => SpeedChanged?.Invoke(v);
        speedRow.AddChild(_speedSlider);

        // Row 3: cell radius (applies on reset).
        var radiusRow = new HBoxContainer();
        box.AddChild(radiusRow);
        radiusRow.AddChild(new Label { Text = "Radius ", CustomMinimumSize = new Vector2(56, 0), VerticalAlignment = VerticalAlignment.Center });
        _radiusSlider = new HSlider { MinValue = 4.0, MaxValue = 40.0, Step = 0.5, Value = config.CellRadius, CustomMinimumSize = new Vector2(140, 0) };
        _radiusSlider.ValueChanged += v => RadiusChanged?.Invoke(v);
        radiusRow.AddChild(_radiusSlider);

        // Row 4: initial conditions.
        var initRow = new HBoxContainer();
        box.AddChild(initRow);
        initRow.AddChild(new Label { Text = "Founders" });
        _founderSpinner = new SpinBox { MinValue = 1, MaxValue = 200, Value = config.InitialCells, CustomMinimumSize = new Vector2(70, 0) };
        initRow.AddChild(_founderSpinner);
        initRow.AddChild(new Label { Text = "  Seed" });
        _seedSpinner = new SpinBox { MinValue = 0, MaxValue = 99999, Value = config.RngSeed, CustomMinimumSize = new Vector2(84, 0) };
        initRow.AddChild(_seedSpinner);

        // Row 5: morphogen field selector.
        var morphRow = new HBoxContainer();
        box.AddChild(morphRow);
        morphRow.AddChild(new Label { Text = "Field   ", CustomMinimumSize = new Vector2(56, 0), VerticalAlignment = VerticalAlignment.Center });
        _morphogenSelect = new OptionButton { CustomMinimumSize = new Vector2(180, 0) };
        foreach (var m in morphogens)
            _morphogenSelect.AddItem($"{m.Id}: {m.Name}", m.Id);
        _morphogenSelect.ItemSelected += idx =>
        {
            if (_suppressSignals) return;
            int id = (int)_morphogenSelect.GetItemId((int)idx);
            MorphogenSelected?.Invoke(id);
        };
        morphRow.AddChild(_morphogenSelect);

        // Row 6: toggles.
        var toggleRow = new HBoxContainer();
        box.AddChild(toggleRow);
        _overlayCheck = new CheckBox { Text = "Morphogens", ButtonPressed = false };
        _overlayCheck.Toggled += on => OverlayToggled?.Invoke(on);
        toggleRow.AddChild(_overlayCheck);
        _deleteCheck = new CheckBox { Text = "Delete mode", ButtonPressed = false };
        _deleteCheck.Toggled += on => DeleteModeToggled?.Invoke(on);
        toggleRow.AddChild(_deleteCheck);

        // Help text.
        box.AddChild(new Label
        {
            Text = "LMB select | RMB/MB drag pan | wheel zoom\nSpace pause | . step | E delete-mode | Del remove | R reset",
            MouseFilter = Control.MouseFilterEnum.Ignore
        });
    }

    private void BuildStatsPanel(CellTypeConfig[] types)
    {
        // Top-right stats line.
        _statsLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        AddChild(_statsLabel);
        _statsLabel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _statsLabel.Position = new Vector2(-16, 12);
        _statsLabel.GrowHorizontal = Control.GrowDirection.Begin;

        // Right-side detail panel below the stats line.
        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.55f),
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8
        };
        panel.AddThemeStyleboxOverride("panel", style);
        panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        panel.Position = new Vector2(-16, 56);
        panel.GrowHorizontal = Control.GrowDirection.Begin;
        AddChild(panel);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(250, 0) };
        panel.AddChild(box);
        box.AddChild(new Label { Text = "-- Phenotypes --" });
        _phenotypeLabel = new Label();
        box.AddChild(_phenotypeLabel);
        box.AddChild(new Label { Text = "\n-- Selected cell --" });
        _selectionLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_selectionLabel);
    }

    private void OnResetPressed()
    {
        if (_suppressSignals) return;
        ResetRequested?.Invoke(Mathf.Max(1, (int)_founderSpinner.Value), (int)_seedSpinner.Value, _radiusSlider.Value);
    }

    /// <summary>Updates the pause/disable state of transport buttons.</summary>
    public void SetPaused(bool paused)
    {
        _pauseButton.Text = paused ? "Resume" : "Pause";
        _stepButton.Disabled = !paused;
    }

    /// <summary>Synchronises the delete-mode checkbox without re-firing its signal.</summary>
    public void SetDeleteMode(bool enabled)
    {
        _suppressSignals = true;
        _deleteCheck.SetPressedNoSignal(enabled);
        _suppressSignals = false;
    }

    public void UpdateStats(int cellCount, int fps, double simTime, long tick, bool paused, int? selectedId)
    {
        _statsLabel.Text =
            $"cells: {cellCount}   fps: {fps}   time: {simTime:F1}s   tick: {tick}\n" +
            (paused ? "PAUSED  " : "") +
            (selectedId is int id ? $"selected: #{id}" : "");
    }

    /// <summary>Refreshes the per-phenotype population counts (multi-line text).</summary>
    public void UpdatePhenotypes(string text) => _phenotypeLabel.Text = text;

    /// <summary>Refreshes the selected-cell detail text (multi-line).</summary>
    public void UpdateSelection(string text) => _selectionLabel.Text = text;

    /// <summary>Synchronises the field selector without re-firing its signal.</summary>
    public void SetSelectedMorphogen(int morphogenId)
    {
        _suppressSignals = true;
        for (int i = 0; i < _morphogenSelect.ItemCount; i++)
        {
            if ((int)_morphogenSelect.GetItemId(i) == morphogenId)
            {
                _morphogenSelect.Select(i);
                break;
            }
        }
        _suppressSignals = false;
    }
}