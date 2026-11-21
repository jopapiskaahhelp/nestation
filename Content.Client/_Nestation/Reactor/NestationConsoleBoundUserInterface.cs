// Nestation CC BY-NC 3.0
using Content.Shared._Nestation.Reactor;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.Client._Nestation.Reactor;

public sealed class NestationConsoleBoundUserInterface : BoundUserInterface
{
    private NestationConsoleWindow? _window;

    public NestationConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<NestationConsoleWindow>();
        _window.OnCommand += message => SendMessage(message);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is NestationConsoleState consoleState)
            _window?.Update(consoleState);
    }
}
