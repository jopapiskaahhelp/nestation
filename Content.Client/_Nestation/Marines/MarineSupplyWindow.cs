// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Shared._Nestation.Marines;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._Nestation.Marines;

public sealed class MarineSupplyWindow : DefaultWindow
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    private static readonly string[] CategoryOrder = { "weapons", "ammo", "grenades", "armor", "medical", "utility" };

    private readonly Label _credits = new();
    private readonly Label _status = new();
    private readonly List<(int Price, Button Button)> _buttons = new();

    public event Action<string>? OnBuy;

    public MarineSupplyWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("marine-supply-title");
        MinSize = new Vector2(460, 480);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8),
        };

        root.AddChild(_credits);
        root.AddChild(_status);

        var list = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 3,
            HorizontalExpand = true,
        };

        var entries = new List<MarineSupplyPrototype>(_prototypes.EnumeratePrototypes<MarineSupplyPrototype>());

        foreach (var category in CategoryOrder)
        {
            var inCategory = entries.FindAll(e => e.Category == category);
            if (inCategory.Count == 0)
                continue;

            inCategory.Sort((a, b) => a.Price.CompareTo(b.Price));

            list.AddChild(new Label
            {
                Text = Loc.GetString("marine-supply-category-" + category),
                Margin = new Thickness(0, 8, 0, 2),
            });

            foreach (var entry in inCategory)
            {
                var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
                row.AddChild(new Label { Text = entry.Name, HorizontalExpand = true });
                row.AddChild(new Label { Text = entry.Price + " cr", MinSize = new Vector2(60, 0) });

                var button = new Button { Text = Loc.GetString("marine-supply-order") };
                var id = entry.ID;
                button.OnPressed += _ => OnBuy?.Invoke(id);
                row.AddChild(button);

                list.AddChild(row);
                _buttons.Add((entry.Price, button));
            }
        }

        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true };
        scroll.AddChild(list);
        root.AddChild(scroll);

        ContentsContainer.AddChild(root);
    }

    public void Update(MarineSupplyUiState state)
    {
        _credits.Text = Loc.GetString("marine-supply-credits", ("credits", state.Credits));

        _status.Text = state.Lift switch
        {
            MarineLiftState.Raised when state.Queued > 0 =>
                Loc.GetString("marine-supply-status-pending", ("seconds", state.SecondsLeft), ("count", state.Queued)),
            MarineLiftState.Raised => Loc.GetString("marine-supply-status-ready"),
            _ => Loc.GetString("marine-supply-status-moving"),
        };

        foreach (var (price, button) in _buttons)
        {
            button.Disabled = !state.CanOrder || state.Credits < price;
        }
    }
}
