// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._Nestation.Marines;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class MarineParachuteComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan Start;

    [DataField, AutoNetworkedField]
    public float Duration = 3.2f;

    [DataField, AutoNetworkedField]
    public float Height = 6f;

    [DataField, AutoNetworkedField]
    public Vector2 EndOffset;
}
