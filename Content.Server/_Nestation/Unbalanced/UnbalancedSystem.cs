// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Physics;
using Content.Shared.SSDIndicator;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.Unbalanced;

public sealed partial class UnbalancedSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private SharedDoorSystem _door = default!;
    [Dependency] private SharedEntityStorageSystem _storage = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedMoverController _mover = default!;

    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    private const int BlockMask = (int) CollisionGroup.MobMask;
    private const int SightMask = (int) (CollisionGroup.Impassable | CollisionGroup.InteractImpassable);

    private const float ThinkInterval = 0.1f;
    private const float WallStop = 1.05f;
    private const float OpenReach = 1.2f;
    private const float SideOffset = 0.28f;
    private const float WaypointRadiusSq = 0.1225f;
    private const float WallCost = 12f;
    private const float OpenableCost = 3f;
    private const int PlanBudget = 20000;

    private static readonly Vector2i[] Dirs =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
    };

    public override void Initialize()
    {
        base.Initialize();

        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
        SubscribeLocalEvent<UnbalancedComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<UnbalancedComponent> ent, ref MapInitEvent args)
    {
        RemComp<SSDIndicatorComponent>(ent);

        var now = _timing.CurTime;
        ent.Comp.NextThink = now;
        ent.Comp.NextRegen = now + TimeSpan.FromSeconds(ent.Comp.RegenInterval);

        if (TryComp<CombatModeComponent>(ent, out var combat))
            _combat.SetInCombatMode(ent, true, combat);

        EnsureGun(ent, ent.Comp);

        var xform = Transform(ent);
        var pos = _transform.GetWorldPosition(xform);
        FixInWall(ent, ent.Comp, xform, ref pos);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<UnbalancedComponent, TransformComponent, InputMoverComponent>();

        while (query.MoveNext(out var uid, out var comp, out var xform, out var mover))
        {
            if (_mobState.IsDead(uid))
            {
                comp.MoveDir = Vector2.Zero;
                ApplyMovement(uid, comp, mover);
                continue;
            }

            if (now >= comp.NextRegen)
            {
                comp.NextRegen = now + TimeSpan.FromSeconds(comp.RegenInterval);
                Regenerate(uid);
            }

            if (!_mobState.IsAlive(uid))
            {
                comp.MoveDir = Vector2.Zero;
                ApplyMovement(uid, comp, mover);
                continue;
            }

            if (now >= comp.NextThink)
            {
                comp.NextThink = now + TimeSpan.FromSeconds(ThinkInterval);
                Think(uid, comp, xform, now);
            }

            ApplyMovement(uid, comp, mover);
        }
    }

    private void ApplyMovement(EntityUid uid, UnbalancedComponent comp, InputMoverComponent mover)
    {
        var input = comp.MoveDir;
        if (input != Vector2.Zero)
            input = (-_mover.GetParentGridAngle(mover)).RotateVec(input);

        mover.CurTickSprintMovement = Vector2.Zero;
        mover.CurTickWalkMovement = input;
        mover.LastInputTick = _timing.CurTick;
        mover.LastInputSubTick = ushort.MaxValue;

        var moving = comp.MoveDir != Vector2.Zero;
        if (moving == comp.WasMoving)
            return;

        comp.WasMoving = moving;
        var ev = new SpriteMoveEvent(moving);
        RaiseLocalEvent(uid, ref ev);
    }

    private void Regenerate(EntityUid uid)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable))
            return;

        var heal = new DamageSpecifier();
        var current = _damageable.GetPositiveDamage((uid, damageable));
        foreach (var (type, value) in current.DamageDict)
        {
            if (value <= FixedPoint2.Zero)
                continue;

            heal.DamageDict[type] = -FixedPoint2.Min(value, FixedPoint2.New(1));
        }

        if (heal.Empty)
            return;

        _damageable.TryChangeDamage(uid, heal, true, false, ignoreGlobalModifiers: true);
    }

    private Entity<GunComponent>? EnsureGun(EntityUid uid, UnbalancedComponent comp)
    {
        if (_gun.TryGetGun(uid, out var held))
        {
            comp.Gun = held.Owner;
            return held;
        }

        if (comp.Gun is not { } gunUid || !Exists(gunUid))
        {
            gunUid = Spawn(comp.GunProto, Transform(uid).Coordinates);
            comp.Gun = gunUid;
        }

        _hands.TryForcePickupAnyHand(uid, gunUid, checkActionBlocker: false);

        return _gun.TryGetGun(uid, out held) ? held : null;
    }

    private void Think(EntityUid uid, UnbalancedComponent comp, TransformComponent xform, TimeSpan now)
    {
        if (xform.MapID == MapId.Nullspace)
        {
            comp.MoveDir = Vector2.Zero;
            return;
        }

        if (EnsureGun(uid, comp) is not { } gun)
        {
            comp.MoveDir = Vector2.Zero;
            return;
        }

        var pos = _transform.GetWorldPosition(xform);
        FixInWall(uid, comp, xform, ref pos);

        if (TryShootLiving(uid, comp, gun, xform, pos))
        {
            comp.MoveDir = Vector2.Zero;
            return;
        }

        Patrol(uid, comp, gun, xform, pos, now);
    }

    private bool TryShootLiving(EntityUid uid, UnbalancedComponent comp, Entity<GunComponent> gun, TransformComponent xform, Vector2 pos)
    {
        var map = xform.MapID;
        EntityUid? best = null;
        var bestPos = Vector2.Zero;
        var bestDist = float.MaxValue;

        var flags = LookupFlags.Dynamic | LookupFlags.Uncontained;
        foreach (var (target, _) in _lookup.GetEntitiesInRange<MobStateComponent>(new MapCoordinates(pos, map), comp.ShootRange, flags))
        {
            if (target == uid || _mobState.IsDead(target))
                continue;

            var targetPos = _transform.GetWorldPosition(target);
            var dist = (targetPos - pos).Length();
            if (dist >= bestDist)
                continue;

            if (dist > 0.2f)
            {
                var dir = (targetPos - pos) / dist;
                if (Cast(null, map, pos, dir, dist - 0.05f, SightMask, uid, out _, out _))
                    continue;
            }

            best = target;
            bestPos = targetPos;
            bestDist = dist;
        }

        if (best == null)
            return false;

        Shoot(uid, gun, xform, pos, bestPos, best);
        return true;
    }

    private bool Shoot(EntityUid uid, Entity<GunComponent> gun, TransformComponent xform, Vector2 from, Vector2 at, EntityUid? target)
    {
        var delta = at - from;
        if (delta.LengthSquared() > 0.0001f)
            _transform.SetWorldRotation(uid, delta.ToWorldAngle());

        if (!_gun.CanShoot(gun.Comp))
            return false;

        EntityCoordinates coords;
        if (_maps.TryFindGridAt(xform.MapID, at, out var gridUid, out var grid))
            coords = new EntityCoordinates(gridUid, _maps.WorldToLocal(gridUid, grid, at));
        else if (xform.MapUid is { } mapUid)
            coords = new EntityCoordinates(mapUid, at);
        else
            return false;

        return _gun.AttemptShoot(uid, gun, coords, target);
    }

    private void Patrol(EntityUid uid, UnbalancedComponent comp, Entity<GunComponent> gun, TransformComponent xform, Vector2 pos, TimeSpan now)
    {
        var map = xform.MapID;

        if (!_maps.TryFindGridAt(map, pos, out var gridUid, out var grid))
        {
            comp.MoveDir = Vector2.Zero;
            return;
        }

        if (comp.Grid != gridUid)
        {
            ResetMemory(comp);
            comp.Grid = gridUid;
        }

        var tile = _maps.WorldToTile(gridUid, grid, pos);
        MarkVisited(comp, tile);
        CheckStuck(comp, pos, now);

        if (comp.Obstacle != null)
        {
            WorkObstacle(uid, comp, gun, xform, pos, now);
            return;
        }

        if (now < comp.NextAction)
        {
            comp.MoveDir = Vector2.Zero;
            return;
        }

        while (comp.Path.Count > 0 && (TileCenter(gridUid, grid, comp.Path[0]) - pos).LengthSquared() < WaypointRadiusSq)
            comp.Path.RemoveAt(0);

        if (comp.Path.Count == 0 && !Plan(comp, gridUid, grid, tile))
        {
            comp.Visited.Clear();
            comp.MoveDir = Vector2.Zero;
            comp.NextAction = now + TimeSpan.FromSeconds(2);
            return;
        }

        if (comp.Path.Count == 0)
        {
            comp.MoveDir = Vector2.Zero;
            return;
        }

        var target = TileCenter(gridUid, grid, comp.Path[0]);
        var delta = target - pos;
        var distToTarget = delta.Length();
        if (distToTarget < 0.01f)
        {
            comp.Path.RemoveAt(0);
            return;
        }

        var moveDir = delta / distToTarget;
        comp.MoveDir = moveDir;
        _transform.SetWorldRotation(uid, moveDir.ToWorldAngle());

        var probeLen = MathF.Min(distToTarget + 0.6f, 2.2f);
        if (!Probe(comp, map, pos, moveDir, probeLen, uid, out var hitDist, out var hit))
            return;

        var isDoor = TryComp<DoorComponent>(hit, out var door);
        var isStorage = TryComp<EntityStorageComponent>(hit, out var storage);
        var reach = isDoor || isStorage ? OpenReach : WallStop;

        if (hitDist > reach)
            return;

        comp.MoveDir = Vector2.Zero;

        if (isDoor && door != null)
        {
            if (door.State == DoorState.Closed)
            {
                if (_door.TryOpen(hit, door, null, quiet: true))
                    comp.NextAction = now + TimeSpan.FromSeconds(0.5);
                else
                    BeginObstacle(comp, hit, true);
            }
            else if (door.State == DoorState.Welded)
            {
                BeginObstacle(comp, hit, true);
            }
            else
            {
                comp.NextAction = now + TimeSpan.FromSeconds(0.3);
            }

            return;
        }

        if (isStorage && storage != null)
        {
            if (storage.Open)
                comp.NextAction = now + TimeSpan.FromSeconds(0.3);
            else if (_storage.TryOpenStorage(uid, hit, true))
                comp.NextAction = now + TimeSpan.FromSeconds(0.5);
            else
                BeginObstacle(comp, hit, false);

            return;
        }

        BeginObstacle(comp, hit, false);
    }

    private void BeginObstacle(UnbalancedComponent comp, EntityUid obstacle, bool infinite)
    {
        comp.Obstacle = obstacle;
        comp.ObstacleShots = 0;
        comp.ObstacleInfinite = infinite;
        comp.LastShot = TimeSpan.Zero;
        comp.MoveDir = Vector2.Zero;
    }

    private void ClearObstacle(UnbalancedComponent comp)
    {
        comp.Obstacle = null;
        comp.ObstacleShots = 0;
        comp.ObstacleInfinite = false;
    }

    private void WorkObstacle(EntityUid uid, UnbalancedComponent comp, Entity<GunComponent> gun, TransformComponent xform, Vector2 pos, TimeSpan now)
    {
        comp.MoveDir = Vector2.Zero;

        var obstacle = comp.Obstacle!.Value;
        if (!Exists(obstacle) || TerminatingOrDeleted(obstacle))
        {
            ClearObstacle(comp);
            comp.Path.Clear();
            return;
        }

        if (TryComp<DoorComponent>(obstacle, out var door))
        {
            if (door.State is DoorState.Open or DoorState.Opening)
            {
                ClearObstacle(comp);
                comp.NextAction = now + TimeSpan.FromSeconds(0.4);
                return;
            }

            if (door.State == DoorState.Closed && _door.TryOpen(obstacle, door, null, quiet: true))
            {
                ClearObstacle(comp);
                comp.NextAction = now + TimeSpan.FromSeconds(0.5);
                return;
            }
        }
        else if (TryComp<EntityStorageComponent>(obstacle, out var storage) && storage.Open)
        {
            ClearObstacle(comp);
            return;
        }

        var obstaclePos = _transform.GetWorldPosition(obstacle);
        if ((obstaclePos - pos).Length() > 3f)
        {
            ClearObstacle(comp);
            comp.Path.Clear();
            return;
        }

        if (!comp.ObstacleInfinite && comp.ObstacleShots >= comp.ObstacleShotLimit)
        {
            if (now - comp.LastShot < TimeSpan.FromSeconds(0.8))
                return;

            comp.UnbreakableEnts.Add(obstacle);
            if (_maps.TryFindGridAt(xform.MapID, obstaclePos, out var gridUid, out var grid))
                comp.Unbreakable.Add(_maps.WorldToTile(gridUid, grid, obstaclePos));

            ClearObstacle(comp);
            comp.Path.Clear();
            comp.Goal = null;
            return;
        }

        if (Shoot(uid, gun, xform, pos, obstaclePos, obstacle))
        {
            comp.ObstacleShots++;
            comp.LastShot = now;
        }
    }

    private void CheckStuck(UnbalancedComponent comp, Vector2 pos, TimeSpan now)
    {
        if (now < comp.NextStuckCheck)
            return;

        comp.NextStuckCheck = now + TimeSpan.FromSeconds(2);

        if (comp.MoveDir != Vector2.Zero && comp.Obstacle == null && (pos - comp.LastPos).Length() < 0.2f)
        {
            comp.StuckCount++;
            if (comp.StuckCount >= 2)
            {
                if (comp.Goal is { } goal)
                    comp.Visited.Add(goal);

                comp.Path.Clear();
                comp.Goal = null;
                comp.StuckCount = 0;
            }
        }
        else
        {
            comp.StuckCount = 0;
        }

        comp.LastPos = pos;
    }

    private void ResetMemory(UnbalancedComponent comp)
    {
        comp.Visited.Clear();
        comp.Unbreakable.Clear();
        comp.UnbreakableEnts.Clear();
        comp.Path.Clear();
        comp.Goal = null;
        comp.Obstacle = null;
        comp.ObstacleShots = 0;
        comp.ObstacleInfinite = false;
    }

    private void MarkVisited(UnbalancedComponent comp, Vector2i tile)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                comp.Visited.Add(new Vector2i(tile.X + dx, tile.Y + dy));
            }
        }
    }

    private Vector2 TileCenter(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        return _transform.ToMapCoordinates(_maps.GridTileToLocal(gridUid, grid, tile)).Position;
    }

    private bool IsBlocking(EntityUid uid)
    {
        if (_physicsQuery.TryGetComponent(uid, out var physics) && !physics.CanCollide)
            return false;

        if (!_fixturesQuery.TryGetComponent(uid, out var fixtures))
            return false;

        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (fixture.Hard && (fixture.CollisionLayer & BlockMask) != 0)
                return true;
        }

        return false;
    }

    private bool TileSolid(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        foreach (var anchored in _maps.GetAnchoredEntities(gridUid, grid, tile))
        {
            if (IsBlocking(anchored))
                return true;
        }

        return false;
    }

    private float TileCost(UnbalancedComponent comp, EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        if (comp.Unbreakable.Contains(tile))
            return -1f;

        if (!_maps.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
            return -1f;

        var cost = 1f;
        foreach (var anchored in _maps.GetAnchoredEntities(gridUid, grid, tile))
        {
            if (!IsBlocking(anchored))
                continue;

            if (comp.UnbreakableEnts.Contains(anchored))
                return -1f;

            if (HasComp<DoorComponent>(anchored) || HasComp<EntityStorageComponent>(anchored))
                cost = MathF.Max(cost, OpenableCost);
            else
                cost = MathF.Max(cost, WallCost);
        }

        return cost;
    }

    private void FixInWall(EntityUid uid, UnbalancedComponent comp, TransformComponent xform, ref Vector2 pos)
    {
        if (!_maps.TryFindGridAt(xform.MapID, pos, out var gridUid, out var grid))
            return;

        var tile = _maps.WorldToTile(gridUid, grid, pos);
        if (!TileSolid(gridUid, grid, tile))
            return;

        for (var radius = 1; radius <= 8; radius++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius)
                        continue;

                    var candidate = new Vector2i(tile.X + dx, tile.Y + dy);
                    if (!_maps.TryGetTileRef(gridUid, grid, candidate, out var tileRef) || tileRef.Tile.IsEmpty)
                        continue;

                    if (TileSolid(gridUid, grid, candidate))
                        continue;

                    _transform.SetCoordinates(uid, _maps.GridTileToLocal(gridUid, grid, candidate));
                    pos = _transform.GetWorldPosition(uid);
                    comp.Path.Clear();
                    comp.Goal = null;
                    ClearObstacle(comp);
                    return;
                }
            }
        }
    }

    private bool Plan(UnbalancedComponent comp, EntityUid gridUid, MapGridComponent grid, Vector2i start)
    {
        comp.Path.Clear();
        comp.Goal = null;

        var costs = new Dictionary<Vector2i, float>();
        var best = new Dictionary<Vector2i, float> { [start] = 0f };
        var parent = new Dictionary<Vector2i, Vector2i>();
        var closed = new HashSet<Vector2i>();
        var open = new PriorityQueue<Vector2i, float>();
        open.Enqueue(start, 0f);

        Vector2i? goal = null;
        var budget = PlanBudget;

        while (budget-- > 0 && open.TryDequeue(out var current, out var currentCost))
        {
            if (!closed.Add(current))
                continue;

            if (current != start && !comp.Visited.Contains(current) && GetCost(current) <= 1f)
            {
                goal = current;
                break;
            }

            foreach (var step in Dirs)
            {
                var next = current + step;
                if (closed.Contains(next))
                    continue;

                var cost = GetCost(next);
                if (cost < 0f)
                    continue;

                var total = currentCost + cost;
                if (best.TryGetValue(next, out var old) && old <= total)
                    continue;

                best[next] = total;
                parent[next] = current;
                open.Enqueue(next, total);
            }
        }

        if (goal is not { } found)
            return false;

        var cursor = found;
        while (cursor != start)
        {
            comp.Path.Add(cursor);
            cursor = parent[cursor];
        }

        comp.Path.Reverse();
        comp.Goal = found;
        return true;

        float GetCost(Vector2i tile)
        {
            if (costs.TryGetValue(tile, out var cached))
                return cached;

            var value = TileCost(comp, gridUid, grid, tile);
            costs[tile] = value;
            return value;
        }
    }

    private bool Probe(UnbalancedComponent comp, MapId map, Vector2 pos, Vector2 dir, float len, EntityUid self, out float dist, out EntityUid hit)
    {
        dist = float.MaxValue;
        hit = default;
        var found = false;
        var side = new Vector2(-dir.Y, dir.X) * SideOffset;

        for (var i = -1; i <= 1; i++)
        {
            var origin = pos + side * i;
            if (Cast(comp.UnbreakableEnts, map, origin, dir, len, BlockMask, self, out var d, out var h) && d < dist)
            {
                dist = d;
                hit = h;
                found = true;
            }
        }

        return found;
    }

    private bool Cast(HashSet<EntityUid>? skip, MapId map, Vector2 from, Vector2 dir, float len, int mask, EntityUid self, out float dist, out EntityUid hit)
    {
        dist = len;
        hit = default;

        if (len < 0.05f)
            return false;

        var found = false;
        var ray = new CollisionRay(from, dir, mask);
        foreach (var result in _physics.IntersectRay(map, ray, len, self, false))
        {
            var entity = result.HitEntity;

            if (HasComp<MobStateComponent>(entity))
                continue;

            if (skip != null && skip.Contains(entity))
                continue;

            if (TryComp<DoorComponent>(entity, out var door) && door.State == DoorState.Open)
                continue;

            if (TryComp<EntityStorageComponent>(entity, out var storage) && storage.Open)
                continue;

            if (found && result.Distance >= dist)
                continue;

            found = true;
            dist = result.Distance;
            hit = entity;
        }

        return found;
    }
}
