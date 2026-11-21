// Nestation CC BY-NC 3.0
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Nestation.Reactor;

[RegisterComponent]
public sealed partial class NestationFuelAssemblyComponent : Component
{
    [DataField]
    public float BaseReactivity = 1.25f;

    [DataField]
    public float ReactivityLoss = 0.5f;

    [DataField]
    public float LifeSeconds = 36_000f;

    [DataField]
    public float Burnup;
}
