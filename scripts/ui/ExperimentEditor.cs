using Godot;
using SimulationCore;
using System.Linq;

namespace Sim2;

/// <summary>
/// Experiment settings tab: world size, solver resolution, initial population,
/// RNG seed, founder phenotype and the division-gradient morphogen. These map
/// directly onto SimulationConfig; they are applied on the next run. Dropdowns
/// referencing phenotypes/morphogens are rebuilt by <see cref="RefreshData"/>
/// so newly added or removed objects stay in sync.
/// </summary>
public partial class ExperimentEditor : VBoxContainer
{
    private readonly Main _main;
    private VBoxContainer? _pickers;
    private VBoxContainer? _initialSignalsBox;

    public ExperimentEditor(Main main)
    {
        _main = main;
        Build();
    }

    private void Build()
    {
        AddChild(Ui.Title("Параметры эксперимента"));
        AddChild(Ui.Lbl("Изменения применяются к следующему запуску симуляции."));

        var s = _main.WorkingDraft.Simulation;

        AddChild(Ui.Field("Имя эксперимента:", new LineEdit { Text = _main.ExperimentName }, 160)
            .Also(row => ((LineEdit)row.GetChild(1)).TextChanged += s2 => _main.SetExperimentName(s2)));
        AddChild(Ui.Field("Ширина мира, ед.:",
            Ui.Sp(64, 100000, 8, s.WorldWidth, v => { s.WorldWidth = v; _main.MarkDirty(); })));
        AddChild(Ui.Field("Высота мира, ед.:",
            Ui.Sp(64, 100000, 8, s.WorldHeight, v => { s.WorldHeight = v; _main.MarkDirty(); })));
        AddChild(Ui.Field("Разрешение поля, ед.:",
            Ui.Sp(1, 64, 0.5, s.MorphogenResolution, v => { s.MorphogenResolution = v; _main.MarkDirty(); })));
        AddChild(Ui.Field("Шаг симуляции, с:",
            Ui.Sp(0.001, 1, 0.001, s.FixedTimeStep, v => { s.FixedTimeStep = v; _main.MarkDirty(); })));
        AddChild(Ui.Field("Скорость по умолчанию:",
            Ui.Sp(0.1, 16, 0.1, s.DefaultSpeed, v => { s.DefaultSpeed = v; _main.MarkDirty(); })));
        AddChild(Ui.Field("Начальное число клеток:",
            Ui.Sp(1, 2000, 1, s.InitialCells, v => { s.InitialCells = (int)v; _main.MarkDirty(); })));
        AddChild(Ui.Field("Сид генератора:",
            Ui.Sp(0, 999999, 1, s.RngSeed, v => { s.RngSeed = (int)v; _main.MarkDirty(); })));

        _pickers = new VBoxContainer();
        AddChild(_pickers);
        BuildPickers(_pickers);

        AddChild(new HSeparator());

        var signalsHeader = new HBoxContainer();
        signalsHeader.AddChild(Ui.Title("Начальные сигналы"));
        signalsHeader.AddChild(Ui.Btn("?", () => _main.OpenHelp("experiment.initialSignals")));
        AddChild(signalsHeader);

        _initialSignalsBox = new VBoxContainer();
        AddChild(_initialSignalsBox);
        BuildInitialSignals();

        AddChild(new HSeparator());

        var warn = new Label
        {
            Text = "Изменения размеров мира или разрешения требуют перезапуска.",
            Modulate = new Color(1f, 0.85f, 0.4f)
        };
        AddChild(warn);
    }

    /// <summary>Rebuilds the phenotype/morphogen dropdowns after the draft changed.</summary>
    public void RefreshData()
    {
        if (_pickers == null) return;
        Ui.Clear(_pickers);
        BuildPickers(_pickers);
        if (_initialSignalsBox != null)
        {
            Ui.Clear(_initialSignalsBox);
            BuildInitialSignals();
        }
    }

    private void BuildInitialSignals()
    {
        if (_initialSignalsBox == null) return;
        var draft = _main.WorkingDraft;
        var s = draft.Simulation;

        var internalMorphogens = draft.Morphogens.Where(m => m.IsInternal).ToList();
        if (internalMorphogens.Count > 0)
        {
            _initialSignalsBox.AddChild(Ui.Lbl("Начальные внутренние концентрации основателей:", 0, 0));
            foreach (var m in internalMorphogens)
            {
                int id = m.Id;
                double value = s.InitialInternalMorphogens.TryGetValue(id, out double v) ? v : 0.0;
                _initialSignalsBox.AddChild(Ui.Field($"{Localization.MorphogenName(m)}:",
                    Ui.Sp(0, 1e6, 0.01, value, v =>
                    {
                        s.InitialInternalMorphogens[id] = v;
                        _main.MarkDirty();
                    }), 180));
            }
        }
        else
        {
            _initialSignalsBox.AddChild(Ui.Lbl("Нет внутренних морфогенов.", 0, 0));
        }

        _initialSignalsBox.AddChild(Ui.Lbl("Локальные внешние источники:", 0, 0));
        for (int i = 0; i < s.InitialExternalSources.Count; i++)
        {
            int idx = i;
            var source = s.InitialExternalSources[i];
            var row = new HBoxContainer();
            row.AddChild(Ui.Opt(
                draft.Morphogens.Where(m => m.IsExternal).Select(m => (m.Id, $"{m.Id}: {Localization.MorphogenName(m)}")),
                source.MorphogenId,
                id => { source.MorphogenId = id; _main.MarkDirty(); }, 140));
            row.AddChild(Ui.Sp(-1e6, 1e6, 1, source.X, v => { source.X = v; _main.MarkDirty(); }, 80));
            row.AddChild(Ui.Sp(-1e6, 1e6, 1, source.Y, v => { source.Y = v; _main.MarkDirty(); }, 80));
            row.AddChild(Ui.Sp(0, 1e6, 0.5, source.Radius, v => { source.Radius = v; _main.MarkDirty(); }, 70));
            row.AddChild(Ui.Sp(0, 1e6, 0.01, source.Concentration, v => { source.Concentration = v; _main.MarkDirty(); }, 80));
            var profileOpt = new OptionButton();
            profileOpt.AddItem("равномерно", 0);
            profileOpt.AddItem("гаусс", 1);
            profileOpt.Selected = string.Equals(source.Profile, "gaussian", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            profileOpt.ItemSelected += id =>
            {
                source.Profile = id == 1 ? "gaussian" : "uniform";
                _main.MarkDirty();
            };
            row.AddChild(profileOpt);
            row.AddChild(Ui.Btn("×", () =>
            {
                s.InitialExternalSources.RemoveAt(idx);
                _main.MarkDirty();
                RefreshData();
            }, 36));
            _initialSignalsBox.AddChild(row);
        }

        _initialSignalsBox.AddChild(Ui.Btn("Добавить внешний источник", () =>
        {
            var external = draft.Morphogens.FirstOrDefault(m => m.IsExternal);
            s.InitialExternalSources.Add(new MorphogenSourceConfig
            {
                MorphogenId = external?.Id ?? -1,
                X = s.WorldWidth * 0.5,
                Y = s.WorldHeight * 0.5,
                Radius = 40.0,
                Concentration = 1.0,
                Profile = "uniform"
            });
            _main.MarkDirty();
            RefreshData();
        }));
    }

    private void BuildPickers(VBoxContainer into)
    {
        var s = _main.WorkingDraft.Simulation;

        into.AddChild(Ui.Field("Тип клетки-основателя:",
            Ui.Opt(
                _main.WorkingDraft.CellTypes.Select(t => (t.Id, $"{t.Id}: {Localization.CellTypeName(t)}")),
                s.FounderTypeId >= 0 ? s.FounderTypeId : (_main.WorkingDraft.CellTypes.Length > 0 ? _main.WorkingDraft.CellTypes[0].Id : 0),
                id => { s.FounderTypeId = id; _main.MarkDirty(); }, 220)));
        into.AddChild(Ui.Field("Морфоген для деления:",
            Ui.Opt(
                new[] { (-1, "(не использовать)") }
                    .Concat(_main.WorkingDraft.Morphogens
                        .Where(m => m.IsExternal)
                        .Select(m => ((int)m.Id, $"{m.Id}: {Localization.MorphogenName(m)}"))),
                s.DivisionGradientMorphogen,
                id => { s.DivisionGradientMorphogen = id; _main.MarkDirty(); }, 220)));
    }
}