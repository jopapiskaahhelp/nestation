// Nestation CC BY-NC 3.0
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Nestation.Reactor;

[DataDefinition]
public sealed partial class NestationPort
{
    [DataField(required: true)]
    public Direction Side;

    [DataField(required: true)]
    public NestationMedium Medium;

    [DataField(required: true)]
    public NestationPortRole Role;

    [DataField]
    public Vector2i Offset = Vector2i.Zero;
}

[RegisterComponent]
public sealed partial class NestationPortsComponent : Component
{
    [DataField]
    public List<NestationPort> Ports = new();

    [DataField]
    public Vector2i Size = Vector2i.One;

    [DataField]
    public bool Control;
}
