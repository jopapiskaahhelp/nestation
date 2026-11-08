// Nestation New City CC BY-NC 3.0
using Content.Shared._Nestation.Marines;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Nestation.Marines;

[UsedImplicitly]
public sealed class MarineMissionBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private MarineMissionWindow? _window;

    public MarineMissionBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<MarineMissionWindow>();
        _window.OnStart += () => SendMessage(new MarineMissionStartMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is MarineMissionUiState missionState)
            _window?.Update(missionState);
    }
}
