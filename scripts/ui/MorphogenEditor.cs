using Godot;
using SimulationCore;
using System;
using System.Linq;

namespace Sim2;

/// <summary>
/// Morphogen editor: display name, technical name, description, diffusion,
/// decay and color. Deleting shows a dependency list of the genes that use it.
/// Edits go straight into the working draft.
/// </summary>
public partial class MorphogenEditor : VBoxContainer
{
private readonly Main _main;
    private OptionButton? _list;
    private VBoxContainer? _card;
    private int _morphogenId = -1;

    public MorphogenEditor(Main main)
    {
        _main = main;
        Build();
    }

    private void Build()
    {
        var layout = new HBoxContainer();

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(240, 0) };
        _list = new OptionButton();
        _list.ItemSelected += idx => { _morphogenId = (int)_list.GetItemId((int)idx); RebuildCard(); };
        left.AddChild(_list);
        left.AddChild(Ui.Btn("Добавить морфоген", () =>
        {
            var draft = _main.WorkingDraft;
            int id = draft.Morphogens.Length > 0 ? draft.Morphogens.Max(m => m.Id) + 1 : 1;
            draft.Morphogens = draft.Morphogens.Append(new MorphogenConfig { Id = id, Name = "morphogen", DisplayName = "Новый морфоген" }).ToArray();
            _main.MarkDirty();
            RebuildList(id);
        }));
        left.AddChild(Ui.Btn("Удалить", DeleteMorphogen));
        layout.AddChild(left);

        _card = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        layout.AddChild(_card);
        AddChild(layout);
    }

    private MorphogenConfig? Selected => _main.WorkingDraft.Morphogens.FirstOrDefault(m => m.Id == _morphogenId);

    internal int CurrentMorphogenId => _morphogenId;

    public void RefreshData()
    {
        if (_morphogenId < 0 && _main.WorkingDraft.Morphogens.Length > 0)
            _morphogenId = _main.WorkingDraft.Morphogens[0].Id;
        RebuildList(_morphogenId);
    }

    public void OpenMorphogen(int morphogenId)
    {
        _morphogenId = morphogenId;
        RebuildList(_morphogenId);
    }

    private void RebuildList(int? selectId = null)
    {
        if (_list == null || _card == null) return;
        var draft = _main.WorkingDraft;
        int? prev = selectId ?? _morphogenId;
        _list.Clear();
        for (int i = 0; i < draft.Morphogens.Length; i++)
        {
            var m = draft.Morphogens[i];
            _list.AddItem($"{m.Id}: {Localization.MorphogenName(m)}");
        }
        if (prev is int p)
        {
            for (int i = 0; i < _list.ItemCount; i++)
            {
                if ((int)_list.GetItemId(i) == p)
                {
                    _morphogenId = p;
                    _list.Select(i);
                    break;
                }
            }
        }
        RebuildCard();
    }

    private void DeleteMorphogen()
    {
        var selected = Selected;
        if (selected == null) return;
        var refs = ConfigDraft.FindMorphogenReferences(_main.WorkingDraft, selected.Id);
        if (refs.Count > 0 || _main.WorkingDraft.Morphogens.Length <= 1)
        {
            _main.Notify("Нельзя удалить: морфоген используют: " +
                        (refs.Count > 0 ? string.Join(", ", refs) : "он единственный в среде."), error: true);
            return;
        }
        _main.WorkingDraft.Morphogens = _main.WorkingDraft.Morphogens
            .Where(m => m.Id != selected.Id).ToArray();
        _main.WorkingDraft.Simulation.DivisionGradientMorphogen = Math.Min(
            _main.WorkingDraft.Simulation.DivisionGradientMorphogen, _main.WorkingDraft.Morphogens.Length - 1);
        _main.MarkDirty();
        RebuildList();
    }

    private void RebuildCard()
    {
        if (_card == null) return;
        Ui.Clear(_card);
        var m = Selected;
        if (m == null)
        {
            _card.AddChild(Ui.Lbl("Выберите морфоген."));
            return;
        }

        _card.AddChild(Ui.Title($"Морфоген «{Localization.MorphogenName(m)}» (ид {m.Id})"));
        _card.AddChild(Ui.Field("Название:", new LineEdit
        {
            Text = m.DisplayName,
            PlaceholderText = "русское название, напр. «Фактор деления»"
        }, 120).Also(row => ((LineEdit)row.GetChild(1)).TextChanged += t =>
        {
            m.DisplayName = t;
            _main.MarkDirty();
            if (_list != null)
            {
                for (int i = 0; i < _list.ItemCount; i++)
                {
                    if ((int)_list.GetItemId(i) == m.Id)
                    {
                        _list.SetItemText(i, $"{m.Id}: {Localization.MorphogenName(m)}");
                        break;
                    }
                }
            }
        }));

        _card.AddChild(Ui.Field("Техническое имя:", new LineEdit { Text = m.Name, PlaceholderText = "напр. morphogen_a" }, 120)
            .Also(row => ((LineEdit)row.GetChild(1)).TextChanged += t => { m.Name = t; _main.MarkDirty(); }));

        _card.AddChild(Ui.Field("Описание:", new TextEdit { Text = m.Description, CustomMinimumSize = new Vector2(0, 56) }, 120)
            .Also(row =>
            {
                var te = (TextEdit)row.GetChild(1);
                te.TextChanged += () => { m.Description = te.Text; _main.MarkDirty(); };
            }));

        _card.AddChild(Ui.Field("Диффузия, 1/с:",
            Ui.Sp(0, 50, 0.05, m.Diffusion, v => { m.Diffusion = v; _main.MarkDirty(); })));
        _card.AddChild(Ui.Field("Распад, 1/с:",
            Ui.Sp(0, 50, 0.05, m.Decay, v => { m.Decay = v; _main.MarkDirty(); })));

        var color = new ColorPickerButton
        {
            Color = Color.FromHtml(m.Color),
            CustomMinimumSize = new Vector2(140, 0)
        };
        color.ColorChanged += c =>
        {
            m.Color = c.ToHtml();
            _main.MarkDirty();
            _main.ApplyMorphogenStyle(m.Id);
        };
        _card.AddChild(Ui.Field("Цвет на карте:", color, 120));
        _card.AddChild(Ui.Lbl("Используется слоем «Концентрация» и стрелками слоя «Градиент».", 0, 0));
    }
}