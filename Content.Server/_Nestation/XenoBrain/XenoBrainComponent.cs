// Nestation Anarhey CC BY-NC 3.0
using System.Numerics;

namespace Content.Server._Nestation.XenoBrain;

public enum XenoMode : byte
{
    Patrol,
    Attack,
    Rally,
    Flank,
    Detour,
    Retreat,
    Ambush,
    Kite,
}

[RegisterComponent]
public sealed partial class XenoBrainComponent : Component
{

    [DataField] public float PackRadius = 22f;

    [DataField] public float SightRange = 24f;

    [DataField] public float AmbushTriggerRange = 6f;

    [DataField] public float StuckSeconds = 2f;
    [DataField] public float StuckSecondsAggro = 1f;

    [DataField] public List<string> UnbreakableComponents = new() { "Gatherable", "Indestructible" };

    [DataField] public List<string> UnbreakablePrefixes = new() { "WallRock", "FenceMetalStraight" };

    [DataField] public float StuckMinMove = 0.3f;

    [DataField] public int MaxDetoursPerSpot = 2;

    [DataField] public int RetreatHits = 3;
    [DataField] public float HitWindow = 6f;

    [DataField] public float RetaliateSeconds = 8f;

    [DataField] public float RetreatMaxSeconds = 8f;
    [DataField] public float AmbushSeconds = 25f;
    [DataField] public float CautiousSeconds = 45f;
    [DataField] public float NoRetreatSeconds = 12f;

    [DataField] public float RetreatEngageRange = 3.5f;

    [DataField] public float ClosingEngageRange = 9f;

    [DataField] public float SkirmishChance = 0.6f;

    [DataField] public float StrikeContactSeconds = 1.2f;

    [DataField] public float BaseSpeed = 1.15f;
    [DataField] public float CautiousSpeed = 0.8f;
    [DataField] public float RetreatSpeed = 1.35f;
    [DataField] public float LeapSpeed = 1.65f;

    [DataField] public float PlanCooldown = 0.15f;
    [DataField] public float LeapSeconds = 3f;

    [ViewVariables] public XenoMode Mode = XenoMode.Patrol;
    [ViewVariables] public TimeSpan ModeSince;
    [ViewVariables] public TimeSpan NextThink;
    [ViewVariables] public EntityUid? Target;
    [ViewVariables] public EntityUid? RallyTarget;
    [ViewVariables] public Vector2? GoalPos;

    public EntityUid? LastPlayerAttacker;
    public TimeSpan LastPlayerAttackTime;
    public Vector2 ThreatPos;
    public EntityUid? Retaliator;
    public TimeSpan RetaliatorTime;
    public readonly List<TimeSpan> RecentHits = new();

    public Vector2? RetreatSpot;
    public Vector2? DetourPoint;
    public Vector2? StuckPoint;

    public TimeSpan AmbushUntil;
    public TimeSpan CautiousUntil;
    public TimeSpan LeapUntil;
    public TimeSpan NoRetreatUntil;
    public TimeSpan NoLurkUntil;
    public TimeSpan NoRallyUntil;
    public TimeSpan NoFlankUntil;
    public TimeSpan PersistUntil;
    public TimeSpan HoldStart;

    public Vector2 StuckAnchor;
    public TimeSpan StuckSince;
    public Vector2 LastDetourStuckPos;
    public TimeSpan LastDetourTime;
    public int DetourCount;
    public readonly List<(Vector2 Pos, TimeSpan Expire)> AvoidPoints = new();

    public bool SmashActive;
    public EntityUid? SmashTarget;
    public float SmashBaseline;
    public int SmashNoProgress;
    public TimeSpan NoHuntUntil;

    public float PrevPlayerDist = float.MaxValue;

    public TimeSpan ContactSince;
    public TimeSpan LastHitTime;
    public EntityUid? Striker;
    public bool SkirmishActive;
    public TimeSpan SkirmishRollAt;

    public TimeSpan NextSteer;
    public Vector2? BypassPoint;
    public readonly List<Vector2> Route = new();
    public Vector2 RouteStart;
    public bool RouteKnown;
    public Vector2 SmashFrom;
    public Vector2 SmashPos;
    public Vector2 SmashDir;
    public TimeSpan BypassUntil;
    public int PackIndex;
    public Vector2 PackLeaderPos;
    public readonly List<Vector2> Allies = new();
    public float TargetDist = float.MaxValue;

    public float SpeedMult = 1f;
    public int FlankSide = 1;
    public bool DeathRecorded;
}
