// Nestation CC BY-NC 3.0
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Nestation.Reactor;

[RegisterComponent]
public sealed partial class NestationTurbineComponent : Component
{
    [DataField]
    public float MaxSteamPower = 3_300_000f;

    [DataField]
    public float RatedPower = 1_000_000f;

    [DataField]
    public float NominalEfficiency = 0.30f;

    [DataField]
    public float Inertia = 4f;

    [DataField]
    public float RotorInertia = 2700f;

    public float Rpm;
    public float Efficiency;
    public float SteamPower;
    public float MechanicalPower;
    public float Gross;
    public bool Tripped;
    public float OverspeedTimer;
    public float PrevRpm;
    public float RpmRate;
}
