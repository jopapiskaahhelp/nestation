// Nestation CC BY-NC 3.0
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Nestation.Reactor;

[RegisterComponent]
public sealed partial class NestationSteamGeneratorComponent : Component
{
    [DataField]
    public float PrimaryConductance = 75_000f;

    [DataField]
    public float HeatCapacity = 600_000f;

    [DataField]
    public float SteamSetTemp = 260f;

    [DataField]
    public float ReliefTemp = 280f;

    [DataField]
    public float ReliefConductance = 120_000f;

    [DataField]
    public float BurstPressure = 85f;

    [DataField]
    public float BypassCapacity = 1_500_000f;

    [DataField]
    public float DamageLimit = 1500f;

    [DataField]
    public float SteamVolume = 6f;

    [DataField]
    public float WaterMassFull = 8000f;

    public float Temp = 20f;
    public float SetTemp = 20f;
    public float Level = 0.5f;
    public float Pressure;
    public float TurbinePower;
    public float BypassPower;
    public float ReliefPower;
    public float SteamFlow;
    public float BurstTimer;
}
