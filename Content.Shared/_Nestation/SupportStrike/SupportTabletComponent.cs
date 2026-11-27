// Nestation New City CC BY-NC 3.0
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Nestation.SupportStrike;

[RegisterComponent]
public sealed partial class SupportTabletComponent : Component
{
    [DataField]
    public float Cooldown = 90f;

    [DataField]
    public float LaunchDelay = 2.9f;

    [DataField]
    public EntProtoId RocketProto = "SupportRocket";

    [DataField]
    public SoundSpecifier? ResponseSound;

    public TimeSpan NextUse;
}
