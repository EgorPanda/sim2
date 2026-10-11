using Godot;
using SimulationCore;
using System.Linq;

namespace Sim2;

/// <summary>
/// Compact popup for choosing which morphogens are visualised. Shows every
/// morphogen with its type, id, colour and Russian name; supports quick
/// presets and immediate application.
/// </summary>
public partial class MorphogenPicker : Window
{
    private readonly Main _main;
    private readonly HashSet<int> _selected;
    private VBoxContainer? _list;

    public MorphogenPicker(Main main)
    {
        _main = main;
        _selected = new HashSet<int>(main.World.SelectedMorphogenIds);

        Title = "Выбор морфогенов";
        Size = new Vector2I(340, 420);
        MinSize = new Vector2I(280, 240);
        InitialPosition = WindowInitialPosition.CenterPrimaryScreen;
        Unresizable = false;
        WrapControls = true;

        var root = new VBoxContainer
        {
            OffsetLeft = 8,
            OffsetTop = 8,
            OffsetRight = -8,
            OffsetBottom = -8
        };
        AddChild(root);

        root.AddChild(Ui.Lbl("Отметьте сигналы для отображения:", 300));

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 180)
        };
        _list = new VBoxContainer();
        scroll.AddChild(_list);
        root.AddChild(scroll);

        RebuildList();

        var presets = new HBoxContainer();
        presets.AddChild(Ui.Btn("Все внешние", () =>
        {
            foreach (var m in Morphogens().Where(m => m.IsExternal)) _selected.Add(m.Id);
            RebuildList();
            Apply();
        }, 110));
        presets.AddChild(Ui.Btn("Все внутренние", () =>
        {
            foreach (var m in Morphogens().Where(m => m.IsInternal)) _selected.Add(m.Id);
            RebuildList();
            Apply();
        }, 120));
        presets.AddChild(Ui.Btn("Ничего", () =>
        {
            _selected.Clear();
            RebuildList();
            Apply();
        }, 80));
        root.AddChild(presets);

        root.AddChild(Ui.Lbl("Внешние — поля концентрации. Внутренние — окраска клеток.", 300));

        var closeRow = new HBoxContainer();
        closeRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        closeRow.AddChild(Ui.Btn("Закрыть", () => QueueFree(), 90));
        root.AddChild(closeRow);

        CloseRequested += () => QueueFree();
    }

    private MorphogenConfig[] Morphogens()
    {
        var setup = _main.LiveSetup;
        return setup?.Morphogens.OrderBy(m => m.Id).ToArray() ?? Array.Empty<MorphogenConfig>();
    }

    private void RebuildList()
    {
        if (_list == null) return;
        Ui.Clear(_list);
        foreach (var m in Morphogens())
        {
            int id = m.Id;
            bool external = m.IsExternal;
            bool on = _selected.Contains(id);

            var row = new HBoxContainer();
            var color = new ColorRect
            {
                CustomMinimumSize = new Vector2(16, 16),
                Color = Color.FromHtml(m.Color)
            };
            row.AddChild(color);

            var chk = Ui.Chk($"[{id}] {(external ? "Внешний" : "Внутренний")}: {Localization.MorphogenName(m)}", on, selected =>
            {
                if (selected) _selected.Add(id);
                else _selected.Remove(id);
                Apply();
            });
            row.AddChild(chk);
            _list.AddChild(row);
        }

        if (Morphogens().Length == 0)
            _list.AddChild(Ui.Lbl("Нет морфогенов", 300));
    }

    private void Apply()
    {
        _main.World.SelectedMorphogenIds.Clear();
        _main.World.VisibleMorphogenIds.Clear();
        foreach (int id in _selected)
        {
            _main.World.SelectedMorphogenIds.Add(id);
            var cfg = _main.LiveSetup?.Morphogens.FirstOrDefault(m => m.Id == id);
            if (cfg?.IsExternal == true)
                _main.World.VisibleMorphogenIds.Add(id);
        }

        // Ensure the primary id used by single-morphogen layers is valid.
        var setup = _main.LiveSetup;
        if (setup != null)
        {
            int primary = _selected.FirstOrDefault(id => setup.Morphogens.Any(m => m.Id == id));
            if (primary > 0 || _selected.Any())
                _main.World.MorphogenId = primary;
        }

        _main.RefreshLayer();
    }
}
