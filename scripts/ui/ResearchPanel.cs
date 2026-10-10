using Godot;
using SimulationCore;
using System.Linq;

namespace Sim2;

/// <summary>
/// "Лаборатория" — the research panel. Tabs: Мир (display layers), Геном,
/// Морфогены, Типы клеток, Настройки эксперимента and Справочник. Holds the
/// global action row (Применить / Проверить / Восстановить / Сохранить) and
/// the live validation output. It works on the working draft owned by
/// <see cref="Main"/> and never touches the running simulation directly.
/// </summary>
public partial class ResearchPanel : PanelContainer
{
    internal const int TabWorld = 0;
    internal const int TabGenome = 1;
    internal const int TabMorphogens = 2;
    internal const int TabTypes = 3;
    internal const int TabExperiment = 4;
    internal const int TabHelp = 5;

    private readonly Main _main;
    private TabContainer? _tabs;
    private GenomeEditor? _genome;
    private WorldLayersPanel? _worldTab;
    private MorphogenEditor? _morphogens;
    private CellTypeEditor? _types;
    private ExperimentEditor? _experiment;
    private Label? _dirtyLabel;
    private VBoxContainer? _issuesBox;
    private Label? _summary;

    internal int ActiveSection => _tabs?.CurrentTab ?? -1;
    internal GenomeEditor? GenomeTab => _genome;
    internal MorphogenEditor? MorphogenTab => _morphogens;
    internal string ActiveHelpKey => _tabs?.GetChild(TabHelp) is HelpPanel h ? h.CurrentKey : "";
    internal bool HelpPanelPlaceholderShown => _tabs?.GetChild(TabHelp) is HelpPanel h && h.PlaceholderShown;
    internal int LastErrorCount { get; private set; }

    public ResearchPanel(Main main)
    {
        _main = main;
        SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddThemeStyleboxOverride("panel", Ui.PanelStyle(0.94f, 10));

        // Content is scrollable so the panel stays usable when the window is
        // smaller than the panel's preferred size.
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto
        };
        var root = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(940, 540),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        scroll.AddChild(root);
        AddChild(scroll);
        root.AddChild(BuildToolbar());

        _tabs = new TabContainer
        {
            CustomMinimumSize = new Vector2(0, 380),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        root.AddChild(_tabs);

        _worldTab = new WorldLayersPanel(main);
        _tabs.AddChild(_worldTab);
        _tabs.SetTabTitle(TabWorld, "Мир");

        _genome = new GenomeEditor(main);
        _tabs.AddChild(_genome);
        _tabs.SetTabTitle(TabGenome, "Геном");

        _morphogens = new MorphogenEditor(main);
        _tabs.AddChild(_morphogens);
        _tabs.SetTabTitle(TabMorphogens, "Морфогены");

        _types = new CellTypeEditor(main);
        _tabs.AddChild(_types);
        _tabs.SetTabTitle(TabTypes, "Типы клеток");

        _experiment = new ExperimentEditor(main);
        _tabs.AddChild(_experiment);
        _tabs.SetTabTitle(TabExperiment, "Эксперимент");

        var helpTab = new HelpPanel(main);
        _tabs.AddChild(helpTab);
        _tabs.SetTabTitle(TabHelp, "Справочник");

        root.AddChild(BuildValidationBox());
        UpdateDirtyLabel();
        _summary!.Text = "Изменения в черновике не влияют на симуляцию до нажатия «Применить».";
    }

    private Control BuildToolbar()
    {
        var bar = new HBoxContainer();
        bar.AddChild(Ui.Btn("Применить", () => _main.ApplyDraft(), 90));
        bar.AddChild(Ui.Btn("Проверить", () => _main.ValidateWorkingDraft(showAll: true), 90));
        bar.AddChild(Ui.Btn("Восстановить", () => _main.RevertDraft(), 110));
        bar.AddChild(Ui.Btn("Сохранить", () => _main.SaveExperimentUi(), 90));
        bar.AddChild(Ui.Btn("Загрузить…", () => _main.LoadExperimentUi(), 100));

        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        bar.AddChild(spacer);

        _dirtyLabel = new Label { Text = "" };
        bar.AddChild(_dirtyLabel);
        bar.AddChild(Ui.Btn("Закрыть (Esc)", () => _main.ToggleLaboratory(), 110));
        return bar;
    }

    private Control BuildValidationBox()
    {
        var box = new VBoxContainer();
        _summary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_summary);
        _issuesBox = new VBoxContainer(); // recreated per validation run
        box.AddChild(_issuesBox);
        return box;
    }

    /// <summary>Called when the tab was hidden again.</summary>
    public void OnClosed()
    {
        // No-op: panel simply hides; state persists in the draft.
    }

    public void RefreshData()
    {
        _worldTab?.RefreshData();
        _genome?.RefreshData();
        _morphogens?.RefreshData();
        _types?.RefreshData();
        _experiment?.RefreshData();
        UpdateDirtyLabel();
    }

    public void UpdateDirtyLabel()
    {
        if (_dirtyLabel == null) return;
        _dirtyLabel.Text = _main.Dirty
            ? "Несохранённые изменения"
            : (_main.ExperimentPath == null ? "Черновик (без файла)" : "Всё сохранено");
        _dirtyLabel.AddThemeColorOverride("font_color",
            _main.Dirty ? new Color(1f, 0.8f, 0.3f) : new Color(0.7f, 0.95f, 0.7f));
    }

    public void OpenHelp(string key)
    {
        if (_tabs == null) return;
        _tabs.CurrentTab = TabHelp;
        if (_tabs.GetChild(TabHelp) is HelpPanel help)
            help.ShowKey(key);
    }

    public void OpenGene(string geneKey)
    {
        if (_tabs == null || _genome == null) return;
        _tabs.CurrentTab = TabGenome;
        _genome.OpenGene(geneKey);
    }

    public void OpenMorphogen(int morphogenId)
    {
        if (_tabs == null) return;
        _tabs.CurrentTab = TabMorphogens;
        if (_tabs.GetChild(TabMorphogens) is MorphogenEditor m)
            m.OpenMorphogen(morphogenId);
    }

    public void ShowValidation(IReadOnlyCollection<ConfigIssue> issues)
    {
        if (_issuesBox == null || _summary == null) return;
        Ui.Clear(_issuesBox);

        int errors = issues.Count(i => i.IsError);
        int warnings = issues.Count - errors;
        LastErrorCount = errors;

        if (issues.Count == 0)
        {
            _summary.Text = "Конфигурация корректна, ошибок нет.";
            return;
        }

        _summary.Text = $"Найдено проблем: {errors} ошибок, {warnings} предупреждений.";
        int shown = 0;
        foreach (var issue in issues)
        {
            if (shown++ >= 40) break;
            var line = new Label
            {
                Text = $"{(issue.IsError ? "【ошибка】" : "【предупреждение】")} " +
                       $"{issue.Section}: {issue.Message}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            line.AddThemeColorOverride("font_color",
                issue.IsError ? new Color(1f, 0.55f, 0.55f) : new Color(1f, 0.85f, 0.4f));
            if (!string.IsNullOrEmpty(issue.Hint)) line.TooltipText = issue.Hint;
            _issuesBox.AddChild(line);
            _issuesBox.AddChild(new Label { Text = "   ↳ " + issue.Location, Modulate = new Color(0.8f, 0.8f, 0.85f) });
        }
        if (issues.Count > 40)
            _issuesBox.AddChild(Ui.Lbl($"… и ещё {issues.Count - 40}.", 0, 0));
    }
}