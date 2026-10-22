// Nestation Anarhey CC BY-NC 3.0
using Robust.Shared.Prototypes;

namespace Content.Server._Nestation.XenoHive;

[RegisterComponent]
public sealed partial class XenoEggComponent : Component
{
    [ViewVariables]
    public EntProtoId Hatch;

    [ViewVariables]
    public TimeSpan HatchAt;

    [ViewVariables]
    public int Class = 1;

    [ViewVariables]
    public EntityUid? Clutch;
}
