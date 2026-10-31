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

    public bool Screaming;
    public EntityUid? AimTarget;

    public Vector2 MoveDir;
    public bool WasMoving;

    public Vector2i? Goal;
    public List<Vector2i> Path = new();
    public Dictionary<EntityUid, UnbalancedGridMemory> Memory = new();
    public HashSet<EntityUid> UnbreakableEnts = new();
    public HashSet<string> UnbreakableProtos = new();

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
