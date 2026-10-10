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
                        .Select(m => ((int)m.Id, $"{m.Id}: {Localization.MorphogenName(m)}"))),
                s.DivisionGradientMorphogen,
                id => { s.DivisionGradientMorphogen = id; _main.MarkDirty(); }, 220)));
    }
}