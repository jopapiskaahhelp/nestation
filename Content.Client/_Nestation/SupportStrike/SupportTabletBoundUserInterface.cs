// Nestation New City CC BY-NC 3.0
using Content.Shared._Nestation.SupportStrike;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Nestation.SupportStrike;

[UsedImplicitly]
public sealed class SupportTabletBoundUserInterface : BoundUserInterface
{
    private SupportTabletWindow? _window;

    public SupportTabletBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<SupportTabletWindow>();
        _window.OnLaunch += () => SendMessage(new SupportTabletLaunchMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is SupportTabletBoundUserInterfaceState tablet)
            _window?.SetState(tablet);
    }
}
