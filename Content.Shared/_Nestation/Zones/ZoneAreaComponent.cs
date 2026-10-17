// Nestation Anarhey CC BY-NC 3.0
using System.Numerics;
using Robust.Shared.Prototypes;

namespace Content.Shared.Zones;

[RegisterComponent]
public sealed partial class ZoneAreaComponent : Component
{
    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public Vector2 Size = new(20f, 20f);
}
