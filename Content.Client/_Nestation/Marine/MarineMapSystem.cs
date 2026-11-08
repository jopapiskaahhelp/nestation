// Nestation new city CC BY-NC 3.0
using Content.Shared._NeStation.Marine;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Client._NeStation.Marine;

public sealed class MarineMapSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;

    private MarineMapWindow? _window;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<MarineMapOpenEvent>(OnOpen);
        SubscribeNetworkEvent<MarineMapLayoutEvent>(ev => _window?.Map.SetLayout(ev.Layout));
        SubscribeNetworkEvent<MarineMapUpdateEvent>(OnUpdate);
        SubscribeNetworkEvent<MarineMapCloseEvent>(_ => CloseWindow(false));
        SubscribeNetworkEvent<MarineMapStrokeEvent>(ev => _window?.Map.AddRemoteStroke(ev.Stroke));
        SubscribeNetworkEvent<MarineMapEraseEvent>(ev => _window?.Map.EraseRemote(ev.Pos, ev.Radius));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CloseWindow(false);
    }

    private void OnOpen(MarineMapOpenEvent ev)
    {
        CloseWindow(false);

        var window = new MarineMapWindow();
        window.Map.SetLayout(ev.Layout);
        window.Map.SetStrokes(ev.Strokes);
        window.SetCanDraw(ev.CanDraw);

        window.Map.OnStroke += stroke => RaiseNetworkEvent(new MarineMapStrokeEvent(stroke));
        window.Map.OnErase += (pos, radius) => RaiseNetworkEvent(new MarineMapEraseEvent(pos, radius));
        window.OnClose += OnWindowClosed;

        _window = window;
        window.OpenCenteredLeft();
    }

    private void OnUpdate(MarineMapUpdateEvent ev)
    {
        if (_window == null)
            return;

        var self = _player.LocalEntity is { } local ? GetNetEntity(local) : NetEntity.Invalid;
        _window.Map.SetBlips(ev.Blips, self);
        _window.SetCanDraw(ev.CanDraw);
    }

    private void OnWindowClosed()
    {
        _window = null;
        RaiseNetworkEvent(new MarineMapClosedEvent());
    }

    private void CloseWindow(bool notifyServer)
    {
        if (_window == null)
            return;

        var window = _window;
        _window = null;

        window.OnClose -= OnWindowClosed;
        window.Close();

        if (notifyServer)
            RaiseNetworkEvent(new MarineMapClosedEvent());
    }
}
