// Nestation CC BY-NC 3.0
using Robust.Shared.Serialization;

namespace Content.Shared._Nestation.Reactor;

[Serializable, NetSerializable]
public enum NestationMedium : byte
{
    Coolant,
    Steam,
    Feed,
    Cooling,
    Shaft,
    Control,
}

[Serializable, NetSerializable]
public enum NestationPortRole : byte
{
    ReactorHot,
    ReactorCold,
    PumpIn,
    PumpOut,
    SgPrimaryIn,
    SgPrimaryOut,
    SgSteamOut,
    SgFeedIn,
    TurbineSteamIn,
    TurbineExhaust,
    TurbineShaft,
    GeneratorShaft,
    CondenserExhaustIn,
    CondenserCondensateOut,
    CondenserCoolingIn,
    CondenserCoolingOut,
    PoolPort,
    ConsoleLink,
}

[Serializable, NetSerializable]
public enum NestationPumpKind : byte
{
    Primary,
    Feed,
    Circulation,
}

[Serializable, NetSerializable]
public enum NestationVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum NestationVisualState : byte
{
    Off,
    Running,
    Hot,
    Alarm,
    Broken,
    Critical,
}

[Flags, Serializable, NetSerializable]
public enum NestationWarning
{
    None = 0,
    FuelOverheat = 1 << 0,
    HighPower = 1 << 1,
    LowFlow = 1 << 2,
    HighPressure = 1 << 3,
    HighCoolantTemp = 1 << 4,
    LowSgLevel = 1 << 5,
    HighSgLevel = 1 << 6,
    HeatSinkLoss = 1 << 7,
    TurbineOverspeed = 1 << 8,
    Hydrogen = 1 << 9,
    PoolHot = 1 << 10,
    PoolLow = 1 << 11,
    PrimaryOpen = 1 << 12,
    SecondaryOpen = 1 << 13,
    CoolingOpen = 1 << 14,
    NoFuel = 1 << 15,
    PowerLoss = 1 << 16,
    ProtectionsBypassed = 1 << 17,
    MeltImminent = 1 << 18,
    Scram = 1 << 19,
    TurbineTrip = 1 << 20,
    ReliefOpen = 1 << 21,
    CoreUncovered = 1 << 22,
    ZirconiumReaction = 1 << 23,
    VesselDamaged = 1 << 24,
    ControlLost = 1 << 25,
    RodsJammed = 1 << 26,
}

[Serializable, NetSerializable]
public enum NestationConsoleUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum NestationCommand : byte
{
    Scram,
    ResetScram,
    ToggleAuto,
    SetpointDelta,
    RodsDelta,
    PumpDelta,
    PumpAuto,
    ToggleBreaker,
    ResetTurbine,
    ToggleBypass,
    RunTest,
}

[Serializable, NetSerializable]
public sealed class NestationConsoleMessage : BoundUserInterfaceMessage
{
    public readonly NestationCommand Command;
    public readonly float Amount;
    public readonly NestationPumpKind Pump;

    public NestationConsoleMessage(NestationCommand command, float amount = 0f, NestationPumpKind pump = NestationPumpKind.Primary)
    {
        Command = command;
        Amount = amount;
        Pump = pump;
    }
}

[Serializable, NetSerializable]
public sealed class NestationConsoleState : BoundUserInterfaceState
{
    public bool Linked;
    public bool Scrammed;
    public bool AutoMode;
    public bool Bypassed;
    public bool HasFuel;
    public bool BreakerClosed;
    public bool TurbineTripped;
    public bool HasGenerator;

    public float Setpoint;
    public float Rods;
    public float Neutron;
    public float ThermalPower;
    public float FuelTemp;
    public float CoolantTemp;
    public float SteamTemp;
    public float SteamPressure;
    public float SgLevel;
    public float PrimaryFlow;
    public float FeedFlow;
    public float CirculationFlow;
    public float PrimarySetpoint;
    public float FeedSetpoint;
    public float CirculationSetpoint;
    public bool FeedAuto;
    public bool CirculationAuto;
    public float Rpm;
    public float Gross;
    public float Net;
    public float Own;
    public float Efficiency;
    public float CondenserTemp;
    public float HeatToPool;
    public float PoolTemp;
    public float PoolLevel;
    public int PoolSegments;
    public float ReserveMj;
    public float Burnup;
    public float Hydrogen;
    public float Inventory;
    public float DryoutSeconds = -1f;
    public float DecayKw;
    public float Integrity = 1f;
    public float PotentialTnt;
    public float SafeRadius;
    public float Countdown = -1f;
    public NestationWarning Warnings;
}
