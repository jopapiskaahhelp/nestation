// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Shared._Nestation.SupportStrike;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Timing;

namespace Content.Client._Nestation.SupportStrike;

public sealed class SupportTabletWindow : DefaultWindow
{
    private readonly IGameTiming _timing = IoCManager.Resolve<IGameTiming>();
    private readonly Label _status = new();
    private readonly Label _signal = new();
    private readonly Button _launch = new() { Text = Loc.GetString("support-tablet-launch") };

    private TimeSpan _nextUse;
    private bool _hasTarget;

    public event Action? OnLaunch;

    public SupportTabletWindow()
    {
        Title = Loc.GetString("support-tablet-title");

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            Margin = new Thickness(10),
            MinSize = new Vector2(320, 120)
        };

        root.AddChild(_status);
        root.AddChild(_signal);
        root.AddChild(_launch);
        ContentsContainer.AddChild(root);

        _launch.OnPressed += _ => OnLaunch?.Invoke();
        Refresh();
    }

    public void SetState(SupportTabletBoundUserInterfaceState state)
    {
        _nextUse = state.NextUse;
        _hasTarget = state.HasTarget;
        Refresh();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        Refresh();
    }

    private void Refresh()
    {
        var remaining = (float) (_nextUse - _timing.CurTime).TotalSeconds;
        var ready = remaining <= 0f;

        _status.Text = ready
            ? Loc.GetString("support-tablet-ready")
            : Loc.GetString("support-tablet-reload", ("seconds", (int) MathF.Ceiling(remaining)));

        _signal.Text = Loc.GetString(_hasTarget ? "support-tablet-signal" : "support-tablet-no-signal");
        _launch.Disabled = !ready || !_hasTarget;
    }
}
