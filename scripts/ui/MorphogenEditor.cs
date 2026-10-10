using Godot;
using SimulationCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sim2;

/// <summary>
/// Morphogen editor: left column has a search box and a filterable list of
/// morphogens (stable ids selected by id, never by row index), the right column
/// shows a card for the selected morphogen (display name, technical name,
/// description, diffusion, decay, color). Germane management buttons mirror the
/// genome tab: add, copy, delete (with reference confirmation and dependency
/// cleanup), apply, save and revert. Deleting the last morphogen is safe and
/// shows an empty state instead of erroring.
/// </summary>
public partial class MorphogenEditor : VBoxContainer
{
    private readonly Main _main;
    private VBoxContainer? _listBox;
    private LineEdit? _search;
    private VBoxContainer? _card;
    private string _searchText = "";
    private int _morphogenId = -1;
    private readonly List<(Button Btn, int Id)> _entries = new();

    public MorphogenEditor(Main main)
    {
        _main = main;
        Build();
    }

    private void Build()
    {
        var actions = new HBoxContainer();
        actions.AddChild(Ui.Btn("Добавить морфоген", AddMorphogen));
        actions.AddChild(Ui.Btn("Копировать", CopyMorphogen));
        actions.AddChild(Ui.Btn("Удалить", DeleteMorphogen));
        actions.AddChild(Ui.Btn("Применить изменения", () => _main.ApplyDraft()));
        actions.AddChild(Ui.Btn("Сохранить конфигурацию", () => _main.SaveExperimentUi()));
        actions.AddChild(Ui.Btn("Отменить изменения", () =>
        {
            _main.RevertDraft();
            RefreshData();
        }));
        AddChild(actions);

        var layout = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(240, 0) };
        _search = new LineEdit { PlaceholderText = "Поиск по имени или ид…" };
        _search.TextChanged += t =>
        {
            _searchText = t.Trim();
            RebuildList(_morphogenId);
        };
        left.AddChild(_search);

        _listBox = new VBoxContainer();
        var listScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(240, 320),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        listScroll.AddChild(_listBox);
        left.AddChild(listScroll);
        layout.AddChild(left);

        var cardScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _card = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cardScroll.AddChild(_card);
        layout.AddChild(cardScroll);

        AddChild(layout);
    }

    private MorphogenConfig? Selected => _main.WorkingDraft.Morphogens.FirstOrDefault(m => m.Id == _morphogenId);

    internal int CurrentMorphogenId => _morphogenId;

    /// <summary>Draft changed externally (apply/revert/load): keep selection if it still exists.</summary>
    public void RefreshData()
    {
        if (!_main.WorkingDraft.Morphogens.Any(m => m.Id == _morphogenId))
            _morphogenId = -1;
        RebuildList(_morphogenId);
    }

    public void OpenMorphogen(int morphogenId)
    {
        _morphogenId = morphogenId;
        RebuildList(_morphogenId);
    }

    private void AddMorphogen()
    {
        var draft = _main.WorkingDraft;
        int id = ConfigDraft.UniqueMorphogenId(draft,
            draft.Morphogens.Length > 0 ? draft.Morphogens.Max(m => m.Id) + 1 : 1);
        draft.Morphogens = draft.Morphogens.Append(new MorphogenConfig
        {
            Id = id,
            Name = "morphogen",
            DisplayName = "Новый морфоген",
            Description = "Опишите назначение этого морфогена.",
            Diffusion = 0.1,
            Decay = 0.1,
            Color = "#4ea8de"
        }).ToArray();
        _morphogenId = id;
        _main.MarkDirty();
        _main.Notify($"Добавлен морфоген {id}");
        RebuildList(id);
    }

    private void CopyMorphogen()
    {
        var selected = Selected;
        if (selected == null) return;
        var draft = _main.WorkingDraft;
        var copy = ConfigDraft.CopyMorphogen(draft, selected);
        draft.Morphogens = draft.Morphogens.Append(copy).ToArray();
        _morphogenId = copy.Id;
        _main.MarkDirty();
        _main.Notify($"Создана копия «{Localization.MorphogenName(copy)}» (ид {copy.Id})");
        RebuildList(copy.Id);
    }

    private void DeleteMorphogen()
    {
        var selected = Selected;
        if (selected == null) return;
        var draft = _main.WorkingDraft;
        var refs = ConfigDraft.FindMorphogenReferences(draft, selected.Id);
        if (refs.Count > 0)
        {
            var dialog = new ConfirmationDialog
            {
                Title = "Удалить морфоген",
                DialogText = $"Морфоген «{Localization.MorphogenName(selected)}» (ид {selected.Id}) используют:\n" +
                             $"- {string.Join("\n- ", refs)}\n\n" +
                             "Удалить морфоген и очистить все ссылки на него?",
                OkButtonText = "Удалить",
                CancelButtonText = "Отмена"
            };
            dialog.Confirmed += () => { DoDelete(selected.Id); dialog.QueueFree(); };
            dialog.Canceled += () => dialog.QueueFree();
            dialog.CloseRequested += () => dialog.QueueFree();
            _main.AddChild(dialog);
            dialog.PopupCentered();
            return;
        }
        DoDelete(selected.Id);
    }

    private void DoDelete(int id)
    {
        var draft = _main.WorkingDraft;
        var affected = ConfigDraft.RemoveMorphogen(draft, id);
        _morphogenId = -1;
        _main.MarkDirty();
        _main.Notify(affected.Count > 0
            ? $"Удалён морфоген {id}; очищены ссылки из {string.Join(", ", affected)}"
            : $"Удалён морфоген {id}.");
        RebuildList();
    }

    private void RebuildList(int? selectId = null)
    {
        if (_listBox == null || _card == null) return;
        var draft = _main.WorkingDraft;
        int? prev = selectId ?? _morphogenId;
        if (prev is int p && !draft.Morphogens.Any(m => m.Id == p))
            prev = null;

        Ui.Clear(_listBox);
        _entries.Clear();

        if (draft.Morphogens.Length == 0)
        {
            _listBox.AddChild(Ui.Lbl("Морфогены не заданы.", 0, 0));
            _morphogenId = -1;
            RebuildCard();
            return;
        }

        var visible = draft.Morphogens
            .Where(m => MatchesFilter(m))
            .OrderBy(m => m.Id)
            .ToList();

        if (visible.Count == 0)
        {
            _listBox.AddChild(Ui.Lbl("Ничего не найдено.", 0, 0));
            _morphogenId = -1;
            RebuildCard();
            return;
        }

        if (prev is not int keep || !visible.Any(m => m.Id == keep))
            keep = visible[0].Id;
        _morphogenId = keep;

        foreach (var m in visible)
        {
            bool isSelected = m.Id == keep;
            var button = new Button
            {
                Text = $"{(isSelected ? "▸ " : "  ")}{m.Id}: {Localization.MorphogenName(m)}",
                ToggleMode = true,
                ButtonPressed = isSelected,
                Alignment = HorizontalAlignment.Left,
                TooltipText = $"ид {m.Id}\n{m.Description}"
            };
            button.SetPressedNoSignal(isSelected);
            button.Toggled += on =>
            {
                if (on && m.Id != _morphogenId)
                {
                    _morphogenId = m.Id;
                    RebuildList(_morphogenId);
                }
            };
            _entries.Add((button, m.Id));
            _listBox.AddChild(button);
        }
        RebuildCard();
    }

    private bool MatchesFilter(MorphogenConfig m)
    {
        if (_searchText.Length == 0) return true;
        return m.Id.ToString().Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || (m.Name ?? "").Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || (m.DisplayName ?? "").Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || (m.Description ?? "").Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateEntryText(int id)
    {
        var draft = _main.WorkingDraft;
        var m = draft.Morphogens.FirstOrDefault(x => x.Id == id);
        if (m == null) return;
        foreach (var (btn, entryId) in _entries)
        {
            if (entryId == id)
                btn.Text = $"{(id == _morphogenId ? "▸ " : "  ")}{id}: {Localization.MorphogenName(m)}";
        }
    }

    private void RebuildCard()
    {
        if (_card == null) return;
        Ui.Clear(_card);
        var m = Selected;
        if (m == null)
        {
            _card.AddChild(Ui.Title("Морфогены не заданы"));
            _card.AddChild(Ui.Lbl("Нажмите «Добавить морфоген», чтобы создать первый морфоген среды.", 0, 0));
            _card.AddChild(Ui.Lbl("Морфогены — вещества, порождающие химический градиент в среде.", 0, 0));
            return;
        }

        _card.AddChild(Ui.Title($"Морфоген «{Localization.MorphogenName(m)}» (ид {m.Id})"));

        _card.AddChild(Ui.Field("Название:", new LineEdit
        {
            Text = m.DisplayName,
            PlaceholderText = "русское название, напр. «Фактор деления»"
        }, 160).Also(row => ((LineEdit)row.GetChild(1)).TextChanged += t =>
        {
            m.DisplayName = t;
            _main.MarkDirty();
            UpdateEntryText(m.Id);
        }));

        _card.AddChild(Ui.Field("Техническое имя:", new LineEdit { Text = m.Name, PlaceholderText = "напр. morphogen_a" }, 160)
            .Also(row => ((LineEdit)row.GetChild(1)).TextChanged += t =>
            {
                m.Name = t;
                _main.MarkDirty();
                UpdateEntryText(m.Id);
            }));

        _card.AddChild(Ui.Field("Описание:", new TextEdit { Text = m.Description, CustomMinimumSize = new Vector2(0, 56) }, 160)
            .Also(row =>
            {
                var te = (TextEdit)row.GetChild(1);
                te.TextChanged += () => { m.Description = te.Text; _main.MarkDirty(); UpdateEntryText(m.Id); };
            }));

        _card.AddChild(Ui.Field("Диффузия, 1/с:",
            Ui.Sp(0, 500, 0.05, m.Diffusion, v => { m.Diffusion = v; _main.MarkDirty(); })));
        _card.AddChild(Ui.Field("Распад, 1/с:",
            Ui.Sp(0, 20, 0.05, m.Decay, v => { m.Decay = v; _main.MarkDirty(); })));

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
        _card.AddChild(Ui.Field("Цвет на карте:", color, 160));
        _card.AddChild(Ui.Lbl("Используется слоем «Концентрация» и стрелками слоя «Градиент».", 0, 0));
        _card.AddChild(Ui.Lbl("«Применить изменения» пересобирает мир с новыми параметрами среды.", 0, 0));
    }
}