using Godot;
using SimulationCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sim2;

/// <summary>
/// Root node of the Godot front-end. Owns the running simulation, camera, HUD and
/// the laboratory panel, controls stepping, and forwards input. Holds the working
/// draft (a deep clone of the setup) that the editors mutate; the draft only takes
/// effect when "Применить" validates and rebuilds the world. Contains no
/// biological rules — everything biological lives in SimulationCore.
/// </summary>
public partial class Main : Node2D
{
    private Simulation? _sim;
    private SimulationSetup? _setup;
    private SimulationSetup? _draft;
    private bool _dirty;
    private Camera2D _camera = null!;
    private WorldView _world = null!;
    private Hud _hud = null!;
    private ResearchPanel _lab = null!;

    private bool _paused;
    private bool _deleteMode;
    private int? _selectedCellId;
    private string? _experimentPath;
    private string _experimentName = "эксперимент";
    private int _overlayCounter;

    private bool _panning;
    private Vector2 _panStartScreen;
    private Vector2 _cameraStartPos;

    // --- Public state consumed by panels -----------------------------------

    public Simulation? Sim => _sim;
    public SimulationSetup? LiveSetup => _setup;
    public WorldView World => _world;
    public bool Paused => _paused;
    public bool Dirty => _dirty;
    public int? SelectedCellId { get => _selectedCellId; set => _selectedCellId = value; }
    public string? ExperimentPath => _experimentPath;
    public string ExperimentName => _experimentName;
    public double Speed { get; set; } = 1.0;
    public bool DeleteMode { get => _deleteMode; set => _deleteMode = value; }

    public WorldLayer CurrentLayer
    {
        get => _world.Layer;
        set => _world.Layer = value;
    }

    /// <summary>Returns the working draft, creating it from the live setup on first use.</summary>
    public SimulationSetup WorkingDraft
    {
        get
        {
            if (_draft == null)
            {
                _draft = ConfigLoader.DeepClone(_setup!);
                _dirty = false;
            }
            return _draft;
        }
    }

    public string PhenotypeText => BuildPhenotypeText();

    // --- Lifecycle ----------------------------------------------------------

    public override void _Ready()
    {
        _setup = LoadSetup();
        Speed = _setup.Simulation.DefaultSpeed;

        _sim = new Simulation(_setup);

        _camera = new Camera2D
        {
            Zoom = new Vector2(0.7f, 0.7f),
            Position = new Vector2((float)(_setup.Simulation.WorldWidth * 0.5),
                (float)(_setup.Simulation.WorldHeight * 0.5))
        };
        AddChild(_camera);
        _camera.MakeCurrent();

        _world = new WorldView { Sim = _sim };
        int morphogenId = _setup.Morphogens.Length > 0 ? _setup.Morphogens[0].Id : 0;
        _world.MorphogenId = morphogenId;
        _world.SetCellColors(_setup.CellTypes, morphogenId);
        ApplyMorphogenStyle(morphogenId);
        AddChild(_world);

        _hud = new Hud(this);
        AddChild(_hud);

        _lab = new ResearchPanel(this);
        _lab.Visible = false;
        _hud.AddChild(_lab);

        RefreshLayer();

        if (OS.GetCmdlineUserArgs().Contains("--selftest"))
            RunSelfTest();
        if (OS.GetCmdlineUserArgs().Contains("--uitest"))
            Callable.From(StartUiTest).CallDeferred();
    }

    private static SimulationSetup LoadSetup()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--experiment")
                return ConfigLoader.LoadExperiment(ProjectSettings.GlobalizePath(args[i + 1]));
        }
        return ConfigLoader.LoadFromDirectory(ProjectSettings.GlobalizePath("res://config"));
    }

    public override void _Process(double delta)
    {
        if (_sim == null || _setup == null) return;

        if (!_paused)
        {
            double stepDt = _sim.Config.FixedTimeStep;
            _accumulator += delta * Speed;
            int guard = 0;
            while (_accumulator >= stepDt && guard < 64)
            {
                _sim.Step(stepDt);
                _accumulator -= stepDt;
                guard++;
            }
            if (guard >= 64) _accumulator = 0.0;
        }

        HandleKeyboardPan(delta);

        if (CurrentLayer == WorldLayer.Morphogen && Engine.GetFramesPerSecond() > 0)
        {
            if (++_overlayCounter % 3 == 0) RefreshLayer();
        }

        _world.SelectedCellId = _selectedCellId;
        _world.QueueRedraw();

        _hud.RefreshStats();
    }

    private double _accumulator;

    // --- Draft / apply / validate ------------------------------------------

    public void RefreshDraft()
    {
        _draft = _setup != null ? ConfigLoader.DeepClone(_setup) : null;
        _dirty = false;
        _lab.RefreshData();
    }

    public void MarkDirty()
    {
        _dirty = true;
        _lab.UpdateDirtyLabel();
    }

    public void RevertDraft()
    {
        RefreshDraft();
        Notify("Изменения отменены — редактор возвращён к текущему состоянию симуляции.");
    }

    public void ValidateWorkingDraft(bool showAll = false)
    {
        var issues = ConfigValidator.Validate(WorkingDraft);
        _lab.ShowValidation(issues);
        if (issues.Count(i => i.IsError) == 0)
            Notify(issues.Count == 0
                ? "Проверка: конфигурация корректна."
                : $"Проверка: {issues.Count} предупреждение(й), критических ошибок нет.");
        else
            Notify($"Проверка: {issues.Count(i => i.IsError)} ошибок — см. панель.", error: true);
    }

    public void ApplyDraft()
    {
        var issues = ConfigValidator.Validate(WorkingDraft);
        _lab.ShowValidation(issues);
        if (issues.Any(i => i.IsError))
        {
            Notify($"Не применено: {issues.Count(i => i.IsError)} ошибок в конфигурации — исправьте и проверьте.", error: true);
            return;
        }

        ApplySetup(ConfigLoader.DeepClone(WorkingDraft));
        _dirty = false;
        _lab.UpdateDirtyLabel();
        _lab.RefreshData();
        Notify("Драфт применён: мир пересобран (без биологической аналитики — только биология ядра).");
    }

    private void ApplySetup(SimulationSetup newSetup)
    {
        _setup = newSetup;
        _sim = new Simulation(_setup);
        _accumulator = 0.0;
        _selectedCellId = null;
        _world.Sim = _sim;
        int mid = _setup.Morphogens.Length > 0 ? _setup.Morphogens[0].Id : 0;
        _world.SetCellColors(_setup.CellTypes, mid);
        _world.MorphogenId = mid;
        ApplyMorphogenStyle(mid);
        RefreshLayer();
        _hud.SetLayer(CurrentLayer);
    }

    // --- Saving / loading experiment profiles -------------------------------

    public void SaveExperimentUi()
    {
        if (_experimentPath != null)
        {
            SaveExperimentTo(_experimentPath);
            return;
        }
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.SaveFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Сохранить эксперимент (файл профиля *.json)",
            Filters = new[] { "*.json ; Профиль эксперимента" },
            CurrentFile = "experiment.json"
        };
        dialog.FileSelected += path =>
        {
            dialog.QueueFree();
            SaveExperimentTo(path);
        };
        dialog.Canceled += () => dialog.QueueFree();
        _hud.AddChild(dialog);
        dialog.PopupCentered(new Vector2I(760, 480));
    }

    public void SaveExperimentTo(string profilePath)
    {
        string full = System.IO.Path.GetFullPath(profilePath);
        string dir = System.IO.Path.GetDirectoryName(full)!;
        string profileName = System.IO.Path.GetFileNameWithoutExtension(full);
        string filesDir = System.IO.Path.Combine(dir, profileName);
        System.IO.Directory.CreateDirectory(filesDir);

        ConfigWriter.SaveSetupToDirectory(filesDir, WorkingDraft);

        var profile = new ExperimentConfig
        {
            Name = _experimentName,
            Description = "Сохранено из лаборатории Sim2.",
            SimulationFile = $"{profileName}/simulation.json",
            MorphogenFile = $"{profileName}/morphogens.json",
            GenomeFile = $"{profileName}/genomes.json",
            CellTypeFile = $"{profileName}/celltypes.json",
            AdhesionFile = $"{profileName}/adhesion.json"
        };
        System.IO.File.WriteAllText(full, ConfigLoader.SerializeExperiment(profile));

        _experimentPath = full;
        _dirty = false;
        _lab.UpdateDirtyLabel();
        Notify($"Сохранено: {full}");
    }

    public void LoadExperimentUi()
    {
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Загрузить эксперимент",
            Filters = new[] { "*.json ; Профиль эксперимента" }
        };
        dialog.FileSelected += path =>
        {
            dialog.QueueFree();
            try
            {
                var loaded = ConfigLoader.LoadExperiment(System.IO.Path.GetFullPath(path));
                _experimentPath = System.IO.Path.GetFullPath(path);
                _experimentName = System.IO.Path.GetFileNameWithoutExtension(path);
                ApplySetup(loaded);
                RefreshDraft();
                Notify($"Загружен эксперимент: {_experimentName}");
            }
            catch (System.Exception ex)
            {
                Notify($"Не удалось загрузить: {ex.Message}", error: true);
            }
        };
        dialog.Canceled += () => dialog.QueueFree();
        _hud.AddChild(dialog);
        dialog.PopupCentered(new Vector2I(760, 480));
    }

    public void SetExperimentName(string name)
    {
        _experimentName = name;
        _dirty = true;
        _lab.UpdateDirtyLabel();
    }

    // --- Commands from the HUD ----------------------------------------------

    public void TogglePause()
    {
        _paused = !_paused;
        _hud.SetPaused(_paused);
    }

    public void StepOnce()
    {
        if (_sim == null) return;
        _sim.Step(_sim.Config.FixedTimeStep);
        _accumulator = 0.0;
        _world.QueueRedraw();
    }

    public void ResetSimulation()
    {
        if (_sim == null || _setup == null) return;
        _sim.Reset();
        _selectedCellId = null;
        _accumulator = 0.0;
        _world.QueueRedraw();
        Notify("Симуляция перезапущена");
    }

    public void ToggleLaboratory()
    {
        if (!_lab.Visible) EnsureWorkingDraftFresh();
        _lab.Visible = !_lab.Visible;
        _hud.SetLabOpen(_lab.Visible);
        if (_lab.Visible) _lab.RefreshData();
    }

    private void EnsureWorkingDraftFresh()
    {
        if (_draft == null)
            RefreshDraft();
    }

    /// <summary>Makes the laboratory visible (without toggling it closed).</summary>
    private void ShowLaboratorySync()
    {
        if (!_lab.Visible)
        {
            _lab.Visible = true;
            _hud.SetLabOpen(true);
        }
        _lab.RefreshData();
    }

    public void OpenHelp(string key)
    {
        EnsureWorkingDraftFresh();
        ShowLaboratorySync();
        _lab.OpenHelp(key);
    }

    public void OpenGene(string geneKey)
    {
        EnsureWorkingDraftFresh();
        ShowLaboratorySync();
        _lab.OpenGene(geneKey);
    }

    public void OpenMorphogen(int morphogenId)
    {
        EnsureWorkingDraftFresh();
        ShowLaboratorySync();
        _lab.OpenMorphogen(morphogenId);
    }

    public void Notify(string message, bool error = false) => _hud.UpdateNotify(message, error);

    public void ApplyMorphogenStyle(int morphogenId)
    {
        if (_setup == null || _world == null) return;
        var cfg = _setup.Morphogens.FirstOrDefault(m => m.Id == morphogenId);
        _world.SetMorphogenStyle(cfg?.Color ?? "#4ea8de", 1.0);
    }

    public void RefreshLayer()
    {
        if (_world == null) return;
        _world.RefreshOverlay(everyNFrames: 1);
        if (_hud != null)
        {
            _hud.SetLayer(CurrentLayer);
        }
        _world.QueueRedraw();
    }

    // --- Text builders ------------------------------------------------------

    private string BuildPhenotypeText()
    {
        if (_setup == null || _sim == null) return string.Empty;
        var counts = new Dictionary<int, int>();
        foreach (var cell in _sim.Cells)
        {
            counts.TryGetValue(cell.TypeId, out int n);
            counts[cell.TypeId] = n + 1;
        }

        var sb = new StringBuilder();
        foreach (var type in _setup.CellTypes)
        {
            counts.TryGetValue(type.Id, out int n);
            sb.AppendLine($"{Localization.CellTypeName(type)}: {n}");
        }
        return sb.ToString().TrimEnd();
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
        if (_sim == null || key.Echo) return;
        switch (key.Keycode)
        {
            case Key.Space:
                TogglePause();
                break;
            case Key.Period:
            case Key.Right:
                StepOnce();
                break;
            case Key.R:
                ResetSimulation();
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
            case Key.L:
                ToggleLaboratory();
                break;
            case Key.Escape:
                if (_lab.Visible) ToggleLaboratory();
                break;
            case Key.F1:
                OpenHelp("concept.gene_regulation");
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
        var cam = _camera.GetScreenCenterPosition();
        var viewSize = GetViewport().GetVisibleRect().Size;
        var offset = (screenPos - viewSize * 0.5f) / _camera.Zoom;
        return cam + offset;
    }

    // --- Headless self test -------------------------------------------------

    private void RunSelfTest()
    {
        if (_sim == null || _setup == null) return;

        GD.Print("[selftest] config loaded: " +
                 $"{_setup.Simulation.WorldWidth}x{_setup.Simulation.WorldHeight}, " +
                 $"{_setup.Morphogens.Length} morphogen(s), {_setup.CellTypes.Length} cell type(s)");

        var checkA = new Simulation(_setup);
        for (int i = 0; i < 300; i++) checkA.Step(0.05);
        var checkB = new Simulation(_setup);
        for (int i = 0; i < 300; i++) checkB.Step(0.05);

        GD.Print($"[selftest] reproducibility: countA={checkA.Cells.Count} countB={checkB.Cells.Count} " +
                 $"identical={checkA.Cells.Count == checkB.Cells.Count}");

        const int steps = 4000;
        for (int s = 0; s < steps; s++)
        {
            _sim.Step(_sim.Config.FixedTimeStep);
            if ((s + 1) % (steps / 8) == 0)
                GD.Print($"[selftest] step {s + 1}: cells={_sim.Cells.Count} time={_sim.Time:F1}s");
        }

        GD.Print($"[selftest] done. final cells={_sim.Cells.Count} ticks={_sim.Tick} time={_sim.Time:F1}s");
        GD.Print($"[selftest] morphogen mass={_sim.Morphogens.TotalMass(0):F2}");
        GetTree().Quit();
    }

    // --- Headless / windowed UI smoke test (--uitest) ------------------------

    private int _uiFailures;

    private void StartUiTest() => _ = RunUiTest();

    private void Expect(bool ok, string what)
    {
        if (!ok) _uiFailures++;
        GD.Print($"[uitest] {(ok ? "ok  " : "FAIL")} {what}");
    }

    private void SendKey(Key key)
    {
        var ev = new InputEventKey { Keycode = key, Pressed = true };
        GetViewport().PushInput(ev);
    }

    /// <summary>
    /// Drives the real input/routing paths used by buttons, L/F1 keys, the cell
    /// inspector and the editors, then quits with exit code 0 on success.
    /// Uses actual scene nodes and state — not source-level string matching.
    /// </summary>
    private async Task RunUiTest()
    {
        if (_sim == null || _setup == null) { GD.PushError("[uitest] no simulation"); GetTree().Quit(2); return; }

        _paused = true;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Expect(!_lab.Visible, "laboratory hidden initially");
        Expect(_lab.Size.X > 0 && _lab.Size.Y > 0, "laboratory has nonzero planned size while hidden");

        // 1. Open via the same method the «Лаборатория» button invokes.
        ToggleLaboratory();
        ExpectedVisibleAndGeometry("button-equivalent opens laboratory", ResearchPanel.TabWorld);
        Expect(_hud.LabButtonText == "Лаборатория (открыта)", "lab button text reflects open state");

        // 2. Real key route: L toggles closed and open again.
        SendKey(Key.L);
        await Frame();
        Expect(!_lab.Visible, "L key closes laboratory");
        Expect(_hud.LabButtonText == "Лаборатория…", "lab button text reflects closed state");
        SendKey(Key.L);
        await Frame();
        Expect(_lab.Visible, "L key opens laboratory");

        // 3. Reopen reuses the same panel — no copies are created.
        ulong labId = _lab.GetInstanceId();
        ToggleLaboratory();
        await Frame();
        ToggleLaboratory();
        await Frame();
        Expect(_lab.GetInstanceId() == labId, "reopening reuses the same panel instance");

        // 4. F1 (real key route) opens help directly from the hidden state.
        ToggleLaboratory();
        await Frame();
        Expect(!_lab.Visible, "lab closed before F1 test");
        SendKey(Key.F1);
        await Frame();
        Expect(_lab.Visible, "F1 opens laboratory");
        Expect(_lab.ActiveSection == ResearchPanel.TabHelp, "F1 selects help tab");
        Expect(_lab.ActiveHelpKey == "concept.gene_regulation", "F1 shows gene-regulation article");

        // 5. Help button route and context link route.
        OpenHelp("logic.and");
        await Frame();
        Expect(_lab.Visible && _lab.ActiveSection == ResearchPanel.TabHelp && _lab.ActiveHelpKey == "logic.and",
            "OpenHelp(logic.and) shows the requested article");

        // 6. Unknown key → understandable fallback, never an empty panel.
        OpenHelp("does.not.exist");
        await Frame();
        Expect(_lab.ActiveHelpKey == "does.not.exist", "unknown help key is routed");
        Expect(_lab.HelpPanelPlaceholderShown, "unknown help key shows fallback note");

        // 7. Gene route from the cell inspector opens the right genome and card.
        var targetGene = WorkingDraft.Genomes
            .SelectMany(g => g.Genes)
            .FirstOrDefault()?.Key ?? "";
        Expect(targetGene.Length > 0, "config has at least one gene for routing test");
        if (targetGene.Length > 0)
        {
            OpenGene(targetGene);
            await Frame();
            Expect(_lab.Visible && _lab.ActiveSection == ResearchPanel.TabGenome, "OpenGene opens genome tab");
            Expect(_lab.GenomeTab?.CurrentGeneKey == targetGene, "OpenGene selects the requested gene");
            Expect(_lab.GenomeTab?.GeneCardShows(targetGene) == true, "gene card shows the routed gene");
        }

        // 8. Morphogen route.
        int mid = WorkingDraft.Morphogens.FirstOrDefault()?.Id ?? -1;
        Expect(mid >= 0, "config has at least one morphogen for routing test");
        if (mid >= 0)
        {
            OpenMorphogen(mid);
            await Frame();
            Expect(_lab.Visible && _lab.ActiveSection == ResearchPanel.TabMorphogens, "OpenMorphogen opens morphogen tab");
            Expect(_lab.MorphogenTab?.CurrentMorphogenId == mid, "OpenMorphogen selects the requested morphogen");
        }

        // 9. Valid draft: validate + apply rebuilds the world without errors.
        int cellsBefore = _sim.Cells.Count;
        Expect(cellsBefore > 0, "simulation has cells");
        ValidateWorkingDraft(showAll: true);
        Expect(_lab.LastErrorCount == 0, "default config validates without errors");
        ApplyDraft();
        Expect(_sim.Time == 0.0 && _sim.Cells.Count == cellsBefore, "applying valid draft rebuilds the world");

        // 10. Invalid draft: validate reports it and apply must not touch the world.
        var oldSim = _sim;
        WorkingDraft.Simulation.FounderTypeId = _setup.Simulation.FounderTypeId + 9999;
        ValidateWorkingDraft(showAll: true);
        Expect(_lab.LastErrorCount > 0, "invalid draft is reported");
        ApplyDraft();
        Expect(ReferenceEquals(_sim, oldSim) && _sim.Cells.Count == cellsBefore,
            "applying an invalid draft leaves the world untouched");
        WorkingDraft.Simulation.FounderTypeId = _setup.Simulation.FounderTypeId;
        ApplyDraft();
        Expect(!ReferenceEquals(_sim, oldSim) && _sim.Time == 0.0, "apply works after fixing the draft");

        // 11. Layer switching never mutates the biological state.
        _paused = true;
        int tick0 = (int)_sim.Tick;
        int cellsAfter = _sim.Cells.Count;
        foreach (WorldLayer layer in Enum.GetValues<WorldLayer>())
        {
            CurrentLayer = layer;
            RefreshLayer();
        }
        await Frame();
        Expect(_sim.Tick == tick0 && _sim.Cells.Count == cellsAfter, "layer switching does not mutate simulation state");

        // 12. Saving an experiment with an unusual name produces valid JSON that reloads.
        RunSaveProfileTest();

        GD.Print($"[uitest] DONE failures={_uiFailures}");
        GetTree().Quit(_uiFailures == 0 ? 0 : 1);
    }

    private async Task Frame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void ExpectedVisibleAndGeometry(string what, int expectedTab)
    {
        Expect(_lab.Visible, what + " (visible)");
        var rect = _lab.GetGlobalRect();
        var viewport = GetViewport().GetVisibleRect();
        Expect(rect.Size.X > 0 && rect.Size.Y > 0, what + " (nonzero size)");
        Expect(rect.Position.X >= -1 && rect.Position.Y >= -1 &&
               rect.End.X <= viewport.Size.X + 1 && rect.End.Y <= viewport.Size.Y + 1,
            what + " (within viewport)");
        if (expectedTab >= 0)
            Expect(_lab.ActiveSection == expectedTab, what + " (tab = " + expectedTab + ")");
    }

    private void RunSaveProfileTest()
    {
        string weird = "профиль \"с\" \\кавычками\nи 'переносом' + ☂";
        SetExperimentName(weird);
        string tmpDir = System.IO.Path.Combine(ProjectSettings.GlobalizePath(OS.GetUserDataDir()), "uitest_save");
        string profilePath = System.IO.Path.Combine(tmpDir, "профиль.json");
        try
        {
            System.IO.Directory.CreateDirectory(tmpDir);
            SaveExperimentTo(profilePath);
            var exp = ConfigLoader.ParseExperiment(System.IO.File.ReadAllText(profilePath));
            Expect(exp.Name == weird, "profile JSON escaping: unusual name round-trips");
            var loaded = ConfigLoader.LoadExperiment(profilePath);
            Expect(loaded.Morphogens.Length == _setup!.Morphogens.Length &&
                   loaded.Genomes.Length == _setup!.Genomes.Length &&
                   loaded.CellTypes.Length == _setup!.CellTypes.Length,
                "saved profile reloads the full configuration");
        }
        catch (Exception ex)
        {
            Expect(false, "save-profile test threw: " + ex.Message);
        }
        finally
        {
            if (_experimentPath == profilePath) _experimentPath = null;
            if (System.IO.Directory.Exists(tmpDir)) System.IO.Directory.Delete(tmpDir, true);
        }
    }
}