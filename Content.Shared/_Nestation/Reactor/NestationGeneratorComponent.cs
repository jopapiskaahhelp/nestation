// Nestation CC BY-NC 3.0
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Nestation.Reactor;

[RegisterComponent]
public sealed partial class NestationGeneratorComponent : Component
{
    [DataField]
    public float RatedPower = 1_000_000f;

    [DataField]
    public float ReserveCapacity = 45_000_000f;

    [DataField]
    public float ReserveChargePower = 20_000f;

    public float Reserve = 45_000_000f;
    public bool BreakerClosed = true;
    public float Availability = 1f;
    public float Gross;
    public float Own;
    public float Net;
}
