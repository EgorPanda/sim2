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
        var layout = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };

        var left = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(260, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
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
            CustomMinimumSize = new Vector2(260, 320),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        listScroll.AddChild(_listBox);
        left.AddChild(listScroll);

        var actionRow = new HBoxContainer();
        actionRow.AddChild(Ui.Btn("+ морфоген", AddMorphogen));
        actionRow.AddChild(Ui.Btn("копия", CopyMorphogen));
        actionRow.AddChild(Ui.Btn("удалить", DeleteMorphogen));
        left.AddChild(actionRow);
        left.AddChild(Ui.Lbl("Клик по морфогену — карточка справа."));
        layout.AddChild(left);

        var cardScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _card = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
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
            Name = "Новый морфоген",
            Role = "",
            Description = "Опишите назначение этого морфогена.",
            Type = "External",
            Diffusion = 0.1,
            Decay = 0.1,
            Color = "#4ea8de",
            DivisionInheritance = "Reset"
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

    private ConfirmationDialog? _pendingDeleteDialog;

    private void DeleteMorphogen()
    {
        var selected = Selected;
        if (selected == null || _pendingDeleteDialog != null) return;
        var draft = _main.WorkingDraft;
        int id = selected.Id;
        var refs = ConfigDraft.FindMorphogenReferences(draft, id);
        if (refs.Count > 0)
        {
            _pendingDeleteDialog = new ConfirmationDialog
            {
                Title = "Удалить морфоген",
                DialogText = $"Морфоген «{Localization.MorphogenName(selected)}» (ид {id}) используют:\n" +
                             $"- {string.Join("\n- ", refs)}\n\n" +
                             "Удалить морфоген и очистить все ссылки на него?",
                OkButtonText = "Удалить",
                CancelButtonText = "Отмена"
            };
            _pendingDeleteDialog.Confirmed += () => { DoDelete(id); CleanupDeleteDialog(); };
            _pendingDeleteDialog.Canceled += CleanupDeleteDialog;
            _pendingDeleteDialog.CloseRequested += CleanupDeleteDialog;
            _main.AddChild(_pendingDeleteDialog);
            _pendingDeleteDialog.PopupCentered();
            return;
        }
        DoDelete(id);
    }

    private void CleanupDeleteDialog()
    {
        if (_pendingDeleteDialog == null) return;
        _pendingDeleteDialog.QueueFree();
        _pendingDeleteDialog = null;
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
                Text = $"{(isSelected ? "▸ " : "  ")}{m.Id}: {Localization.MorphogenName(m)} [{Localization.MorphogenType(m.Type)}]",
                ToggleMode = true,
                ButtonPressed = isSelected,
                Alignment = HorizontalAlignment.Left,
                TooltipText = $"ид {m.Id}\nТип: {Localization.MorphogenType(m.Type)}\n{m.Description}"
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
            || (m.Role ?? "").Contains(_searchText, StringComparison.OrdinalIgnoreCase)
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
                btn.Text = $"{(id == _morphogenId ? "▸ " : "  ")}{id}: {Localization.MorphogenName(m)} [{Localization.MorphogenType(m.Type)}]";
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

        _card.AddChild(Ui.Field("Имя:", new LineEdit
        {
            Text = m.Name,
            PlaceholderText = "русское название, напр. «Фактор деления»"
        }, 160).Also(row => ((LineEdit)row.GetChild(1)).TextChanged += t =>
        {
            m.Name = t;
            _main.MarkDirty();
            UpdateEntryText(m.Id);
        }));

        _card.AddChild(Ui.Field("Роль:", new LineEdit { Text = m.Role, PlaceholderText = "напр. запускает развитие" }, 160)
            .Also(row => ((LineEdit)row.GetChild(1)).TextChanged += t =>
            {
                m.Role = t;
                _main.MarkDirty();
                UpdateEntryText(m.Id);
            }));

        _card.AddChild(Ui.Field("Описание:", new TextEdit { Text = m.Description, CustomMinimumSize = new Vector2(0, 56) }, 160)
            .Also(row =>
            {
                var te = (TextEdit)row.GetChild(1);
                te.TextChanged += () => { m.Description = te.Text; _main.MarkDirty(); UpdateEntryText(m.Id); };
            }));

        var typeRow = new HBoxContainer();
        typeRow.AddChild(Ui.Lbl("Тип:", 160));
        typeRow.AddChild(Ui.Opt(
            Localization.MorphogenTypes.Select((t, i) => (i, Localization.MorphogenType(t))),
            Localization.MorphogenTypes.ToList().FindIndex(t => string.Equals(t, m.Type, StringComparison.OrdinalIgnoreCase)),
            idx =>
            {
                string newType = Localization.MorphogenTypes[idx];
                bool changed = !string.Equals(m.Type, newType, StringComparison.OrdinalIgnoreCase);
                m.Type = newType;
                if (changed && m.IsInternal) m.Diffusion = 0.0;
                if (changed)
                {
                    // Initial signals must match the new type, otherwise the
                    // configuration stops applying.
                    ConfigDraft.ReconcileInitialSignals(_main.WorkingDraft, m.Id);
                }
                _main.MarkDirty();
                UpdateEntryText(m.Id);
                RebuildCard();
            }, 220));
        typeRow.AddChild(Ui.Btn("?", () => _main.OpenHelp("param.morphogenType")));
        _card.AddChild(typeRow);

        if (m.IsInternal)
        {
            _card.AddChild(Ui.Field("Распад, 1/с:",
                Ui.Sp(0, 20, 0.05, m.Decay, v => { m.Decay = v; _main.MarkDirty(); })));
            _card.AddChild(Ui.Field("Наследование при делении:",
                Ui.Opt(
                    new[] { "Reset", "Copy", "Split" }
                        .Select((mode, i) => (i, Localization.DivisionInheritance(mode))),
                    new[] { "Reset", "Copy", "Split" }
                        .ToList()
                        .FindIndex(mode => string.Equals(mode, m.DivisionInheritance, StringComparison.OrdinalIgnoreCase)),
                    idx =>
                    {
                        m.DivisionInheritance = new[] { "Reset", "Copy", "Split" }[idx];
                        _main.MarkDirty();
                    }, 360)));
            _card.AddChild(Ui.Lbl("Внутренний морфоген не диффундирует в среде. Его концентрация хранится в каждой клетке отдельно.", 0, 0));
        }
        else
        {
            _card.AddChild(Ui.Field("Диффузия, 1/с:",
                Ui.Sp(0, 500, 0.05, m.Diffusion, v => { m.Diffusion = v; _main.MarkDirty(); })));
            _card.AddChild(Ui.Field("Распад, 1/с:",
                Ui.Sp(0, 20, 0.05, m.Decay, v => { m.Decay = v; _main.MarkDirty(); })));
        }

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