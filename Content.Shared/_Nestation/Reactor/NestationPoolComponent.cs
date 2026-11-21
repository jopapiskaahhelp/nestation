// Nestation CC BY-NC 3.0
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Nestation.Reactor;

[RegisterComponent]
public sealed partial class NestationPoolComponent : Component
{
    [DataField]
    public float HeatCapacity = 2_500_000f;

    [DataField]
    public float LossPerSegment = 6_000f;

    [DataField]
    public float LatentCapacity = 300_000_000f;

    [DataField]
    public float MakeupRate = 0.0002f;

    public float Temperature = 20f;
    public float Level = 1f;
}
