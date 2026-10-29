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
    public float RegenInterval = 3f;

    [DataField]
    public int ObstacleShotLimit = 8;

    public EntityUid? Gun;
    public EntityUid? Grid;

    public TimeSpan NextThink;
    public TimeSpan NextRegen;
    public TimeSpan NextAction;
    public TimeSpan NextStuckCheck;
    public TimeSpan LastShot;

    public Vector2 MoveDir;
    public bool WasMoving;

    public Vector2i? Goal;
    public List<Vector2i> Path = new();
    public HashSet<Vector2i> Visited = new();
    public HashSet<Vector2i> Unbreakable = new();
    public HashSet<EntityUid> UnbreakableEnts = new();

    public EntityUid? Obstacle;
    public int ObstacleShots;
    public bool ObstacleInfinite;

    public Vector2 LastPos;
    public int StuckCount;
}
