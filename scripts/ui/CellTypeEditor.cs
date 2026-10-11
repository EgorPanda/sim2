using Godot;
using SimulationCore;
using System;
using System.Linq;

namespace Sim2;

/// <summary>
/// Cell phenotype editor: biological parameters of each cell type, optional
/// Russian display name/description, color, and the genome this type expresses.
/// Deleting warns about genes and experiment settings that reference the type.
/// </summary>
public partial class CellTypeEditor : VBoxContainer
{
    private readonly Main _main;
    private OptionButton? _list;
    private VBoxContainer? _card;
    private int _typeId = -1;

    public CellTypeEditor(Main main)
    {
        _main = main;
        Build();
    }

    private void Build()
    {
        var layout = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };

        var left = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(240, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        _list = new OptionButton();
        _list.ItemSelected += idx => { _typeId = (int)_list.GetItemId((int)idx); RebuildCard(); };
        left.AddChild(_list);
        left.AddChild(Ui.Btn("Добавить тип", () =>
        {
            var draft = _main.WorkingDraft;
            int id = ConfigDraft.UniqueCellTypeId(draft,
                draft.CellTypes.Length > 0 ? draft.CellTypes.Max(t => t.Id) + 1 : 1);
            var type = new CellTypeConfig
            {
                Id = id,
                Name = "cell",
                DisplayName = "Новый тип клетки",
                Description = "Опишите фенотип.",
                Color = "#63b3ed",
                GenomeId = draft.Genomes.Length > 0 ? draft.Genomes[0].Id : 0
            };
            draft.CellTypes = draft.CellTypes.Append(type).ToArray();
            _main.MarkDirty();
            RebuildList(type.Id);
        }));
        left.AddChild(Ui.Btn("Удалить", DeleteType));
        layout.AddChild(left);

        _card = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        layout.AddChild(_card);
        AddChild(layout);
    }

    private CellTypeConfig? Selected => _main.WorkingDraft.CellTypes.FirstOrDefault(t => t.Id == _typeId);

    internal int CurrentTypeId => _typeId;

    public void RefreshData()
    {
        if (_typeId < 0 && _main.WorkingDraft.CellTypes.Length > 0)
            _typeId = _main.WorkingDraft.CellTypes[0].Id;
        RebuildList(_typeId);
    }

    private void RebuildList(int? selectId = null)
    {
        if (_list == null || _card == null) return;
        var draft = _main.WorkingDraft;
        int? prev = selectId ?? _typeId;
        _list.Clear();
        for (int i = 0; i < draft.CellTypes.Length; i++)
        {
            var t = draft.CellTypes[i];
            _list.AddItem($"{t.Id}: {Localization.CellTypeName(t)}", t.Id);
        }
        if (prev is int p)
        {
            for (int i = 0; i < _list.ItemCount; i++)
            {
                if ((int)_list.GetItemId(i) == p)
                {
                    _typeId = p;
                    _list.Select(i);
                    break;
                }
            }
        }
        RebuildCard();
    }

    private void DeleteType()
    {
        var selected = Selected;
        if (selected == null) return;
        var refs = ConfigDraft.FindCellTypeReferences(_main.WorkingDraft, selected.Id);
        if (refs.Count > 0 || _main.WorkingDraft.CellTypes.Length <= 1)
        {
            _main.Notify("Нельзя удалить: на тип ссылаются " +
                        (refs.Count > 0 ? string.Join(", ", refs) : "(он единственный)."), error: true);
            return;
        }
        _main.WorkingDraft.CellTypes = _main.WorkingDraft.CellTypes.Where(t => t.Id != selected.Id).ToArray();
        _typeId = _main.WorkingDraft.CellTypes.Length > 0 ? _main.WorkingDraft.CellTypes[0].Id : -1;
        _main.MarkDirty();
        RebuildList(_typeId);
    }

    private void RebuildCard()
    {
        if (_card == null) return;
        Ui.Clear(_card);
        var t = Selected;
        if (t == null)
        {
            _card.AddChild(Ui.Lbl("Выберите тип клетки."));
            return;
        }

        _card.AddChild(Ui.Title($"Тип клетки «{Localization.CellTypeName(t)}» (ид {t.Id})"));

        _card.AddChild(Ui.Field("Название:", new LineEdit { Text = t.DisplayName, PlaceholderText = "русское название" }, 120)
            .Also(row => ((LineEdit)row.GetChild(1)).TextChanged += s =>
            {
                t.DisplayName = s;
                _main.MarkDirty();
                if (_list != null)
                {
                    for (int i = 0; i < _list.ItemCount; i++)
                    {
                        if ((int)_list.GetItemId(i) == t.Id)
                        {
                            _list.SetItemText(i, $"{t.Id}: {Localization.CellTypeName(t)}");
                            break;
                        }
                    }
                }
            }));
        _card.AddChild(Ui.Field("Техническое имя:", new LineEdit { Text = t.Name }, 120)
            .Also(row => ((LineEdit)row.GetChild(1)).TextChanged += s => { t.Name = s; _main.MarkDirty(); }));
        _card.AddChild(Ui.Field("Описание:", new TextEdit { Text = t.Description, CustomMinimumSize = new Vector2(0, 48) }, 120)
            .Also(row => ((TextEdit)row.GetChild(1)).TextChanged += () => { t.Description = ((TextEdit)row.GetChild(1)).Text; _main.MarkDirty(); }));

        var color = new ColorPickerButton { Color = Color.FromHtml(t.Color), CustomMinimumSize = new Vector2(140, 0) };
        color.ColorChanged += c => { t.Color = c.ToHtml(); _main.MarkDirty(); };
        _card.AddChild(Ui.Field("Цвет:", color, 120));

        var draft = _main.WorkingDraft;
        _card.AddChild(Ui.Field("Геном:",
            Ui.Opt(
                draft.Genomes.Select(g => (g.Id, $"{g.Id}: {g.Name}")),
                t.GenomeId,
                id => { t.GenomeId = id; _main.MarkDirty(); }, 200)));

        _card.AddChild(Ui.Field("Базовый радиус, ед.:",
            Ui.Sp(1, 100, 0.5, t.BaseRadius, v => { t.BaseRadius = v; _main.MarkDirty(); })));
        _card.AddChild(Ui.Field("Макс. радиус, ед.:",
            Ui.Sp(1, 200, 0.5, t.MaxRadius, v => { t.MaxRadius = v; _main.MarkDirty(); })));
    }
}