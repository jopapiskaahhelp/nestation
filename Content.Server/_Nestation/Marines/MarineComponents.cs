// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Shared._Nestation.Marines;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._Nestation.Marines;

public enum MarinePointKind : byte
{
    Walk,
    Talk,
    Bar,
    Queue,
    Counter,
    Assembly,
    Patrol,
    Hall,
}

public enum MarineCivilianMode : byte
{
    Idle,
    Route,
    Meeting,
    Talking,
    Busy,
}

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
    public string Team = "south";

    [DataField]
    public bool Defender;

    [DataField]
    public bool TargetXeno = true;

    [DataField]
    public bool TargetXenoStructures = true;

    [DataField]
    public bool Static;

    [DataField]
    public bool Passive;

    [DataField]
    public bool Neutral;

    [DataField]
    public string Route = string.Empty;

    [DataField]
    public string PatrolRoute = string.Empty;

    [DataField]
    public bool Police;

    [DataField]
    public bool Banker;

    [DataField]
    public bool Lecturer;

    [DataField]
    public bool CanBuild;

    [DataField]
    public int MaxBarricades = 2;

    [DataField]
    public EntProtoId Barricade = "MarineBarricade";

    [DataField]
    public bool DeleteGunOnDeath;

    [DataField]
    public Dictionary<EntProtoId, float> GunPool = new();

    [DataField]
    public float MemorySeconds = 25f;

    [DataField]
    public float AggroRange = 22f;

    [DataField]
    public float Leash = 30f;

    [DataField]
    public float HoldDistance = 7f;

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
    public EntityUid? Scene;
    public float Hunger;
    public float Thirst;
    public float Fear;
    public float Boldness = 0.5f;
    public bool PoliceNear;
    public Vector2 PoliceFrom;
    public TimeSpan NextAwareness;
    public TimeSpan NextSilence;
    public TimeSpan NextFlinch;
    public TimeSpan NextVoice;
    public TimeSpan ListenUntil;
    public TimeSpan StandUpAt;
    public EntityUid? MemTarget;
    public Vector2 MemPos;
    public Vector2 MemVel;
    public TimeSpan MemSeen;
    public List<Vector2> MemTrail = new();
    public Vector2? SearchPoint;
    public TimeSpan NextSearch;
    public Vector2? PatrolGoal;
    public List<Vector2>? PatrolPoints;
    public int PatrolIndex;
    public TimeSpan PatrolWait;
    public TimeSpan NextEncounter;
    public EntityUid? Hunt;
    public bool Wanted;
    public bool Partisan;
    public TimeSpan IgnoreRemovalUntil;
    public int AvoidSide;
    public TimeSpan AvoidUntil;
    public MarineCivilianMode CivMode;
    public EntityUid? Partner;
    public Vector2 MeetPoint;
    public bool Arrived;
    public bool Speaker;
    public TimeSpan TalkUntil;
    public TimeSpan MeetTimeout;
    public TimeSpan NextEmote;
    public TimeSpan NextActivity;
    public List<Vector2>? RoutePoints;
    public int RouteIndex;
    public bool Frozen;
    public bool Running = true;
    public bool Flanker;
    public Vector2? ScriptGoal;
    public Vector2? LastKnown;
    public TimeSpan LastSeen;
    public Vector2? HeardPoint;
    public TimeSpan HeardUntil;
    public Vector2? FlankPoint;
    public TimeSpan FlankUntil;
    public int Barricades;
    public TimeSpan NextBuild;
    public TimeSpan? BuildAt;
    public Vector2 BuildPos;
    public TimeSpan PanicUntil;
    public Vector2 PanicFrom;
    public EntityUid? Emplacement;
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
    public Vector2? Home;
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

    [DataField]
    public bool SpawnOnInit;
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

    [DataField]
    public string DefenderGroup = "defenders";

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

[RegisterComponent]
public sealed partial class MarineEmplacementComponent : Component
{
    [DataField]
    public EntProtoId Gunner = "MobNorthKoreanGunner";

    [DataField]
    public EntProtoId Piece = "MarineSandbag";

    [DataField]
    public EntProtoId Seat = "MarineGunSeat";

    [DataField]
    public bool Bunker;

    public EntityUid? GunnerUid;
    public EntityUid? SeatUid;
    public bool Destroyed;
    public List<EntityUid> Pieces = new();
}

[RegisterComponent]
public sealed partial class MarineSandbagComponent : Component;

[RegisterComponent]
public sealed partial class MarineGunSeatComponent : Component
{
    [DataField]
    public EntProtoId Gun = "WeaponHmgPlayer";

    [DataField]
    public float Radius = 0.55f;

    public EntityUid? Controller;
    public bool Active = true;
    public TimeSpan NextCheck;
    public Dictionary<EntityUid, EntityUid> Holders = new();
}

public enum MarineSceneKind : byte
{
    Search,
    Interrogation,
    Beating,
    Execution,
}

[RegisterComponent]
public sealed partial class MarineSceneComponent : Component
{
    [DataField]
    public MarineSceneKind Kind = MarineSceneKind.Execution;

    [DataField]
    public float TriggerRange = 14f;

    [DataField]
    public int SoldierCount = 2;

    [DataField]
    public int CivilianCount = 3;

    [DataField]
    public bool WithOfficer = true;

    [DataField]
    public bool Dynamic;

    [DataField]
    public EntProtoId Soldier = "MobNorthKoreanSoldier";

    [DataField]
    public EntProtoId Officer = "MobNorthKoreanOfficer";

    [DataField]
    public EntProtoId Civilian = "MobMarineCivilian";

    public List<EntityUid> Soldiers = new();
    public List<EntityUid> Civilians = new();
    public bool Started;
    public bool Done;
    public TimeSpan StartedAt;
    public List<Func<bool>> Steps = new();
    public int Step;
}

[RegisterComponent]
public sealed partial class MarineCivilianPointComponent : Component
{
    [DataField]
    public string Route = "default";

    [DataField]
    public int Order;

    [DataField]
    public bool Talk;

    [DataField]
    public MarinePointKind Kind = MarinePointKind.Walk;
}
