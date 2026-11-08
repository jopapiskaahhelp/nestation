// Nestation New City CC BY-NC 3.0
using Robust.Shared.Prototypes;

namespace Content.Shared._Nestation.SupportStrike;

[RegisterComponent]
public sealed partial class SupportTabletComponent : Component
{
    [DataField]
    public float Cooldown = 90f;

    [DataField]
    public EntProtoId RocketProto = "SupportRocket";

    public TimeSpan NextUse;
}
