// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._Nestation.NorthKoreanGuard;

public enum NorthKoreanGuardState : byte
{
    Post,
    Build,
    Fight,
    Return,
}

public sealed class NorthKoreanGuardProvocation
{
    public bool Warned;
    public bool Hostile;
    public bool InZone;
    public int Approaches;
    public TimeSpan WarnedAt;
    public TimeSpan LastNear;
}

[RegisterComponent]
public sealed partial class NorthKoreanGuardComponent : Component
{
    [DataField]
    public EntProtoId GunProto = "WeaponRifleAkNorthKoreanGuard";

    [DataField]
    public EntProtoId FortificationProto = "NorthKoreanGuardBarricade";

    [DataField]
    public string WarnMessage = "north-korean-guard-warn";

    [DataField]
    public float WarnRange = 2f;

    [DataField]
    public float ClearRange = 3f;

    [DataField]
    public float ThreatRange = 13f;

    [DataField]
    public float WarnGrace = 3f;

    [DataField]
    public float LoiterLimit = 10f;

    [DataField]
    public float ForgetAfter = 30f;

    [DataField]
    public int MaxApproaches = 2;

    [DataField]
    public float CalmDownTime = 10f;

    [DataField]
    public float FortifyDistance = 3f;

    [DataField]
    public float BuildTime = 2f;

    [DataField]
    public float AimDelay = 0.5f;

    [DataField]
    public float ShotGapMin = 0.25f;

    [DataField]
    public float ShotGapMax = 0.45f;

    public NorthKoreanGuardState State;

    public bool HasPost;
    public EntityCoordinates PostCoords;
    public Angle PostAngle;

    public EntityUid? Target;
    public EntityUid? AimingAt;
    public EntityUid? Gun;
    public bool FightingMarine;

    public TimeSpan NextScan;
    public TimeSpan LastDanger;
    public TimeSpan NextShot;
    public TimeSpan AimReadyAt;

    public bool Building;
    public TimeSpan BuildDoneAt;

    public EntityCoordinates? NestFire;
    public List<EntityCoordinates> NestSlots = new();
    public List<EntityUid?> NestBuilt = new();

    public Dictionary<EntityUid, NorthKoreanGuardProvocation> Provocations = new();

    public Vector2 MoveDir;
    public bool WasMoving;
    public Vector2 LastPos;
    public TimeSpan NextStuckCheck;
    public int StuckCount;
}

[RegisterComponent]
public sealed partial class NorthKoreanGuardFortificationComponent : Component
{
    public EntityUid Guard;
}
