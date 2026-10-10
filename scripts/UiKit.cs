using Godot;
using System;
using System.Collections.Generic;

namespace Sim2;

/// <summary>
/// Small builders used across the interface so panels share the same look and
/// the layout code stays short. UI-only; no core knowledge here.
/// </summary>
public static class Ui
{
    public static StyleBoxFlat PanelStyle(float alpha = 0.86f, int radius = 8)
    {
        return new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.07f, 0.10f, alpha),
            BorderColor = new Color(0.45f, 0.5f, 0.6f, 0.25f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 8,
            ContentMarginBottom = 8
        };
    }

    public static PanelContainer Panel(Control content, float alpha = 0.86f)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", PanelStyle(alpha));
        panel.AddChild(content);
        return panel;
    }

    public static Label Lbl(string text, int minWidth = 0, int minHeight = 0, bool wrap = false)
    {
        var label = new Label { Text = text };
        if (minWidth > 0) label.CustomMinimumSize = new Vector2(minWidth, 0);
        if (minHeight > 0) label.CustomMinimumSize = new Vector2(label.CustomMinimumSize.X, minHeight);
        if (wrap) label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }

    public static Label Title(string text, int minWidth = 0)
    {
        var label = Lbl(text, minWidth);
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.8f));
        return label;
    }

    public static HBoxContainer Row(Control? left = null, Control? mid = null, Control? right = null)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        if (left != null) row.AddChild(left);
        if (mid != null) row.AddChild(mid);
        if (right != null) row.AddChild(right);
        return row;
    }

    public static Button Btn(string text, Action onPressed, int minWidth = 0)
    {
        var b = new Button { Text = text };
        if (minWidth > 0) b.CustomMinimumSize = new Vector2(minWidth, 0);
        b.Pressed += onPressed;
        return b;
    }

    public static Button Toggle(string text, bool initial, Action<bool> onToggled)
    {
        var b = new Button
        {
            Text = text,
            ToggleMode = true,
            ButtonPressed = initial
        };
        b.Toggled += on => onToggled(on);
        return b;
    }

    public static OptionButton Opt(
        IEnumerable<(int Id, string Text)> items,
        int selectedId,
        Action<int> onSelect,
        int minWidth = 0)
    {
        var opt = new OptionButton();
        if (minWidth > 0) opt.CustomMinimumSize = new Vector2(minWidth, 0);
        foreach (var (id, text) in items)
            opt.AddItem(text, id);
        Select(opt, selectedId);
        opt.ItemSelected += idx => onSelect((int)opt.GetItemId((int)idx));
        return opt;
    }

    public static void Select(OptionButton opt, int id)
    {
        for (int i = 0; i < opt.ItemCount; i++)
        {
            if ((int)opt.GetItemId(i) == id)
            {
                opt.Select(i);
                return;
            }
        }
    }

    public static SpinBox Sp(double min, double max, double step, double value, Action<double> onValue, int minWidth = 90)
    {
        var spin = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = Math.Clamp(value, min, max),
            CustomMinimumSize = new Vector2(minWidth, 0)
        };
        spin.ValueChanged += v => onValue(v);
        return spin;
    }

    public static CheckBox Chk(string text, bool initial, Action<bool> onToggled)
    {
        var c = new CheckBox { Text = text, ButtonPressed = initial };
        c.Toggled += on => onToggled(on);
        return c;
    }

    public static VBoxContainer Section(string title, params Control[] children)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddChild(Title(title));
        foreach (var child in children)
        {
            if (child != null) box.AddChild(child);
        }
        return box;
    }

    public static void Clear(Node node)
    {
        var children = node.GetChildren().ToList();
        foreach (Node child in children)
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    public static OptionButton OptStr(
        IEnumerable<(string Id, string Text)> items,
        string selectedId,
        Action<string> onSelect,
        int minWidth = 0)
    {
        var opt = new OptionButton();
        if (minWidth > 0) opt.CustomMinimumSize = new Vector2(minWidth, 0);
        foreach (var (id, text) in items)
            opt.AddItem(text);
        for (int i = 0; i < opt.ItemCount; i++)
        {
            if (opt.GetItemText(i) == selectedId) opt.Select(i);
        }
        opt.ItemSelected += idx => onSelect(opt.GetItemText((int)idx));
        return opt;
    }

    /// <summary>Single-line row wrapped in a fixed-width caption for form labels.</summary>
    public static HBoxContainer Field(string caption, Control control, int captionWidth = 110)
    {
        var row = new HBoxContainer();
        row.AddChild(Lbl(caption, captionWidth));
        row.AddChild(control);
        return row;
    }

    public static HBoxContainer Caption(string text, int width = 110)
    {
        var row = new HBoxContainer();
        row.AddChild(Lbl(text, width));
        return row;
    }

    public static HBoxContainer Spacer()
    {
        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        return new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    }
}