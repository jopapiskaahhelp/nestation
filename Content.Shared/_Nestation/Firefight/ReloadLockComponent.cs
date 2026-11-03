// Nestation New City CC BY-NC 3.0
using Robust.Shared.GameStates;

namespace Content.Shared._Nestation.Firefight;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ReloadLockComponent : Component
{
    [AutoNetworkedField]
    public TimeSpan LockedUntil;

    [ViewVariables]
    public bool LastMagazineEmpty = true;
}
