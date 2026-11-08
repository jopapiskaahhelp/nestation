// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Shared._Nestation.Marines;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._Nestation.Marines;

public sealed class MarineMissionWindow : DefaultWindow
{
    private readonly Label _status = new();
    private readonly Button _start = new();

    public event Action? OnStart;

    public MarineMissionWindow()
    {
        Title = Loc.GetString("marine-mission-title");

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            Margin = new Thickness(10),
            MinSize = new Vector2(280, 100),
        };

        _start.Text = Loc.GetString("marine-mission-start");
        _start.OnPressed += _ => OnStart?.Invoke();

        root.AddChild(_status);
        root.AddChild(_start);
        ContentsContainer.AddChild(root);
    }

    public void Update(MarineMissionUiState state)
    {
        _start.Disabled = state.Started;
        _status.Text = state.Started
            ? Loc.GetString("marine-mission-status-started", ("spawned", state.Spawned), ("total", state.Total))
            : Loc.GetString("marine-mission-status-ready");
    }
}
