using Godot;
using SimulationCore;
using System.Linq;

namespace Sim2;

/// <summary>
/// Russian on-screen shell: transport (пауза/шаг/сброс), speed, layer selector,
/// delete mode, laboratory toggle and help button on the left; live stats and
/// the selected-cell inspector on the right; a gradient legend and notifications
/// at the bottom. It only renders data and forwards commands — all biology lives
/// in SimulationCore, the lab panel logic in <see cref="ResearchPanel"/>.
/// </summary>
public partial class Hud : CanvasLayer
{
    private readonly Main _main;
    private Button? _pauseButton;
    private HSlider? _speedSlider;
    private OptionButton? _layerSelect;
    private Button? _labButton;
    private CheckBox? _deleteCheck;
    private Label? _statsLabel;
    private Label? _phenotypeLabel;
    private VBoxContainer? _selectionBox;
    private Label? _notifyLabel;
    private LegendBar? _legend;
    private int? _inspectorCell;
    private bool _suppressSignals;

    public Hud(Main main)
    {
        _main = main;
        BuildControlPanel();
        BuildStatsPanel();
        BuildLegend();
        UpdateNotify("Добро пожаловать в лабораторию. F1 — справка, L — панель, Пробел — пауза.");
    }

    private void BuildControlPanel()
    {
        var panel = new PanelContainer { Position = new Vector2(12, 12) };
        panel.AddThemeStyleboxOverride("panel", Ui.PanelStyle(0.55f, 8));
        var box = new VBoxContainer();
        panel.AddChild(box);
        AddChild(panel);

        var transport = new HBoxContainer();
        _pauseButton = Ui.Btn("Поставить на паузу", () => _main.TogglePause(), 150);
        transport.AddChild(_pauseButton);
        transport.AddChild(Ui.Btn("Шаг", () => _main.StepOnce(), 60));
        transport.AddChild(Ui.Btn("Сброс", () => _main.ResetSimulation(), 70));
        box.AddChild(transport);

        var speedRow = new HBoxContainer();
        speedRow.AddChild(new Label { Text = "Скорость", CustomMinimumSize = new Vector2(60, 0), VerticalAlignment = VerticalAlignment.Center });
        _speedSlider = new HSlider
        {
            MinValue = 0.1, MaxValue = 16.0, Step = 0.1,
            Value = _main.LiveSetup?.Simulation.DefaultSpeed ?? 1.0,
            CustomMinimumSize = new Vector2(160, 0)
        };
        _speedSlider.ValueChanged += v => _main.Speed = v;
        speedRow.AddChild(_speedSlider);
        speedRow.AddChild(new Label { Text = " ", CustomMinimumSize = new Vector2(36, 0) });
        box.AddChild(speedRow);

        var layerRow = new HBoxContainer();
        layerRow.AddChild(new Label { Text = "Слой", CustomMinimumSize = new Vector2(40, 0), VerticalAlignment = VerticalAlignment.Center });
        _layerSelect = new OptionButton { CustomMinimumSize = new Vector2(210, 0) };
        var layers = new (WorldLayer, string)[]
        {
            (WorldLayer.CellTypes, "Типы клеток"),
            (WorldLayer.Morphogen, "Концентрация"),
            (WorldLayer.Gradient, "Градиент"),
            (WorldLayer.GeneActivity, "Активность гена"),
            (WorldLayer.GeneState, "Подавленные гены"),
            (WorldLayer.Energy, "Энергия"),
            (WorldLayer.Neighbours, "Соседи"),
        };
        foreach (var (layer, text) in layers)
            _layerSelect.AddItem(text, (int)layer);
        _layerSelect.ItemSelected += idx =>
        {
            if (_suppressSignals) return;
            _main.CurrentLayer = (WorldLayer)(int)_layerSelect.GetItemId((int)idx);
            _main.RefreshLayer();
        };
        layerRow.AddChild(_layerSelect);
        box.AddChild(layerRow);

        var miscRow = new HBoxContainer();
        _deleteCheck = Ui.Chk("Режим удаления", false, on => _main.DeleteMode = on);
        miscRow.AddChild(_deleteCheck);
        _labButton = Ui.Btn("Лаборатория…", () => _main.ToggleLaboratory(), 130);
        miscRow.AddChild(_labButton);
        box.AddChild(miscRow);

        box.AddChild(Ui.Btn("Справочник (F1)", () => _main.OpenHelp("concept.gene_regulation"), 180));

        box.AddChild(new Label
        {
            Text = "ЛКМ — выбрать • ПКМ/средняя — панорама • колесо — зум\nПробел — пауза • . — шаг • R — сброс • E — удаление • L — панель • F1 — справка",
            MouseFilter = Control.MouseFilterEnum.Ignore
        });
    }

    private void BuildStatsPanel()
    {
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

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Ui.PanelStyle(0.55f, 8));
        panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        panel.Position = new Vector2(-16, 52);
        panel.GrowHorizontal = Control.GrowDirection.Begin;
        AddChild(panel);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        panel.AddChild(box);

        box.AddChild(new Label { Text = "— Фенотипы —" });
        _phenotypeLabel = new Label();
        box.AddChild(_phenotypeLabel);

        box.AddChild(new Label { Text = "— Выбранная клетка —" });
        _selectionBox = new VBoxContainer();
        box.AddChild(_selectionBox);
    }

    private void BuildLegend()
    {
        _legend = new LegendBar { Visible = false };
        AddChild(_legend);
        _notifyLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        AddChild(_notifyLabel);
        _notifyLabel.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _notifyLabel.Position = new Vector2(12, -34);
        _notifyLabel.GrowVertical = Control.GrowDirection.Begin;
    }

    // --- Commands from the environment / to Main ---------------------------

    public void SetPaused(bool paused)
    {
        if (_pauseButton == null) return;
        _pauseButton.Text = paused ? "Продолжить" : "Поставить на паузу";
    }

    public void SetDeleteMode(bool on)
    {
        _suppressSignals = true;
        if (_deleteCheck != null) _deleteCheck.SetPressedNoSignal(on);
        _suppressSignals = false;
    }

    public void SetLabOpen(bool open)
    {
        if (_labButton == null) return;
        _labButton.Text = open ? "Лаборатория (открыта)" : "Лаборатория…";
    }

    public void SetLayer(WorldLayer layer)
    {
        _suppressSignals = true;
        if (_layerSelect != null)
        {
            for (int i = 0; i < _layerSelect.ItemCount; i++)
            {
                if ((int)_layerSelect.GetItemId(i) == (int)layer)
                {
                    _layerSelect.Select(i);
                    break;
                }
            }
        }
        _suppressSignals = false;
    }

    // --- Per-frame refresh -------------------------------------------------

    public void RefreshStats()
    {
        if (_sim == null) return;
        var sim = _main.Sim!;
        var setup = _main.LiveSetup!;
        if (_statsLabel != null)
        {
            _statsLabel.Text =
                $"клеток: {sim.Cells.Count}   fps: {Engine.GetFramesPerSecond():F0}   " +
                $"время: {sim.Time:F1} с   такт: {sim.Tick}\n" +
                $"{(_main.Paused ? "ПАУЗА   " : "")}" +
                (_main.SelectedCellId is int id ? $"выбрано: #{id}" : "");
        }

        if (_phenotypeLabel != null)
            _phenotypeLabel.Text = _main.PhenotypeText;

        RefreshSelection();
        RefreshLegend();
    }

    private void RefreshSelection()
    {
        if (_selectionBox == null) return;
        int? id = _main.SelectedCellId;
        if (_inspectorCell == id) return;
        _inspectorCell = id;
        Ui.Clear(_selectionBox);
        if (id is not int cellId) return;

        var sim = _main.Sim;
        if (sim == null) return;
        var cell = sim.Cells.FirstOrDefault(c => c.Id == cellId);
        if (cell == null) return;
        var setup = _main.LiveSetup;
        if (setup == null) return;

        var type = setup.CellTypes.FirstOrDefault(t => t.Id == cell.TypeId);
        var genome = sim.GenomeOf(cell.GenomeId);

        string typeName = type != null ? Localization.CellTypeName(type) : $"(тип {cell.TypeId})";
        _selectionBox.AddChild(Ui.Lbl($"#{cell.Id}  {typeName}"));
        _selectionBox.AddChild(Ui.Lbl($"состояние: {Localization.State(cell.State)}  энергия: {cell.Energy:F1}/{type?.MaxEnergy:F0}"));
        _selectionBox.AddChild(Ui.Lbl($"радиус: {cell.Radius:F1}  возраст: {cell.Age:F1} с  соседи: {cell.NeighbourCount}"));

        if (genome != null)
        {
            var active = genome.Genes.Where(g => cell.StateOf(g.Key).Active).ToList();
            var silenced = genome.Genes.Where(g => cell.IsSilenced(g.Key)).ToList();
            _selectionBox.AddChild(Ui.Lbl($"гены активны: {active.Count}/{genome.Genes.Count}"));
            if (active.Count > 0)
            {
                foreach (var gene in active)
                {
                    var row = new HBoxContainer();
                    row.AddChild(Ui.Btn(Localization.GeneName(gene), () => _main.OpenGene(gene.Key)));
                    row.AddChild(Ui.Lbl("активен"));
                    _selectionBox.AddChild(row);
                }
            }
            if (silenced.Count > 0)
            {
                var row = new HBoxContainer();
                row.AddChild(Ui.Lbl("подавлены:"));
                foreach (var gene in silenced)
                {
                    var btn = Ui.Btn(Localization.GeneName(gene), () => _main.OpenGene(gene.Key));
                    btn.Modulate = new Color(1f, 0.6f, 0.6f);
                    row.AddChild(btn);
                }
                _selectionBox.AddChild(row);
            }

            if (cell.Timers.Count > 0)
            {
                _selectionBox.AddChild(Ui.Lbl("таймеры: " + string.Join(", ",
                    cell.Timers.Select(kv => $"{kv.Key} {kv.Value:F1}с"))));
            }
        }

        var fields = setup.Morphogens
            .Select(m => $"{Localization.MorphogenName(m)} {sim.SampleMorphogen(m.Id, cell.X, cell.Y):F2}");
        _selectionBox.AddChild(Ui.Lbl(string.Join("  ", fields)));
    }

    private void RefreshLegend()
    {
        if (_legend == null || _world == null) return;
        var world = _main.World;
        bool show = _main.CurrentLayer == WorldLayer.Morphogen;
        _legend.Visible = show;
        if (show) _legend.Show(world);
    }

    public void UpdateNotify(string text, bool error = false)
    {
        if (_notifyLabel == null) return;
        _notifyLabel.Text = text;
        _notifyLabel.AddThemeColorOverride("font_color",
            error ? new Color(1f, 0.5f, 0.5f) : new Color(0.92f, 0.95f, 1f));
    }

    private SimulationCore.Simulation? _sim => _main.Sim;
    private WorldView? _world => _main.World;
}

/// <summary>Bottom-centre legend strip: color ramp for the concentration layer.</summary>
public partial class LegendBar : Control
{
    private Label? _caption;
    private Label? _min;
    private Label? _max;
    private Color _color = Colors.DeepSkyBlue;
    private string _note = "";

    public LegendBar()
    {
        SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        Position = new Vector2(-180, -56);
        CustomMinimumSize = new Vector2(420, 44);
        GrowHorizontal = Control.GrowDirection.Both;
        MouseFilter = Control.MouseFilterEnum.Ignore;

        var boxLayout = new HBoxContainer();
        boxLayout.AnchorRight = 1;
        boxLayout.OffsetLeft = 40;
        boxLayout.OffsetRight = -40;
        boxLayout.OffsetTop = 2;
        boxLayout.OffsetBottom = 34;
        AddChild(boxLayout);

        _min = new Label { Text = "0", VerticalAlignment = VerticalAlignment.Center, CustomMinimumSize = new Vector2(44, 0) };
        _max = new Label { Text = "1", VerticalAlignment = VerticalAlignment.Center, CustomMinimumSize = new Vector2(44, 0) };
        boxLayout.AddChild(_min);
        boxLayout.AddChild(new Label { Text = "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }); // ramp area
        boxLayout.AddChild(_max);

        _caption = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0, AnchorRight = 1, OffsetTop = 34, OffsetBottom = 54,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        AddChild(_caption);
    }

    public void Show(WorldView world)
    {
        _color = world.MorphogenUiColor;
        _note = world.LegendNote;
        _min!.Text = Num.Fmt(world.LegendMin);
        _max!.Text = Num.Fmt(world.LegendMax);
        _caption!.Text = $"{world.LegendTitle}  •  {_note}";
        QueueRedraw();
    }

    public override void _Draw()
    {
        float h = Size.Y > 20 ? 26 : 12;
        var rectTop = new Rect2(44, 2, Size.X - 88, h);
        var from = new Color(_color, 0.0f);
        var to = _color;
        DrawRect(rectTop, from);
        // Simulate a ramp with several translucent rectangles.
        const int steps = 24;
        float stepW = rectTop.Size.X / steps;
        for (int i = 0; i < steps; i++)
        {
            float t = i / (float)steps;
            var c = new Color(_color, t * 0.65f);
            DrawRect(new Rect2(rectTop.Position.X + i * stepW, rectTop.Position.Y, stepW + 1, rectTop.Size.Y), c);
        }
        DrawRect(new Rect2(rectTop.Position, rectTop.Size), new Color(to, 0.0f), filled: false, width: 1.0f);
    }
}