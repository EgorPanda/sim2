using Godot;
using SimulationCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sim2;

/// <summary>
/// Visual genome editor: list of genes (create/copy/delete), per-gene card
/// (id, name, description, delay), logical regulator groups (AND within a group,
/// OR between groups, NOT per condition) and a list of effects. Every edit is
/// written straight into the working draft held by <see cref="Main"/>, which
/// validates on apply.
/// </summary>
public partial class GenomeEditor : VBoxContainer
{
    private readonly Main _main;
    private OptionButton? _genomeSelect;
    private OptionButton? _filterSelect;
    private VBoxContainer? _geneList;
    private ScrollContainer? _cardScroll;
    private VBoxContainer? _card;
    private string _selectedGene = "";
    private int _genomeIndex;
    private string _geneFilter = "all";

    private GenomeConfig? CurrentGenome
    {
        get
        {
            var draft = _main.WorkingDraft;
            if (draft.Genomes.Length == 0) return null;
            return draft.Genomes[Math.Clamp(_genomeIndex, 0, draft.Genomes.Length - 1)];
        }
    }

    private GeneConfig? SelectedGene => CurrentGenome?.Genes.FirstOrDefault(g => g.Key == _selectedGene);

    public GenomeEditor(Main main)
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
        _genomeSelect = new OptionButton();
        _genomeSelect.ItemSelected += idx =>
        {
            _genomeIndex = (int)idx;
            RebuildList();
        };
        left.AddChild(_genomeSelect);

        _filterSelect = new OptionButton();
        _filterSelect.AddItem("Все гены", 0);
        _filterSelect.AddItem("С обяз. триггером", 1);
        _filterSelect.AddItem("Без обяз. триггера", 2);
        _filterSelect.AddItem("Не настроены", 3);
        _filterSelect.ItemSelected += idx =>
        {
            _geneFilter = ((int)idx) switch
            {
                1 => "trigger",
                2 => "noTrigger",
                3 => "notConfigured",
                _ => "all"
            };
            RebuildList();
        };
        left.AddChild(_filterSelect);

        _geneList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var listScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(240, 320),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        listScroll.AddChild(_geneList);
        left.AddChild(listScroll);

        var actionRow = new HBoxContainer();
        actionRow.AddChild(Ui.Btn("+ ген", () => { AddGene(); }));
        actionRow.AddChild(Ui.Btn("копия", () => { CopyGene(); }));
        actionRow.AddChild(Ui.Btn("удалить", () => { DeleteGene(); }));
        left.AddChild(actionRow);
        left.AddChild(Ui.Lbl("Клик по гену — карточка справа."));
        layout.AddChild(left);

        _cardScroll = new ScrollContainer
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
        _cardScroll.AddChild(_card);
        layout.AddChild(_cardScroll);

        AddChild(layout);
    }

    public void RefreshData()
    {
        RebuildGenomeSelect();
        RebuildList();
    }

    public void OpenGene(string geneKey)
    {
        var draft = _main.WorkingDraft;
        for (int i = 0; i < draft.Genomes.Length; i++)
        {
            if (draft.Genomes[i].Genes.Any(g => g.Key == geneKey))
            {
                _genomeIndex = i;
                break;
            }
        }
        _selectedGene = geneKey;
        RebuildGenomeSelect();
        RebuildList();
    }

    internal string CurrentGeneKey => _selectedGene;

    internal GenomeConfig? CurrentGenomeInternal => CurrentGenome;

    internal bool GeneCardShows(string geneKey)
    {
        if (_card == null) return false;
        foreach (var child in _card.GetChildren())
        {
            if (child is Label label && label.Text.Contains("Ген «" + geneKey + "»", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private void RebuildGenomeSelect()
    {
        if (_genomeSelect == null) return;
        var draft = _main.WorkingDraft;
        int prev = _genomeIndex;
        _genomeSelect.Clear();
        for (int i = 0; i < draft.Genomes.Length; i++)
        {
            var genome = draft.Genomes[i];
            string name = string.IsNullOrWhiteSpace(genome.Name) ? $"геном {genome.Id}" : genome.Name;
            _genomeSelect.AddItem($"{genome.Id}: {name}");
        }
        if (prev >= 0 && prev < _genomeSelect.ItemCount)
        {
            _genomeIndex = prev;
            _genomeSelect.Select(prev);
        }
    }

    private void AddGene()
    {
        var genome = CurrentGenome;
        if (genome == null) return;
        var gene = ConfigDraft.NewGene(genome, _main.WorkingDraft.Morphogens);
        genome.Genes.Add(gene);
        _selectedGene = gene.Key;
        _main.MarkDirty();
        _main.Notify($"Добавлен ген «{gene.Key}»");
        RebuildList();
    }

    private void CopyGene()
    {
        var genome = CurrentGenome;
        var selected = SelectedGene;
        if (genome == null || selected == null) return;
        var copy = ConfigDraft.CopyGene(genome, selected);
        genome.Genes.Add(copy);
        _selectedGene = copy.Key;
        _main.MarkDirty();
        _main.Notify($"Создана копия «{copy.Key}»");
        RebuildList();
    }

    private void DeleteGene()
    {
        var genome = CurrentGenome;
        var selected = SelectedGene;
        if (genome == null || selected == null) return;

        var dependents = ConfigDraft.FindGeneReferences(_main.WorkingDraft, selected.Key);
        if (dependents.Count > 0)
        {
            _main.Notify($"Нельзя удалить: на «{selected.Key}» ссылаются {dependents.Count} гена.", error: true);
            return;
        }
        genome.Genes.Remove(selected);
        _selectedGene = genome.Genes.Count > 0 ? genome.Genes[0].Key : "";
        _main.MarkDirty();
        _main.Notify($"Удалён ген «{selected.Key}»");
        RebuildList();
    }

    private void RebuildList()
    {
        if (_geneList == null || _card == null) return;
        var genome = CurrentGenome;
        Ui.Clear(_geneList);

        if (genome == null)
        {
            Ui.Clear(_card);
            _card.AddChild(Ui.Lbl("Нет геномов — создайте геном в разделе «Типы клеток»."));
            return;
        }

        if (genome.Genes.Count == 0)
        {
            _geneList.AddChild(Ui.Lbl("(пусто)"));
            RebuildCard();
            return;
        }

        if (!genome.Genes.Any(g => g.Key == _selectedGene))
            _selectedGene = genome.Genes[0].Key;

        foreach (var gene in genome.Genes.Where(PassesFilter))
        {
            bool isSelected = gene.Key == _selectedGene;
            var button = new Button
            {
                Text = $"{(isSelected ? "▸ " : "  ")}{Localization.GeneName(gene)}",
                ToggleMode = true,
                ButtonPressed = isSelected,
                Alignment = HorizontalAlignment.Left,
                TooltipText = Brief(gene)
            };
            button.SetPressedNoSignal(isSelected);
            button.Toggled += on =>
            {
                if (on && gene.Key != _selectedGene)
                {
                    _selectedGene = gene.Key;
                    RebuildList();
                }
            };
            _geneList.AddChild(button);
        }
        RebuildCard();
    }

    private bool PassesFilter(GeneConfig gene)
    {
        return _geneFilter switch
        {
            "trigger" => gene.RequireMorphogenTrigger && gene.RequiredMorphogenId >= 0,
            "noTrigger" => !gene.RequireMorphogenTrigger,
            "notConfigured" => gene.RequireMorphogenTrigger && gene.RequiredMorphogenId < 0,
            _ => true
        };
    }

    private static string TriggerStatus(GeneConfig gene, SimulationSetup setup)
    {
        if (!gene.RequireMorphogenTrigger) return "обязательный триггер отключён";
        if (gene.RequiredMorphogenId < 0) return "❗ не настроен: морфоген не выбран";
        var m = setup.Morphogens.FirstOrDefault(x => x.Id == gene.RequiredMorphogenId);
        if (m == null) return $"❗ не настроен: морфоген {gene.RequiredMorphogenId} не найден";
        string type = Localization.MorphogenType(m.Type);
        return $"триггер: {type.ToLowerInvariant()} «{Localization.MorphogenName(m)}» ≥ {Num.Fmt(gene.RequiredMorphogenThreshold)}";
    }

    private static string Brief(GeneConfig gene)
    {
        var parts = new List<string>();
        foreach (var group in gene.EffectiveGroups())
        {
            if (group.Regulators.Count == 0) continue;
            string op = Localization.GroupOperator(group.Operator);
            parts.Add($"{string.Join(" | ", group.Regulators.Select(ConditionText))} ({op})");
        }
        string conditions = parts.Count > 0 ? string.Join(" || ", parts) : "условий нет";
        string effects = gene.Effects.Count > 0
            ? string.Join(", ", gene.Effects.Select(e => Localization.Effect(e.Kind)))
            : "нет";
        string trigger = gene.RequireMorphogenTrigger
            ? $"триггер: морфоген {gene.RequiredMorphogenId} ≥ {Num.Fmt(gene.RequiredMorphogenThreshold)}"
            : "без обязательного триггера";
        return $"{trigger}\n{conditions}\nЭффекты: {effects}";
    }

    private static string ConditionText(RegulatorConfig r)
    {
        string inv = r.Invert ? "НЕ " : "";
        switch (r.Kind)
        {
            case "Property":
                return $"{inv}«{Localization.Property(r.Property)}» {Localization.DescribeRange(r.Min, r.Max)}";
            case "Morphogen":
                return $"{inv}морфоген #{r.Morphogen} {Localization.DescribeRange(r.Min, r.Max)}";
            case "Phenotype":
                return inv + $"тип клетки #{r.TypeId}";
            case "State":
                return inv + $"состояние = {Localization.StateByValue(r.TypeId)}";
            case "Timer":
                if (!double.IsNegativeInfinity(r.Min) || !double.IsPositiveInfinity(r.Max))
                    return inv + $"таймер «{r.Timer}»: прошло {Localization.DescribeRange(r.Min, r.Max)} с";
                return inv + $"таймер «{r.Timer}» идёт";
            default:
                return inv + $"«{r.Kind}»";
        }
    }

    private void RebuildCard()
    {
        if (_card == null) return;
        Ui.Clear(_card);
        var gene = SelectedGene;
        if (gene == null)
        {
            _card.AddChild(Ui.Lbl("Выберите ген из списка."));
            return;
        }

        var genome = CurrentGenome!;
        _card.AddChild(Ui.Title($"Ген «{gene.Key}»"));

        const int w = 64;
        var idRow = Ui.Field("Ид:", new LineEdit
        {
            Text = gene.Id,
            PlaceholderText = "стабильный идентификатор (не менять после публикации)"
        }, w);
        ((LineEdit)idRow.GetChild(1)).TextChanged += t =>
        {
            if (gene.Key != t && !ConfigDraft.GeneIdExists(genome, t))
            {
                gene.Id = t;
                if (gene.Name == gene.Key) gene.Name = t;
                _main.MarkDirty();
            }
        };
        _card.AddChild(idRow);

        _card.AddChild(Ui.Field("Имя:", new LineEdit { Text = gene.Name, PlaceholderText = "короткое имя (напр. secretor)" }, w));
        ((LineEdit)((HBoxContainer)_card.GetChild(_card.GetChildCount() - 1)).GetChild(1)).TextChanged += t =>
        {
            gene.Name = t;
            _main.MarkDirty();
        };

        _card.AddChild(Ui.Field("Название:", new LineEdit { Text = gene.DisplayName, PlaceholderText = "русское название в интерфейсе" }, w));
        ((LineEdit)((HBoxContainer)_card.GetChild(_card.GetChildCount() - 1)).GetChild(1)).TextChanged += t =>
        {
            gene.DisplayName = t;
            _main.MarkDirty();
        };

        _card.AddChild(Ui.Field("Роль:", new LineEdit { Text = gene.Role, PlaceholderText = "напр. секреция сигнала деления" }, w));
        ((LineEdit)((HBoxContainer)_card.GetChild(_card.GetChildCount() - 1)).GetChild(1)).TextChanged += t =>
        {
            gene.Role = t;
            _main.MarkDirty();
        };

        var delay = Ui.Sp(0, 60, 0.05, gene.ActivationDelay, v => { gene.ActivationDelay = v; _main.MarkDirty(); });
        _card.AddChild(Ui.Field("Задержка, с:", delay, w));

        _card.AddChild(Ui.Field("Описание:", new TextEdit { Text = gene.Description, CustomMinimumSize = new Vector2(0, 64) }, w));
        ((TextEdit)((HBoxContainer)_card.GetChild(_card.GetChildCount() - 1)).GetChild(1)).TextChanged += () =>
        {
            gene.Description = ((TextEdit)((HBoxContainer)_card.GetChild(_card.GetChildCount() - 1)).GetChild(1)).Text;
            _main.MarkDirty();
        };

        BuildRequiredTriggerEditor(_card, gene);
        BuildGroupsEditor(_card, gene);
        BuildEffectsEditor(_card, gene);
    }

    private void BuildRequiredTriggerEditor(VBoxContainer into, GeneConfig gene)
    {
        var draft = _main.WorkingDraft;
        var header = new HBoxContainer();
        header.AddChild(Ui.Title("Обязательный сигнал активации"));
        header.AddChild(Ui.Btn("?", () => _main.OpenHelp("param.requiredTrigger")));
        into.AddChild(header);

        var status = Ui.Lbl(TriggerStatus(gene, draft), 0, 0);
        status.Name = "triggerStatus";
        into.AddChild(status);

        var chk = Ui.Chk("Включить обязательный морфогенный триггер", gene.RequireMorphogenTrigger, on =>
        {
            gene.RequireMorphogenTrigger = on;
            if (on && gene.RequiredMorphogenId < 0 && draft.Morphogens.Length > 0)
            {
                var first = draft.Morphogens.FirstOrDefault(m => m.IsExternal) ?? draft.Morphogens[0];
                gene.RequiredMorphogenId = first.Id;
                gene.RequiredMorphogenThreshold = 0.5;
            }
            _main.MarkDirty();
            RebuildCard();
        });
        into.AddChild(chk);

        if (!gene.RequireMorphogenTrigger)
        {
            into.AddChild(Ui.Lbl("Ген активируется только по условиям ниже (старый режим).", 0, 0));
            return;
        }

        var morphOptions = draft.Morphogens
            .Select(m => (m.Id, $"{m.Id}: {Localization.MorphogenName(m)} ({Localization.MorphogenType(m.Type).ToLowerInvariant()})"))
            .ToList();
        if (morphOptions.Count == 0)
        {
            into.AddChild(Ui.Lbl("Морфогены не заданы — создайте морфоген и выберите его.", 0, 0));
            return;
        }

        var morphRow = new HBoxContainer();
        morphRow.AddChild(Ui.Lbl("Морфоген:", 100));
        morphRow.AddChild(Ui.Opt(
            morphOptions,
            gene.RequiredMorphogenId,
            id =>
            {
                gene.RequiredMorphogenId = id;
                _main.MarkDirty();
                RebuildCard();
            }, 240));
        morphRow.AddChild(Ui.Btn("→", () => _main.OpenMorphogen(gene.RequiredMorphogenId), 40));
        into.AddChild(morphRow);

        var thresholdRow = new HBoxContainer();
        thresholdRow.AddChild(Ui.Lbl("Порог концентрации:", 140));
        thresholdRow.AddChild(Ui.Sp(0, 1e6, 0.01, gene.RequiredMorphogenThreshold, v =>
        {
            gene.RequiredMorphogenThreshold = v;
            _main.MarkDirty();
            RebuildCard();
        }, 120));
        into.AddChild(thresholdRow);

        var m = draft.Morphogens.FirstOrDefault(x => x.Id == gene.RequiredMorphogenId);
        if (m != null)
        {
            string sentence = gene.RequiredMorphogenId < 0
                ? "❗ Этот ген неактивен: морфоген не выбран."
                : $"Этот ген активируется, когда концентрация морфогена «{Localization.MorphogenName(m)}» достигает {Num.Fmt(gene.RequiredMorphogenThreshold)}, при условии выполнения остальных правил.";
            into.AddChild(Ui.Lbl(sentence, 0, 0));
        }
    }

    // --- Groups ------------------------------------------------------------

    public static List<RegulatorGroupConfig> EffectiveGroupsForEdit(GeneConfig gene)
    {
        if (gene.RegulatorGroups != null) return gene.RegulatorGroups;
        gene.RegulatorGroups = new List<RegulatorGroupConfig>();
        if (gene.Regulators.Count > 0)
            gene.RegulatorGroups.Add(new RegulatorGroupConfig { Operator = "And", Regulators = gene.Regulators });
        return gene.RegulatorGroups;
    }

    private void BuildGroupsEditor(VBoxContainer into, GeneConfig gene)
    {
        var groups = EffectiveGroupsForEdit(gene);
        var header = new HBoxContainer();
        header.AddChild(Ui.Title("Условия (И внутри группы, ИЛИ между группами)"));
        header.AddChild(Ui.Btn("?", () => _main.OpenHelp("logic.and")));
        into.AddChild(header);

        if (groups.Count == 0)
            into.AddChild(Ui.Lbl("Условий нет — ген активен всегда."));

        for (int gi = 0; gi < groups.Count; gi++)
        {
            var group = groups[gi];
            var crate = new PanelContainer();
            crate.AddThemeStyleboxOverride("panel", Ui.PanelStyle(0.5f, 4));
            var cbox = new VBoxContainer();
            crate.AddChild(cbox);

            var top = new HBoxContainer();
            top.AddChild(Ui.Lbl($"Группа {gi + 1} ({group.Regulators.Count} усл.)"));
            top.AddChild(Ui.Opt(
                new[] { (0, "И (все условия)"), (1, "ИЛИ (любое условие)") },
                group.Operator == "Or" ? 1 : 0,
                s => { group.Operator = s == 1 ? "Or" : "And"; _main.MarkDirty(); }, 150));
            top.AddChild(Ui.Btn("удалить", () =>
            {
                if (groups.Remove(group) && groups.Count == 0)
                    gene.RegulatorGroups = new List<RegulatorGroupConfig>();
                _main.MarkDirty();
                RebuildCard();
            }, 80));
            cbox.AddChild(top);

            foreach (var reg in group.Regulators)
            {
                var rrow = new HBoxContainer();
                rrow.AddChild(Ui.Btn("✎", () => OpenCondition(gene, group, reg), 34));
                rrow.AddChild(Ui.Lbl(ConditionText(reg)));
                rrow.AddChild(Ui.Btn("×", () =>
                {
                    group.Regulators.Remove(reg);
                    _main.MarkDirty();
                    RebuildCard();
                }, 30));
                cbox.AddChild(rrow);
            }

            cbox.AddChild(Ui.Btn("Добавить условие", () =>
                OpenCondition(gene, group, new RegulatorConfig { Kind = "Property", Property = "age" })));
            into.AddChild(crate);
        }

        into.AddChild(Ui.Btn("Добавить группу", () =>
        {
            groups.Add(new RegulatorGroupConfig { Operator = "And", Regulators = new List<RegulatorConfig>() });
            _main.MarkDirty();
            RebuildCard();
        }));
    }

    private void OpenCondition(GeneConfig gene, RegulatorGroupConfig group, RegulatorConfig reg)
    {
        bool isNew = !group.Regulators.Contains(reg);
        var dialog = new AcceptDialog
        {
            Title = "Условие активации",
            OkButtonText = "Готово",
            Size = new Vector2I(540, 340)
        };
        dialog.Confirmed += () => dialog.QueueFree();
        dialog.Canceled += () => dialog.QueueFree();
        dialog.CloseRequested += () => dialog.QueueFree();
        dialog.GetOkButton().Pressed += () => dialog.QueueFree();

        if (isNew)
        {
            void Commit()
            {
                if (!group.Regulators.Contains(reg)) group.Regulators.Add(reg);
                _main.MarkDirty();
                RebuildCard();
            }
            dialog.Confirmed += Commit;
            dialog.GetOkButton().Pressed += Commit;
        }

        var content = new VBoxContainer();
        dialog.AddChild(content);
        BuildConditionEditor(content, gene, group, reg);
        _main.AddChild(dialog);
        dialog.PopupCentered(new Vector2I(560, 380));
    }

    private void BuildConditionEditor(VBoxContainer into, GeneConfig gene, RegulatorGroupConfig group, RegulatorConfig reg)
    {
        var draft = _main.WorkingDraft;

        void Rebind()
        {
            Ui.Clear(into);
            BuildConditionEditor(into, gene, group, reg);
        }

        into.AddChild(Ui.Field("Тип условия:",
            Ui.Opt(
                Localization.RegulatorKinds.Select((k, i) => (i, Localization.Regulator(k))),
                Math.Max(0, IndexOf(Localization.RegulatorKinds, reg.Kind)),
                s =>
                {
                    reg.Kind = Localization.RegulatorKinds[s];
                    reg.Property = "";
                    reg.Timer = "";
                    reg.Invert = false;
                    _main.MarkDirty();
                    Rebind();
                }, 240)));

        switch (reg.Kind)
        {
            case "Property":
                into.AddChild(Ui.Field("Свойство:",
                    Ui.Opt(
                        Localization.PropertyNames.Select((p, i) => (i, Localization.Property(p))),
                        Math.Max(0, IndexOf(Localization.PropertyNames, reg.Property)),
                        s => { reg.Property = Localization.PropertyNames[s]; _main.MarkDirty(); }, 200)));
                BuildRangeRow(into, "значение", reg, Rebind);
                break;

            case "Morphogen":
                into.AddChild(Ui.Field("Морфоген:",
                    Ui.Opt(
                        draft.Morphogens.Select(m => (m.Id, $"{m.Id}: {Localization.MorphogenName(m)}")),
                        reg.Morphogen >= 0 ? reg.Morphogen : (draft.Morphogens.Length > 0 ? draft.Morphogens[0].Id : 0),
                        id => { reg.Morphogen = id; _main.MarkDirty(); }, 220)));
                BuildRangeRow(into, "концентрация", reg, Rebind);
                break;

            case "Phenotype":
                into.AddChild(Ui.Field("Тип клетки:",
                    Ui.Opt(
                        draft.CellTypes.Select(t => (t.Id, $"{t.Id}: {Localization.CellTypeName(t)}")),
                        reg.TypeId,
                        id => { reg.TypeId = id; _main.MarkDirty(); }, 220)));
                break;

            case "State":
                into.AddChild(Ui.Field("Состояние:",
                    Ui.Opt(
                        new[]
                        {
                            (0, Localization.StateByValue(0)),
                            (1, Localization.StateByValue(1)),
                            (2, Localization.StateByValue(2))
                        },
                        reg.TypeId,
                        id => { reg.TypeId = id; _main.MarkDirty(); }, 160)));
                break;

            case "Timer":
            {
                var timerEdit = new LineEdit { Text = reg.Timer, PlaceholderText = "имя таймера" };
                timerEdit.TextChanged += t => { reg.Timer = t; _main.MarkDirty(); };
                into.AddChild(Ui.Field("Таймер:", timerEdit, 110));

                bool timed = !double.IsNegativeInfinity(reg.Min) || !double.IsPositiveInfinity(reg.Max);
                into.AddChild(Ui.Chk("Условие по времени от запуска (сек)", timed, on =>
                {
                    reg.Min = on ? 1.0 : double.NegativeInfinity;
                    reg.Max = double.PositiveInfinity;
                    _main.MarkDirty();
                    Rebind();
                }));

                if (timed)
                {
                    into.AddChild(Ui.Field("Не раньше, с:",
                        Ui.Sp(0, 1e6, 0.1, reg.Min, v => { reg.Min = v; _main.MarkDirty(); })));

                    bool hasUpper = !double.IsPositiveInfinity(reg.Max);
                    var upperChk = Ui.Chk("", hasUpper, on =>
                    {
                        reg.Max = on ? Math.Max(reg.Min, 1.0) : double.PositiveInfinity;
                        _main.MarkDirty();
                        Rebind();
                    });
                    var upperSpin = Ui.Sp(0, 1e6, 0.1, hasUpper ? reg.Max : 0.0, v => { reg.Max = v; _main.MarkDirty(); });
                    upperSpin.Editable = hasUpper;
                    upperSpin.GetLineEdit().PlaceholderText = "не ограничено";
                    into.AddChild(Ui.Field("Не позже, с:", new HBoxContainer().Also(h =>
                    {
                        h.AddChild(upperChk);
                        h.AddChild(upperSpin);
                    }), 160));

                    into.AddChild(Ui.Lbl("Условие истинно, пока таймер идёт и с момента запуска прошло не меньше «Не раньше» (и не больше «Не позже», если задано).", 0, 0));
                }
                else
                {
                    into.AddChild(Ui.Lbl("Без ограничения по времени условие истинно, пока таймер идёт (осталось время > 0).", 0, 0));
                }
                break;
            }
        }

        into.AddChild(Ui.Chk("Отрицание (НЕ)", reg.Invert, on => { reg.Invert = on; _main.MarkDirty(); }));

        if (isNewCondition(group, reg))
            into.AddChild(Ui.Lbl("Новое условие будет добавлено в группу при нажатии «Готово».", 0, 0));
    }

    private static bool isNewCondition(RegulatorGroupConfig group, RegulatorConfig reg)
        => !group.Regulators.Contains(reg);

    private static int IndexOf(string[] values, string key)
    {
        int idx = Array.FindIndex(values, v => string.Equals(v, key, StringComparison.OrdinalIgnoreCase));
        return Math.Max(0, idx);
    }

    private void BuildRangeRow(VBoxContainer into, string what, RegulatorConfig reg, Action rebind)
    {
        bool hasLow = !double.IsNegativeInfinity(reg.Min);
        bool hasHigh = !double.IsPositiveInfinity(reg.Max);

        void SetLow(bool on)
        {
            reg.Min = on ? 0.0 : double.NegativeInfinity;
            _main.MarkDirty();
            rebind();
        }
        void SetHigh(bool on)
        {
            reg.Max = on ? 1.0 : double.PositiveInfinity;
            _main.MarkDirty();
            rebind();
        }

        var lowCheck = Ui.Chk("", hasLow, SetLow);
        var lowSpin = Ui.Sp(0, 1e6, 0.01, hasLow ? reg.Min : 0.0, v => { reg.Min = v; _main.MarkDirty(); });
        lowSpin.Editable = hasLow;
        lowSpin.GetLineEdit().PlaceholderText = "не ограничено";
        into.AddChild(Ui.Field($"Нижний предел для {what}:", new HBoxContainer { Name = "low" }
            .Also(h => { h.AddChild(lowCheck); h.AddChild(lowSpin); }), 200));

        var highCheck = Ui.Chk("", hasHigh, SetHigh);
        var highSpin = Ui.Sp(0, 1e6, 0.01, hasHigh ? reg.Max : 0.0, v => { reg.Max = v; _main.MarkDirty(); });
        highSpin.Editable = hasHigh;
        highSpin.GetLineEdit().PlaceholderText = "не ограничено";
        into.AddChild(Ui.Field("Верхний предел:", new HBoxContainer { Name = "high" }
            .Also(h => { h.AddChild(highCheck); h.AddChild(highSpin); }), 200));

        if (!hasLow && !hasHigh)
            into.AddChild(Ui.Lbl($"Допустимо любое значение {what}.", 0, 0));
    }

    // --- Effects -----------------------------------------------------------

    private void BuildEffectsEditor(VBoxContainer into, GeneConfig gene)
    {
        into.AddChild(Ui.Title($"Эффекты ({gene.Effects.Count})"));
        var crate = new PanelContainer();
        crate.AddThemeStyleboxOverride("panel", Ui.PanelStyle(0.5f, 4));
        var cbox = new VBoxContainer();
        crate.AddChild(cbox);

        for (int i = 0; i < gene.Effects.Count; i++)
        {
            var effect = gene.Effects[i];
            var row = new HBoxContainer();
            row.AddChild(Ui.Btn("✎", () => EditEffect(gene, effect), 34));
            row.AddChild(Ui.Lbl(EffectLabel(effect)));
            row.AddChild(Ui.Btn("×", () =>
            {
                gene.Effects.Remove(effect);
                _main.MarkDirty();
                RebuildCard();
            }, 30));
            cbox.AddChild(row);
        }

        if (gene.Effects.Count == 0)
            cbox.AddChild(Ui.Lbl("Эффектов нет — ген ничего не делает."));

        into.AddChild(crate);
        into.AddChild(Ui.Btn("Добавить эффект", () => EditNewEffect(gene, "EmitMorphogen")));
    }

    /// <summary>Creates a new effect with valid defaults so it can be saved as-is.</summary>
    private void EditNewEffect(GeneConfig gene, string kind)
    {
        var effect = new GeneEffectConfig { Kind = kind };
        var draft = _main.WorkingDraft;
        switch (kind)
        {
            case "EmitMorphogen":
                effect.Morphogen = draft.Morphogens.Length > 0 ? draft.Morphogens[0].Id : -1;
                break;
            case "SetCellType":
                effect.TypeId = draft.CellTypes.Length > 0 ? draft.CellTypes[0].Id : -1;
                break;
            case "Divide":
                effect.Rate = 1.0;
                break;
            case "Move":
                effect.Morphogen = draft.Morphogens.FirstOrDefault(m => m.IsExternal)?.Id
                    ?? (draft.Morphogens.Length > 0 ? draft.Morphogens[0].Id : -1);
                effect.Rate = 10.0;
                break;
        }
        EditEffect(gene, effect);
    }

    private static string EffectLabel(GeneEffectConfig e)
    {
        string baseText = Localization.Effect(e.Kind);
        return e.Kind switch
        {
            "EmitMorphogen" => $"{baseText}: морфоген #{e.Morphogen}, {Num.Fmt(e.Rate)}/с",
            "Grow" => $"{baseText}: {Num.Fmt(e.Rate)} ед./с",
            "Divide" => e.ChildTypeId is int cid
                ? $"{baseText}: {Num.Fmt(e.Rate)} ед./с, потомок → тип #{cid}"
                : $"{baseText}: {Num.Fmt(e.Rate)} ед./с, потомок → как родитель",
            "SetCellType" => $"{baseText}: тип #{e.TypeId}",
            "StartTimer" => $"{baseText}: «{e.Timer}» {Num.Fmt(e.Duration)} с",
            "SilenceGene" => $"{baseText}: «{e.Gene}»",
            "Apoptosis" => $"{baseText}: P={Num.Fmt(e.Probability)}/с",
            "Move" => $"{baseText}: морфоген #{e.Morphogen}{(e.Repel ? " (отталкивание)" : "")}, {Num.Fmt(e.Rate)} ед./с",
            _ => baseText
        };
    }

    private void EditEffect(GeneConfig gene, GeneEffectConfig effect)
    {
        bool isNew = !gene.Effects.Contains(effect);
        var dialog = new AcceptDialog
        {
            Title = "Эффект гена",
            OkButtonText = "Готово",
            Size = new Vector2I(540, 280)
        };
        dialog.Confirmed += () => dialog.QueueFree();
        dialog.Canceled += () => dialog.QueueFree();
        dialog.CloseRequested += () => dialog.QueueFree();
        dialog.GetOkButton().Pressed += () => dialog.QueueFree();

        if (isNew)
        {
            void Commit()
            {
                if (!gene.Effects.Contains(effect)) gene.Effects.Add(effect);
                _main.MarkDirty();
                RebuildCard();
            }
            dialog.Confirmed += Commit;
            dialog.GetOkButton().Pressed += Commit;
        }

        var content = new VBoxContainer();
        dialog.AddChild(content);
        BuildEffectEditor(content, gene, effect);
        _main.AddChild(dialog);
        dialog.PopupCentered(new Vector2I(560, 320));
    }

    private void BuildEffectEditor(VBoxContainer into, GeneConfig gene, GeneEffectConfig effect)
    {
        var draft = _main.WorkingDraft;

        void Rebind()
        {
            Ui.Clear(into);
            BuildEffectEditor(into, gene, effect);
        }

        into.AddChild(Ui.Field("Тип эффекта:",
            Ui.Opt(
                Localization.EffectKinds.Select((k, i) => (i, Localization.Effect(k))),
                Math.Max(0, IndexOf(Localization.EffectKinds, effect.Kind)),
                s => { effect.Kind = Localization.EffectKinds[s]; _main.MarkDirty(); Rebind(); }, 240)));
        into.AddChild(Ui.Lbl(Localization.EffectExplanation(effect.Kind), 0, 0));

        switch (effect.Kind)
        {
            case "EmitMorphogen":
                into.AddChild(Ui.Field("Морфоген:",
                    Ui.Opt(
                        draft.Morphogens.Select(m => (m.Id, $"{m.Id}: {Localization.MorphogenName(m)}")),
                        effect.Morphogen >= 0 ? effect.Morphogen : (draft.Morphogens.Length > 0 ? draft.Morphogens[0].Id : 0),
                        id => { effect.Morphogen = id; _main.MarkDirty(); }, 220)));
                into.AddChild(Ui.Field("Скорость, ед./с:",
                    Ui.Sp(0, 1000, 0.1, effect.Rate, v => { effect.Rate = v; _main.MarkDirty(); })));
                break;

            case "Grow":
                into.AddChild(Ui.Field("Скорость, ед./с:",
                    Ui.Sp(0, 1000, 0.05, effect.Rate, v => { effect.Rate = v; _main.MarkDirty(); })));
                break;

            case "Divide":
                into.AddChild(Ui.Field("Скорость, ед./с:",
                    Ui.Sp(0, 1000, 0.05, effect.Rate, v => { effect.Rate = v; _main.MarkDirty(); })));
                into.AddChild(Ui.Field("Фенотип дочерней клетки:",
                    Ui.Opt(
                        new[] { (-1, "Как у родителя (по умолчанию)") }
                            .Concat(draft.CellTypes.Select(t => ((int)t.Id, $"{t.Id}: {Localization.CellTypeName(t)}"))),
                        effect.ChildTypeId ?? -1,
                        id => { effect.ChildTypeId = id < 0 ? null : id; _main.MarkDirty(); }, 260)));
                into.AddChild(Ui.Lbl("Дочерняя клетка создаётся с этим фенотипом; родитель сохраняет свой.", 0, 0));
                break;

            case "SetCellType":
                into.AddChild(Ui.Field("Новый тип клетки:",
                    Ui.Opt(
                        draft.CellTypes.Select(t => (t.Id, $"{t.Id}: {Localization.CellTypeName(t)}")),
                        effect.TypeId >= 0 ? effect.TypeId : (draft.CellTypes.Length > 0 ? draft.CellTypes[0].Id : 0),
                        id => { effect.TypeId = id; _main.MarkDirty(); }, 220)));
                break;

            case "StartTimer":
                var tEdit = new LineEdit { Text = effect.Timer, PlaceholderText = "например: развитие" };
                tEdit.TextChanged += t => { effect.Timer = t; _main.MarkDirty(); };
                into.AddChild(Ui.Field("Имя таймера:", tEdit));
                into.AddChild(Ui.Field("Длительность, с:",
                    Ui.Sp(0, 9999, 0.1, effect.Duration, v => { effect.Duration = v; _main.MarkDirty(); })));
                break;

            case "SilenceGene":
                var targets = (CurrentGenome ?? new GenomeConfig()).Genes
                    .Where(g => g.Key != gene.Key)
                    .Select(g => (g.Key, Localization.GeneName(g)))
                    .ToList();
                if (targets.Count == 0) targets.Add(("", "(нечего подавлять)"));
                into.AddChild(Ui.Field("Подавляемый ген:",
                    Ui.OptStr(targets, effect.Gene, s => { effect.Gene = s; _main.MarkDirty(); }, 220)));
                into.AddChild(Ui.Lbl("Ген навсегда отключается в клетке, где активен этот эффект.", 0, 0));
                break;

            case "Apoptosis":
                into.AddChild(Ui.Field("Вероятность/с (0 = сразу):",
                    Ui.Sp(0, 1000, 0.01, effect.Probability, v => { effect.Probability = v; _main.MarkDirty(); })));
                break;

            case "BlockDivision":
                into.AddChild(Ui.Lbl("Пока ген активен, клетка не делится. Дополнительных параметров нет.", 0, 0));
                break;

            case "Move":
            {
                var external = draft.Morphogens.Where(m => m.IsExternal).ToList();
                into.AddChild(Ui.Field("Морфоген-направитель:",
                    Ui.Opt(
                        external.Select(m => (m.Id, $"{m.Id}: {Localization.MorphogenName(m)}")),
                        external.Any(m => m.Id == effect.Morphogen) ? effect.Morphogen : (external.Count > 0 ? external[0].Id : 0),
                        id => { effect.Morphogen = id; _main.MarkDirty(); }, 220)));
                into.AddChild(Ui.Field("Скорость, ед./с:",
                    Ui.Sp(0, 1000, 0.1, effect.Rate, v => { effect.Rate = v; _main.MarkDirty(); })));
                into.AddChild(Ui.Chk("Отталкиваться (двигаться против градиента)", effect.Repel, on =>
                {
                    effect.Repel = on;
                    _main.MarkDirty();
                }));
                into.AddChild(Ui.Lbl("Клетка смещается вдоль градиента выбранного внешнего морфогена каждый шаг, пока ген активен.", 0, 0));
                break;
            }
        }
    }

    internal static IEnumerable<(string Id, string Text)> genomeGeneOptions(GeneConfig gene)
        => gene.EffectiveGroups().SelectMany(g => g.Regulators)
            .Select(r => r.Timer)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct()
            .Select(t => (t, t))
            .Concat(gene.Effects.Select(e => e.Gene).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().Select(t => (t, t)))
            .Distinct()
            .Append(("", "(выберите ген)"));
}

internal static class NodeExt
{
    public static T Also<T>(this T node, Action<T> setup) where T : Godot.Node
    {
        setup(node);
        return node;
    }
}