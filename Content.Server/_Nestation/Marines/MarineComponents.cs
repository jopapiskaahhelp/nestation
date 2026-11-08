// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Shared._Nestation.Marines;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._Nestation.Marines;

public enum MarineState : byte
{
    Advance,
    Gather,
    Raise,
    Guard,
    Cover,
    Hunt,
}

[RegisterComponent]
public sealed partial class MarineComponent : Component
{
    [DataField]
    public string MissionId = "dday";

    [DataField]
    public EntProtoId GunProto = "WeaponRifleLecterMarine";

    [DataField]
    public float ShootRange = 14f;

    [DataField]
    public float CriticalHealth = 20f;

    [DataField]
    public float DeadHealth = 30f;

    [DataField]
    public float SeparationRadius = 2f;

    [DataField]
    public float AimDelayMin = 0.35f;

    [DataField]
    public float AimDelayMax = 0.8f;

    [DataField]
    public float ShotGap = 0.28f;

    [DataField]
    public int BurstMin = 2;

    [DataField]
    public int BurstMax = 4;

    [DataField]
    public float BurstPauseMin = 0.7f;

    [DataField]
    public float BurstPauseMax = 1.5f;

    [DataField]
    public float ScreamCooldown = 4f;

    [DataField]
    public float CoverTimeMin = 2f;

    [DataField]
    public float CoverTimeMax = 4.5f;

    [DataField]
    public float CoverSearchRadius = 6f;

    [DataField]
    public int GatherCount = 5;

    [DataField]
    public float GatherRadius = 7f;

    [DataField]
    public float GatherMaxWait = 25f;

    [DataField]
    public float RaiseRange = 2.1f;

    public EntityUid? Gun;
    public MarineState State = MarineState.Advance;

    public TimeSpan NextThink;
    public TimeSpan NextScan;
    public TimeSpan NextStuckCheck;
    public TimeSpan NextResist;
    public TimeSpan NextScream;
    public TimeSpan NextCover;
    public TimeSpan CoverUntil;
    public TimeSpan LastDamaged;
    public TimeSpan NextShot;
    public TimeSpan AimReadyAt;
    public TimeSpan BurstPauseUntil;
    public TimeSpan EscapeUntil;
    public TimeSpan CommitUntil;
    public TimeSpan NextRoam;
    public TimeSpan NextOpen;
    public TimeSpan EngageSince;

    public int LastScreamStep;
    public int BurstLeft;
    public int StuckCount;

    public EntityUid? Target;
    public EntityUid? Flag;
    public Vector2? CoverPoint;
    public Vector2? RoamPoint;
    public Vector2 MoveDir;
    public Vector2 EscapeDir;
    public Vector2 CommitDir;
    public Vector2 LastPos;
    public bool WasMoving;
    public bool Holding;
    public Dictionary<EntityUid, TimeSpan> Ignored = new();
}

[RegisterComponent]
public sealed partial class MarineSpawnZoneComponent : Component
{
    [DataField]
    public string MissionId = "dday";

    [DataField]
    public string Group = "landing";

    [DataField]
    public EntProtoId Marine = "MobMarine";

    [DataField]
    public int Count = 1;

    [DataField]
    public float Spread = 0.4f;
}

[RegisterComponent]
public sealed partial class MarineFlagpoleComponent : Component
{
    [DataField]
    public string MissionId = "dday";

    [DataField]
    public int Order;

    [DataField]
    public float RaiseTime = 6f;

    [DataField]
    public int ReinforcementCount = 4;

    [DataField]
    public string SpawnGroup = "reinforce";

    [DataField]
    public int Reward = 300;

    [DataField]
    public int RewardPerPlayer = 60;

    [DataField]
    public float OrderBonus = 0.25f;

    public bool Raised;
    public EntityUid? Raiser;
    public TimeSpan RaiseEnd;
    public TimeSpan RaisedAt;
}

[RegisterComponent]
public sealed partial class MarineMissionConsoleComponent : Component
{
    [DataField]
    public string MissionId = "dday";

    [DataField]
    public string ZoneGroup = "landing";

    [DataField]
    public int BatchSize = 10;

    [DataField]
    public int Total = 30;

    [DataField]
    public float WaveInterval = 10f;

    [DataField]
    public int StartCredits = 800;

    [DataField]
    public int StartCreditsPerPlayer = 100;

    public bool Started;
    public int Spawned;
    public TimeSpan NextWave;
}

[RegisterComponent]
public sealed partial class MarineGateComponent : Component
{
    [DataField]
    public string MissionId = "dday";

    [DataField]
    public int Length = 4;

    [DataField]
    public EntProtoId SegmentProto = "MarineGateSegment";

    public bool Child;
    public List<EntityUid> Segments = new();
}

[RegisterComponent]
public sealed partial class MarineSupplyLiftComponent : Component
{
    [DataField]
    public string MissionId = "dday";

    [DataField]
    public float DeliveryDelay = 30f;

    [DataField]
    public float LowerTime = 2.4f;

    [DataField]
    public float LoweredTime = 6f;

    [DataField]
    public float RaiseTime = 2.4f;

    [DataField]
    public int MaxQueue = 24;

    public MarineLiftState State = MarineLiftState.Raised;
    public TimeSpan? DeliveryAt;
    public TimeSpan StateEnd;
    public List<EntProtoId> Queue = new();
}

[RegisterComponent]
public sealed partial class MarineSupplyConsoleComponent : Component
{
    [DataField]
    public string MissionId = "dday";
}
