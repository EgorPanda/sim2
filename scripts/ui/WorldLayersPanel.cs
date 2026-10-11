using Godot;
using SimulationCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sim2;

/// <summary>
/// "Мир" tab: switches the analytical display layer and its options. Writes
/// directly into <see cref="Main.World"/> and asks Main to refresh the legend
/// and overlay. Pure presentation — never touches core state.
/// </summary>
public partial class WorldLayersPanel : VBoxContainer
{
    private static readonly (WorldLayer Layer, string Text)[] Layers =
    {
        (WorldLayer.CellTypes, "Типы клеток"),
        (WorldLayer.Morphogen, "Концентрация морфогена"),
        (WorldLayer.MultiMorphogen, "Смесь морфогенов"),
        (WorldLayer.Gradient, "Градиент (стрелки)"),
        (WorldLayer.InternalMorphogen, "Внутриклеточная концентрация"),
        (WorldLayer.GeneActivity, "Активность гена"),
        (WorldLayer.GeneState, "Подавленные гены"),
        (WorldLayer.Neighbours, "Плотность соседей"),
    };

    private readonly Main _main;
    private VBoxContainer? _options;

    public WorldLayersPanel(Main main)
    {
        _main = main;
        Build();
    }

    private void Build()
    {
        AddChild(Ui.Title("Слой визуализации"));

        var group = new ButtonGroup();
        foreach (var (layer, text) in Layers)
        {
            var b = new Button
            {
                Text = text,
                ToggleMode = true,
                ButtonGroup = group,
                ButtonPressed = layer == _main.CurrentLayer,
                Alignment = HorizontalAlignment.Left
            };
            b.Pressed += () =>
            {
                _main.CurrentLayer = layer;
                RebuildOptions();
                _main.RefreshLayer();
            };
            AddChild(b);
        }

        _options = new VBoxContainer();
        AddChild(_options);
        RebuildOptions();
    }

    public void RefreshData()
    {
        RebuildOptions();
    }

    private OptionButton geneSelector(string selected)
    {
        var names = new List<string>();
        var live = _main.LiveSetup;
        var genomes = live != null ? live.Genomes : _main.WorkingDraft.Genomes;
        foreach (var genome in genomes)
        {
            foreach (var gene in genome.Genes)
                names.Add(gene.Key);
        }
        if (names.Count == 0) names.Add("");
        return Ui.OptStr(
            names.Select(n => (n, n)),
            selected,
            id =>
            {
                _main.World.GeneId = id;
                _main.RefreshLayer();
            }, 200);
    }

    private void RebuildOptions()
    {
        if (_options == null) return;
        Ui.Clear(_options);
        var world = _main.World;
        var layer = _main.CurrentLayer;

        switch (layer)
        {
            case WorldLayer.Morphogen:
            case WorldLayer.Gradient:
            {
                var live = _main.LiveSetup;
                var morphogens = (live != null ? live.Morphogens : _main.WorkingDraft.Morphogens)
                    .Where(m => m.IsExternal)
                    .OrderBy(m => m.Id)
                    .ToList();
                int currentMorphogen = world.MorphogenId;
                if (morphogens.Count == 0)
                    currentMorphogen = -1;
                else if (!morphogens.Any(m => m.Id == currentMorphogen))
                    currentMorphogen = morphogens[0].Id;

                var morphRow = new HBoxContainer();
                morphRow.AddChild(Ui.Lbl("Морфоген:", 100));
                morphRow.AddChild(Ui.Opt(
                    morphogens.Select(m => (m.Id, $"{m.Id}: {Localization.MorphogenName(m)}")),
                    currentMorphogen,
                    id =>
                    {
                        world.MorphogenId = id;
                        _main.ApplyMorphogenStyle(id);
                        _main.RefreshLayer();
                    }, 200));
                _options.AddChild(morphRow);
                break;
            }

            case WorldLayer.InternalMorphogen:
            {
                var liveInternal = _main.LiveSetup;
                var internalMorphogens = (liveInternal != null ? liveInternal.Morphogens : _main.WorkingDraft.Morphogens)
                    .Where(m => m.IsInternal)
                    .OrderBy(m => m.Id)
                    .ToList();
                int currentInternal = world.MorphogenId;
                if (internalMorphogens.Count == 0)
                    currentInternal = -1;
                else if (!internalMorphogens.Any(m => m.Id == currentInternal))
                    currentInternal = internalMorphogens[0].Id;

                var internalRow = new HBoxContainer();
                internalRow.AddChild(Ui.Lbl("Морфоген:", 100));
                internalRow.AddChild(Ui.Opt(
                    internalMorphogens.Select(m => (m.Id, $"{m.Id}: {Localization.MorphogenName(m)}")),
                    currentInternal,
                    id =>
                    {
                        world.MorphogenId = id;
                        _main.RefreshLayer();
                    }, 200));
                _options.AddChild(internalRow);
                break;
            }

            case WorldLayer.GeneActivity:
            case WorldLayer.GeneState:
                var geneRow = new HBoxContainer();
                geneRow.AddChild(Ui.Lbl("Ген:", 100));
                geneRow.AddChild(geneSelector(world.GeneId));
                _options.AddChild(geneRow);
                break;
        }

        switch (layer)
        {
            case WorldLayer.Morphogen:
                _options.AddChild(Ui.Chk("Подкрашивать клетки поверх поля", world.TintCellsOverField, on =>
                {
                    world.TintCellsOverField = on;
                    _main.RefreshLayer();
                }));
                _options.AddChild(Ui.Chk("Нормализовать к максимуму", world.NormalizeConcentration, on =>
                {
                    world.NormalizeConcentration = on;
                    _main.RefreshLayer();
                }));
                var minMax = new HBoxContainer();
                minMax.AddChild(Ui.Lbl("Абс. мин/макс:", 120));
                minMax.AddChild(Ui.Sp(-1e6, 1e6, 1, world.FixedMin, v => { world.FixedMin = v; _main.RefreshLayer(); }, 90));
                minMax.AddChild(Ui.Sp(-1e6, 1e6, 1, world.FixedMax, v => { world.FixedMax = v; _main.RefreshLayer(); }, 90));
                _options.AddChild(minMax);
                break;

            case WorldLayer.MultiMorphogen:
                _options.AddChild(Ui.Chk("Нормализовать каждый канал к своему максимуму", world.MultiMorphogenNormalize, on =>
                {
                    world.MultiMorphogenNormalize = on;
                    _main.RefreshLayer();
                }));
                var multiMinMax = new HBoxContainer();
                multiMinMax.AddChild(Ui.Lbl("Абс. макс (на канал):", 150));
                multiMinMax.AddChild(Ui.Sp(0, 1e6, 1, world.FixedMax, v => { world.FixedMax = v; _main.RefreshLayer(); }, 90));
                _options.AddChild(multiMinMax);
                break;

            case WorldLayer.Gradient:
                var density = new HBoxContainer();
                density.AddChild(Ui.Lbl("Шаг сетки стрелок:", 150));
                density.AddChild(Ui.Sp(1, 16, 1, world.GradientDensity, v =>
                {
                    world.GradientDensity = (int)v;
                    _main.World.QueueRedraw();
                }));
                _options.AddChild(density);

                var length = new HBoxContainer();
                length.AddChild(Ui.Lbl("Длина стрелки:", 150));
                length.AddChild(Ui.Sp(2, 60, 1, world.GradientLength, v =>
                {
                    world.GradientLength = v;
                    _main.World.QueueRedraw();
                }));
                _options.AddChild(length);

                var alpha = new HBoxContainer();
                alpha.AddChild(Ui.Lbl("Прозрачность:", 150));
                var alphaSlider = new HSlider
                {
                    MinValue = 0.1, MaxValue = 1.0, Step = 0.05, Value = world.GradientAlpha,
                    CustomMinimumSize = new Vector2(140, 0)
                };
                alphaSlider.ValueChanged += v =>
                {
                    world.GradientAlpha = (float)v;
                    _main.World.QueueRedraw();
                };
                alpha.AddChild(alphaSlider);
                _options.AddChild(alpha);
                break;
        }

        _options.AddChild(Ui.Chk("Показывать клетки", world.ShowCells, on =>
        {
            world.ShowCells = on;
            _main.World.QueueRedraw();
        }));
    }

    private List<string> allGeneOptions()
    {
        var result = new List<string>();
        foreach (var genome in _main.WorkingDraft.Genomes)
        {
            foreach (var gene in genome.Genes)
                result.Add(gene.Key);
        }
        if (result.Count == 0) result.Add("");
        return result;
    }
}