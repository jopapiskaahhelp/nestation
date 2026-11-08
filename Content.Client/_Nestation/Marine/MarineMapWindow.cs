// Nestation new city CC BY-NC 3.0
using System.Numerics;
using Content.Shared._NeStation.Marine;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.Client._NeStation.Marine;

public sealed class MarineMapWindow : DefaultWindow
{
    public MarineMapControl Map { get; }

    private readonly BoxContainer _toolbar;
    private readonly Label _hint;
    private readonly Label _sizeLabel;

    public MarineMapWindow()
    {
        Title = Loc.GetString("marine-map-window-title");
        Map = new MarineMapControl();

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };

        _toolbar = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            Visible = false,
        };

        _hint = new Label
        {
            Text = Loc.GetString("marine-map-no-drawing"),
            Modulate = Color.Gray,
        };

        var group = new ButtonGroup();
        var names = new[]
        {
            "marine-map-color-black", "marine-map-color-red",
            "marine-map-color-purple", "marine-map-color-white",
        };

        for (var i = 0; i < names.Length; i++)
        {
            var color = (byte) i;
            var button = new Button
            {
                Text = Loc.GetString(names[i]),
                ToggleMode = true,
                Group = group,
                Pressed = i == 0,
                ModulateSelfOverride = MarineMapPalette.Colors[i],
            };
            button.OnPressed += _ =>
            {
                Map.Tool = MarineMapTool.Pen;
                Map.PenColor = color;
                UpdateSizeLabel();
            };
            _toolbar.AddChild(button);
        }

        var eraser = new Button
        {
            Text = Loc.GetString("marine-map-eraser"),
            ToggleMode = true,
            Group = group,
        };
        eraser.OnPressed += _ =>
        {
            Map.Tool = MarineMapTool.Eraser;
            UpdateSizeLabel();
        };
        _toolbar.AddChild(eraser);

        var minus = new Button { Text = "−" };
        var plus = new Button { Text = "+" };
        _sizeLabel = new Label { MinWidth = 90 };
        minus.OnPressed += _ => ChangeSize(-1);
        plus.OnPressed += _ => ChangeSize(1);

        _toolbar.AddChild(new Control { MinWidth = 10 });
        _toolbar.AddChild(minus);
        _toolbar.AddChild(_sizeLabel);
        _toolbar.AddChild(plus);

        root.AddChild(_toolbar);
        root.AddChild(_hint);
        root.AddChild(Map);
        Contents.AddChild(root);

        UpdateSizeLabel();
    }

    public void SetCanDraw(bool canDraw)
    {
        Map.CanDraw = canDraw;
        _toolbar.Visible = canDraw;
        _hint.Visible = !canDraw;
    }

    private void ChangeSize(int direction)
    {
        if (Map.Tool == MarineMapTool.Eraser)
        {
            Map.EraserSize = Math.Clamp(Map.EraserSize + direction,
                MarineMapPalette.MinEraserSize, MarineMapPalette.MaxEraserSize);
        }
        else
        {
            Map.PenSize = Math.Clamp(Map.PenSize + direction * 0.5f,
                MarineMapPalette.MinPenSize, MarineMapPalette.MaxPenSize);
        }

        UpdateSizeLabel();
    }

    private void UpdateSizeLabel()
    {
        var size = Map.Tool == MarineMapTool.Eraser ? Map.EraserSize : Map.PenSize;
        _sizeLabel.Text = Loc.GetString("marine-map-size", ("size", size.ToString("0.#")));
    }
}
