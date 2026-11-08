// Nestation new city CC BY-NC 3.0
using System.Numerics;
using Content.Shared._NeStation.Marine;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._NeStation.Marine;

public sealed class MarineAnnounceWindow : DefaultWindow
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly LineEdit _input;
    private readonly Button _send;
    private readonly Label _status;
    private TimeSpan _next;

    public event Action<string>? OnSend;

    public MarineAnnounceWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("marine-announce-title");
        MinSize = new Vector2(420, 120);

        _input = new LineEdit
        {
            PlaceHolder = Loc.GetString("marine-announce-placeholder"),
            HorizontalExpand = true,
        };
        _input.OnTextEntered += _ => Send();

        _send = new Button { Text = Loc.GetString("marine-announce-send") };
        _send.OnPressed += _ => Send();

        _status = new Label { Modulate = Color.Gray };

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        row.AddChild(_input);
        row.AddChild(_send);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        root.AddChild(row);
        root.AddChild(_status);
        Contents.AddChild(root);
    }

    public void Update(MarineAnnounceConsoleState state)
    {
        _next = state.NextAnnounceTime;
        _input.IsValid = text => text.Length <= state.MaxLength;
    }

    private void Send()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0 || _timing.CurTime < _next)
            return;

        OnSend?.Invoke(text);
        _input.Clear();
        _next = _timing.CurTime + TimeSpan.FromSeconds(10);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var left = _next - _timing.CurTime;
        if (left > TimeSpan.Zero)
        {
            _send.Disabled = true;
            _status.Text = Loc.GetString("marine-announce-wait", ("seconds", (int) Math.Ceiling(left.TotalSeconds)));
        }
        else
        {
            _send.Disabled = false;
            _status.Text = Loc.GetString("marine-announce-ready");
        }
    }
}
