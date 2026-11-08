// Nestation new city CC BY-NC 3.0
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._NeStation.Marine;

[RegisterComponent]
public sealed partial class MarineMapUserComponent : Component
{
    [DataField]
    public EntityUid? ActionEntity;
}
