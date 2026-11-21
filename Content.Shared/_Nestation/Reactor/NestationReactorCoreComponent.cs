// Nestation CC BY-NC 3.0
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Nestation.Reactor;

[RegisterComponent]
public sealed partial class NestationReactorCoreComponent : Component
{
    [DataField]
    public float NominalPower = 3_000_000f;

    [DataField]
    public float FuelHeatCapacity = 90_000f;

    [DataField]
    public float PipingHeatCapacity = 5_000_000f;

    [DataField]
    public float InventoryNominal = 700f;

    [DataField]
    public float FuelConductance = 10_000f;

    [DataField]
    public float ReferenceFuelTemp = 600f;

    [DataField]
    public float TempCoefficient = 0.0007f;

    [DataField]
    public float VoidCoefficient = 0.5f;

    [DataField]
    public float BoilingTemp = 335f;

    [DataField]
    public float CrisisTemp = 340f;

    [DataField]
    public float MeltTemp = 2800f;

    [DataField]
    public float ZirconiumMass = 600f;

    [DataField]
    public float FuelMass = 1500f;

    [DataField]
    public float CavityWater = 3000f;

    [DataField]
    public float HallVolume = 2500f;

    [DataField]
    public float DecayTimeScale = 5f;

    [DataField]
    public float DecayHeatBoost = 4f;

    [DataField]
    public float DamageLimit = 4000f;

    [DataField]
    public float DelayedNeutronFraction = 0.0065f;

    public float FuelTemp = 20f;
    public float CoolantTemp = 20f;
    public float Power;
    public float Target;
    public float DecayHeat;
    public float OperatingTime;
    public float ShutdownTime;
    public float PrePower;
    public float ThermalPower;
    public float DecayPower;
    public float Inventory = 700f;
    public float ZirconiumOxidized;
    public float Hydrogen;
    public float HydrogenRate;
    public float RodInsertion = 1f;
    public float RodTarget = 1f;
    public float Setpoint = 0.1f;
    public bool AutoMode;
    public bool Scrammed;
    public bool ProtectionsBypassed;
    public bool FuelLocked;
    public bool Exploded;
    public bool RodsJammed;
    public bool ControlLost;
    public float Integrity = 1f;
    public float Breach;
    public float ForcedBreach;
    public bool ForcedJam;
    public float MeltTimer;
    public float ExcursionTimer;
    public float PressureTimer;
    public float DryoutSeconds = -1f;
    public float PotentialTnt;
    public float Countdown = -1f;
    public NestationWarning Warnings;
}
