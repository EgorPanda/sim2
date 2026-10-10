using Godot;
using SimulationCore;

namespace Sim2;

/// <summary>
/// On-screen controls: pause/step/reset, speed slider, cell radius slider,
/// initial-conditions spinner, morphogen overlay toggle, delete-mode toggle
/// and a stats readout. Emits C# events the <see cref="Main"/> subscribes to.
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

    private Button _pauseButton = null!;
    private Button _stepButton = null!;
    private Button _resetButton = null!;
    private HSlider _speedSlider = null!;
    private HSlider _radiusSlider = null!;
    private SpinBox _founderSpinner = null!;
    private SpinBox _seedSpinner = null!;
    private CheckBox _overlayCheck = null!;
    private CheckBox _deleteCheck = null!;
    private Label _statsLabel = null!;
    private bool _suppressSignals;

    public void Build(SimulationConfig config)
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

        // Row 5: toggles.
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

        // Stats, top-right.
        _statsLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        AddChild(_statsLabel);

        _statsLabel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _statsLabel.Position = new Vector2(-16, 12);
        _statsLabel.GrowHorizontal = Control.GrowDirection.Begin;
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
}