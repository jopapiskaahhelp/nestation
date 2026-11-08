// Nestation new city CC BY-NC 3.0
using Content.Shared._NeStation.Marine;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.Client._NeStation.Marine;

public sealed class MarineAnnounceBoundUserInterface : BoundUserInterface
{
    private MarineAnnounceWindow? _window;

    public MarineAnnounceBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new MarineAnnounceWindow();
        _window.OnClose += Close;
        _window.OnSend += text => SendMessage(new MarineAnnounceSendMessage(text));
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is MarineAnnounceConsoleState s)
            _window?.Update(s);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }
}
