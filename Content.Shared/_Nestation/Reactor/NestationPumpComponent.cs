// Nestation CC BY-NC 3.0
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Nestation.Reactor;

[RegisterComponent]
public sealed partial class NestationPumpComponent : Component
{
    [DataField(required: true)]
    public NestationPumpKind Kind;

    [DataField]
    public float NominalPower = 60_000f;

    [DataField]
    public float Setpoint = 1f;

    [DataField]
    public bool Auto;

    [DataField]
    public float SpinUpTime = 6f;

    [DataField]
    public float CoastDownTime = 14f;

    public float Speed;
    public float PowerDraw;
}
