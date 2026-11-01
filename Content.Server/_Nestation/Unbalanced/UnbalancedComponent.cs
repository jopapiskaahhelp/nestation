// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._Nestation.Unbalanced;

[RegisterComponent]
public sealed partial class UnbalancedComponent : Component
{
    [DataField]
    public EntProtoId GunProto = "WeaponShotgunKammererUnbalanced";

    [DataField]
    public float ShootRange = 12f;

    [DataField]
    public float AimDelay = 1f;

    [DataField]
    public float RegenInterval = 3f;

    [DataField]
    public int ObstacleShotLimit = 8;

    [DataField]
    public int MagazineSize = 7;

    [DataField]
    public float ReloadTime = 3.3f;

    [DataField]
    public float TurretScanRange = 24f;

    [DataField]
    public float TurretDangerRange = 13f;

    [DataField]
    public float TurretCloseRange = 6.5f;

    [DataField]
    public float TurretFireRange = 5.5f;

    [DataField]
    public float ScreamBelow = 0.5f;

    [DataField]
    public float ScreamUntil = 0.51f;

    [DataField]
    public float ScreamInterval = 20f;

    public EntityUid? Gun;
    public EntityUid? Grid;

    public TimeSpan NextThink;
    public TimeSpan NextRegen;
    public TimeSpan NextAction;
    public TimeSpan NextStuckCheck;
    public TimeSpan NextHealthCheck;
    public TimeSpan NextScream;
    public TimeSpan LastShot;
    public TimeSpan AimReadyAt;

    public TimeSpan SpeechBusyUntil;
    public TimeSpan NextIdleLine;
    public TimeSpan NextCloseLine;
    public TimeSpan NextHitLine;
    public TimeSpan NextMissLine;
    public TimeSpan LastDamaged;
    public TimeSpan ReloadUntil;
    public TimeSpan ReloadSayAt;
    public TimeSpan MissShotAt;
    public TimeSpan MissCheckAt;
    public TimeSpan GlowUntil;
    public TimeSpan ReloadStartedAt;
    public int ReloadSoundStep;

    public EntityUid? Voice;
    public int ShotsInMagazine;
    public int EngagedShots;

    public bool Screaming;
    public EntityUid? AimTarget;

    public Vector2 MoveDir;
    public bool WasMoving;

    public Vector2i? Goal;
    public List<Vector2i> Path = new();
    public Dictionary<EntityUid, UnbalancedGridMemory> Memory = new();
    public HashSet<EntityUid> UnbreakableEnts = new();
    public HashSet<string> UnbreakableProtos = new();

    public Dictionary<EntityUid, UnbalancedTurret> Turrets = new();
    public HashSet<Vector2i> DangerTiles = new();
    public EntityUid? TurretGrid;
    public EntityUid? TurretTarget;
    public Vector2i? ForcedGoal;
    public TimeSpan NextTurretScan;

    public EntityUid? Obstacle;
    public int ObstacleShots;
    public bool ObstacleInfinite;

    public Vector2 LastPos;
    public int StuckCount;
}

public sealed class UnbalancedGridMemory
{
    public HashSet<Vector2i> Visited = new();
    public HashSet<Vector2i> Unbreakable = new();
}

public sealed class UnbalancedTurret
{
    public bool Weak;
    public bool Engage;
    public int Shots;
    public TimeSpan RefreshAt;
    public TimeSpan IgnoreUntil;
    public HashSet<Vector2i> Exposed = new();
}
