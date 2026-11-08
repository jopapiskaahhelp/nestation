// Nestation New City CC BY-NC 3.0
using Content.Shared._Nestation.Marines;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Nestation.Marines;

[UsedImplicitly]
public sealed class MarineSupplyBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private MarineSupplyWindow? _window;

    public MarineSupplyBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<MarineSupplyWindow>();
        _window.OnBuy += id => SendMessage(new MarineSupplyBuyMessage(id));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is MarineSupplyUiState supplyState)
            _window?.Update(supplyState);
    }
}
