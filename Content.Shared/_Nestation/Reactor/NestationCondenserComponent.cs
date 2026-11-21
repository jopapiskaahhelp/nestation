// Nestation CC BY-NC 3.0
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Nestation.Reactor;

[RegisterComponent]
public sealed partial class NestationCondenserComponent : Component
{
    [DataField]
    public float HeatCapacity = 1_000_000f;

    [DataField]
    public float Conductance = 120_000f;

    public float Temp = 20f;
    public float HeatIn;
    public float HeatOut;
}
