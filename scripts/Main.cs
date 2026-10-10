using Godot;
using SimulationCore;

namespace Sim2;

/// <summary>
/// Root node of the Godot front-end. Owns the simulation instance, camera and
/// HUD, controls stepping, and forwards input. Contains no biological rules —
/// everything biological lives in SimulationCore.
/// </summary>
public partial class Main : Node2D
{
    private Simulation? _sim;
    private SimulationSetup? _setup;
    private Camera2D _camera = null!;
    private WorldView _world = null!;
    private Hud _hud = null!;

    private bool _paused;
    private double _speed = 1.0;
    private double _accumulator;
    private int? _selectedCellId;
    private bool _deleteMode;

    private bool _panning;
    private Vector2 _panStartScreen;
    private Vector2 _cameraStartPos;

    public override void _Ready()
    {
        _setup = ConfigLoader.LoadFromDirectory(ProjectSettings.GlobalizePath("res://config"));
        _speed = _setup.Simulation.DefaultSpeed;

        _sim = new Simulation(_setup);

        _camera = new Camera2D
        {
            Zoom = new Vector2(0.7f, 0.7f),
            Position = new Vector2(
                (float)(_setup.Simulation.WorldWidth * 0.5),
                (float)(_setup.Simulation.WorldHeight * 0.5))
        };
        AddChild(_camera);
        _camera.MakeCurrent();

        _world = new WorldView { Sim = _sim };
        _world.SetCellColors(_setup.CellTypes, morphogenId: 0);
        AddChild(_world);

        _hud = new Hud();
        AddChild(_hud);
        _hud.Build(_setup.Simulation);
        _hud.PauseRequested += OnPauseRequested;
        _hud.StepRequested += OnStepRequested;
        _hud.ResetRequested += OnResetRequested;
        _hud.SpeedChanged += value => _speed = value;
        _hud.RadiusChanged += OnRadiusChanged;
        _hud.OverlayToggled += show => _world.ShowMorphogens = show;
        _hud.DeleteModeToggled += on => _deleteMode = on;
        _hud.SetPaused(_paused);
        _hud.UpdateStats(0, 0, _sim.Time, _sim.Cells.Count, _paused, null);

        if (OS.GetCmdlineUserArgs().Contains("--selftest"))
            RunSelfTest();
    }

    public override void _Process(double delta)
    {
        if (_sim == null) return;

        if (!_paused)
        {
            double stepDt = _sim.Config.FixedTimeStep;
            _accumulator += delta * _speed;
            int guard = 0;
            while (_accumulator >= stepDt && guard < 64)
            {
                _sim.Step(stepDt);
                _accumulator -= stepDt;
                guard++;
            }
            if (guard >= 64) _accumulator = 0.0; // avoid spiral of death
        }

        HandleKeyboardPan(delta);

        // Refresh the morphogen overlay only occasionally (cheap CPU).
        if (_world.ShowMorphogens && Engine.GetFramesPerSecond() > 0)
            _world.MaybeRefreshOverlay();

        _world.SelectedCellId = _selectedCellId;
        _world.QueueRedraw();

        _hud.UpdateStats(
            cellCount: _sim.Cells.Count,
            fps: Mathf.RoundToInt(Engine.GetFramesPerSecond()),
            simTime: _sim.Time,
            tick: _sim.Tick,
            paused: _paused,
            selectedId: _selectedCellId);
    }

    // --- Simulation control -------------------------------------------------

    private void OnPauseRequested()
    {
        _paused = !_paused;
        _hud.SetPaused(_paused);
    }

    private void OnStepRequested()
    {
        if (_sim == null) return;
        _sim.Step(_sim.Config.FixedTimeStep);
        _accumulator = 0.0;
        _world.QueueRedraw();
    }

    private void OnResetRequested(int founders, int seed, double radius)
    {
        if (_sim == null) return;
        _sim.Config.InitialCells = Math.Max(1, founders);
        _sim.Config.RngSeed = seed;
        ApplyRadius(radius);
        _sim.Reset();
        _selectedCellId = null;
        _accumulator = 0.0;
        _world.QueueRedraw();
    }

    private void OnRadiusChanged(double radius)
    {
        if (_sim != null)
        {
            ApplyRadius(radius);
            _sim.Reset();
        }
    }

    private void ApplyRadius(double radius)
    {
        if (_setup == null) return;
        foreach (var type in _setup.CellTypes)
        {
            double ratio = type.MaxRadius / type.BaseRadius;
            type.BaseRadius = radius;
            type.MaxRadius = radius * ratio;
        }
    }

    // --- Input --------------------------------------------------------------

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_sim == null) return;

        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                ZoomAtMouse(1.15f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                ZoomAtMouse(1.0f / 1.15f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle, Pressed: true }:
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                _panning = true;
                _panStartScreen = GetViewport().GetMousePosition();
                _cameraStartPos = _camera.Position;
                break;
            case InputEventMouseButton { Pressed: false } when
                (@event is InputEventMouseButton b &&
                 (b.ButtonIndex == MouseButton.Middle || b.ButtonIndex == MouseButton.Right)):
                _panning = false;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                HandleLeftClick();
                break;
            case InputEventMouseMotion motion when _panning:
                var screen = GetViewport().GetMousePosition();
                _camera.Position = _cameraStartPos - (screen - _panStartScreen) / _camera.Zoom;
                break;
            case InputEventKey { Pressed: true } key:
                HandleKey(key);
                break;
        }
    }

    private void HandleLeftClick()
    {
        if (_sim == null) return;
        var world = _world.GetGlobalMousePosition();

        if (_deleteMode)
        {
            var cell = _sim.PickCell(world.X, world.Y);
            if (cell != null && _sim.RemoveCell(cell.Id))
                _selectedCellId = null;
        }
        else
        {
            var cell = _sim.PickCell(world.X, world.Y, tolerance: 4.0);
            _selectedCellId = cell?.Id;
        }
    }

    private void HandleKey(InputEventKey key)
    {
        if (_sim == null) return;
        switch (key.Keycode)
        {
            case Key.Space:
                if (key.Echo) break;
                OnPauseRequested();
                break;
            case Key.Period:
            case Key.Right:
                if (key.Echo) break;
                OnStepRequested();
                break;
            case Key.R:
                if (key.Echo) break;
                OnResetRequested(_sim.Config.InitialCells, _sim.Config.RngSeed, _sim.Config.CellRadius);
                break;
            case Key.Delete:
            case Key.Backspace:
                if (_selectedCellId is int id)
                {
                    _sim.RemoveCell(id);
                    _selectedCellId = null;
                }
                break;
            case Key.E:
                _deleteMode = !_deleteMode;
                _hud.SetDeleteMode(_deleteMode);
                break;
        }
    }

    private void HandleKeyboardPan(double delta)
    {
        float amount = 600.0f * (float)delta / Mathf.Max(0.1f, _camera.Zoom.X);
        if (Input.IsPhysicalKeyPressed(Key.W)) _camera.Position += new Vector2(0, -amount);
        if (Input.IsPhysicalKeyPressed(Key.S)) _camera.Position += new Vector2(0, amount);
        if (Input.IsPhysicalKeyPressed(Key.A)) _camera.Position += new Vector2(-amount, 0);
        if (Input.IsPhysicalKeyPressed(Key.D)) _camera.Position += new Vector2(amount, 0);
    }

    private void ZoomAtMouse(float factor)
    {
        var mouseScreen = GetViewport().GetMousePosition();
        var worldBefore = GetWorldMouse(mouseScreen);
        var newZoom = (_camera.Zoom * factor).Clamp(new Vector2(0.05f, 0.05f), new Vector2(8f, 8f));
        _camera.Zoom = newZoom;
        var worldAfter = GetWorldMouse(mouseScreen);
        _camera.Position += worldBefore - worldAfter;
    }

    private Vector2 GetWorldMouse(Vector2 screenPos)
    {
        if (_camera == null) return Vector2.Zero;
        var cam = _camera.GetScreenCenterPosition();
        var viewSize = GetViewport().GetVisibleRect().Size;
        var offset = (screenPos - viewSize * 0.5f) / _camera.Zoom;
        return cam + offset;
    }

    // --- Headless self test -------------------------------------------------

    private void RunSelfTest()
    {
        if (_sim == null) return;

        GD.Print("[selftest] config loaded: " +
                 $"{_setup!.Simulation.WorldWidth}x{_setup.Simulation.WorldHeight}, " +
                 $"{_setup.Morphogens.Length} morphogen(s), {_setup.CellTypes.Length} cell type(s)");

        // Reproducibility quick check: two identical seeds must reach identical counts.
        var checkA = new Simulation(_setup);
        for (int i = 0; i < 300; i++) checkA.Step(0.05);
        var checkB = new Simulation(_setup);
        for (int i = 0; i < 300; i++) checkB.Step(0.05);

        GD.Print($"[selftest] reproducibility: countA={checkA.Cells.Count} countB={checkB.Cells.Count} " +
                 $"identical={checkA.Cells.Count == checkB.Cells.Count}");

        const int steps = 4000;
        int progress = steps / 8;
        for (int s = 0; s < steps; s++)
        {
            _sim.Step(_sim.Config.FixedTimeStep);
            if ((s + 1) % progress == 0)
                GD.Print($"[selftest] step {s + 1}: cells={_sim.Cells.Count} time={_sim.Time:F1}s");
        }

        GD.Print($"[selftest] done. final cells={_sim.Cells.Count} ticks={_sim.Tick} time={_sim.Time:F1}s");
        GD.Print($"[selftest] morphogen mass={_sim.Morphogens.TotalMass(0):F2}");
        GetTree().Quit();
    }
}