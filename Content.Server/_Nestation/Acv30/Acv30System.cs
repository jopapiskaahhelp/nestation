// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Atmos.Components;
using Content.Shared.Destructible;
using Content.Shared.Ghost.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.Acv30;

public sealed partial class Acv30System : EntitySystem
{
    private static readonly TimeSpan CrushSoundCooldown = TimeSpan.FromSeconds(0.35);

    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDestructibleSystem _destructible = default!;
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPointLightSystem _light = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly HashSet<Entity<TransformComponent>> _nearby = new();
    private readonly HashSet<Entity<FlammableComponent>> _burnable = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Acv30ZoneComponent, MapInitEvent>(OnZoneMapInit);
        SubscribeLocalEvent<Acv30WreckComponent, ComponentShutdown>(OnWreckShutdown);
    }

    private void OnZoneMapInit(Entity<Acv30ZoneComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Kind != Acv30ZoneKind.Spawn || !ent.Comp.AutoSpawn)
            return;

        ent.Comp.NextSpawn = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.SpawnDelay);
    }

    private void OnWreckShutdown(Entity<Acv30WreckComponent> ent, ref ComponentShutdown args)
    {
        foreach (var spawned in ent.Comp.Spawned)
        {
            if (Exists(spawned))
                QueueDel(spawned);
        }

        ent.Comp.Spawned.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        UpdateSpawners(now);
        UpdateVehicles(frameTime, now);
        UpdateWrecks(now);
    }

    private void UpdateSpawners(TimeSpan now)
    {
        var query = EntityQueryEnumerator<Acv30ZoneComponent>();
        while (query.MoveNext(out var uid, out var zone))
        {
            if (zone.Kind != Acv30ZoneKind.Spawn || zone.NextSpawn is not { } next || now < next)
                continue;

            zone.NextSpawn = null;
            SpawnVehicle(uid, zone);
        }
    }

    public int SpawnAll(string? group)
    {
        var count = 0;
        var query = EntityQueryEnumerator<Acv30ZoneComponent>();
        while (query.MoveNext(out var uid, out var zone))
        {
            if (zone.Kind != Acv30ZoneKind.Spawn || group != null && zone.Group != group)
                continue;

            zone.NextSpawn = null;

            if (SpawnVehicle(uid, zone) != null)
                count++;
        }

        return count;
    }

    public int DetonateAll()
    {
        var count = 0;
        var query = EntityQueryEnumerator<Acv30VehicleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var vehicle, out var xform))
        {
            var (pos, rot) = _transform.GetWorldPositionRotation(xform);
            Detonate(uid, vehicle, xform, pos, rot);
            count++;
        }

        return count;
    }

    private EntityUid? SpawnVehicle(EntityUid zoneUid, Acv30ZoneComponent zone)
    {
        var coords = _transform.GetMapCoordinates(zoneUid);
        if (coords.MapId == MapId.Nullspace)
            return null;

        var route = BuildRoute(zone.Group, coords.MapId);
        var rotation = _transform.GetWorldRotation(zoneUid);

        if (route.Count > 0)
        {
            var direction = _transform.GetWorldPosition(route[0]) - coords.Position;
            if (direction.LengthSquared() > 0.01f)
                rotation = direction.ToWorldAngle();
        }

        var vehicle = Spawn(zone.Vehicle, coords, rotation: rotation);

        var comp = EnsureComp<Acv30VehicleComponent>(vehicle);
        comp.Group = zone.Group;
        comp.Route = route;
        comp.NextShot = _timing.CurTime;

        zone.Spawned = vehicle;
        return vehicle;
    }

    private List<EntityUid> BuildRoute(string group, MapId map)
    {
        var points = new List<(int Index, EntityUid Uid)>();
        var query = EntityQueryEnumerator<Acv30ZoneComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var zone, out var xform))
        {
            if (zone.Kind != Acv30ZoneKind.Route || zone.Group != group || xform.MapID != map)
                continue;

            points.Add((zone.Index, uid));
        }

        points.Sort((a, b) => a.Index.CompareTo(b.Index));

        var route = new List<EntityUid>(points.Count);
        foreach (var point in points)
        {
            route.Add(point.Uid);
        }

        return route;
    }

    private bool IsInside(string group, Acv30ZoneKind kind, MapId map, Vector2 pos)
    {
        var query = EntityQueryEnumerator<Acv30ZoneComponent, TransformComponent>();
        while (query.MoveNext(out _, out var zone, out var xform))
        {
            if (zone.Kind != kind || zone.Group != group || xform.MapID != map)
                continue;

            var (zonePos, zoneRot) = _transform.GetWorldPositionRotation(xform);
            var local = new Angle(-zoneRot.Theta).RotateVec(pos - zonePos);
            var half = zone.Size / 2f;

            if (MathF.Abs(local.X) <= half.X && MathF.Abs(local.Y) <= half.Y)
                return true;
        }

        return false;
    }

    private void UpdateVehicles(float frameTime, TimeSpan now)
    {
        var query = EntityQueryEnumerator<Acv30VehicleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var vehicle, out var xform))
        {
            if (xform.MapID == MapId.Nullspace)
                continue;

            vehicle.Route ??= BuildRoute(vehicle.Group, xform.MapID);

            if (vehicle.Route.Count == 0)
                continue;

            var map = xform.MapID;
            var (pos, rot) = _transform.GetWorldPositionRotation(xform);

            Vector2? target = null;
            while (vehicle.Waypoint < vehicle.Route.Count)
            {
                var waypoint = vehicle.Route[vehicle.Waypoint];

                if (!Exists(waypoint))
                {
                    vehicle.Waypoint++;
                    continue;
                }

                var waypointPos = _transform.GetWorldPosition(waypoint);
                if ((waypointPos - pos).Length() <= vehicle.ReachDistance)
                {
                    vehicle.Waypoint++;
                    continue;
                }

                target = waypointPos;
                break;
            }

            if (target is not { } destination)
            {
                Detonate(uid, vehicle, xform, pos, rot);
                continue;
            }

            var desired = (destination - pos).ToWorldAngle();
            var diff = MathF.IEEERemainder((float) (desired.Theta - rot.Theta), MathF.Tau);
            var maxTurn = vehicle.TurnRate * frameTime;
            var turn = Math.Clamp(diff, -maxTurn, maxTurn);
            rot = new Angle(rot.Theta + turn);

            var speed = MathF.Abs(diff) - MathF.Abs(turn) > vehicle.PivotAngle ? 0f : vehicle.Speed;

            pos += rot.ToWorldVec() * speed * frameTime;
            _transform.SetWorldPositionRotation(uid, pos, rot, xform);

            if (IsInside(vehicle.Group, Acv30ZoneKind.Detonation, map, pos))
            {
                Detonate(uid, vehicle, xform, pos, rot);
                continue;
            }

            if (IsInside(vehicle.Group, Acv30ZoneKind.Fire, map, pos))
                Shoot(uid, vehicle, map, pos, rot, now);
            else
                vehicle.NextShot = now;

            Crush(uid, vehicle, map, pos, rot, now);
        }
    }

    private void Shoot(EntityUid uid, Acv30VehicleComponent vehicle, MapId map, Vector2 pos, Angle rot, TimeSpan now)
    {
        var interval = TimeSpan.FromSeconds(1f / vehicle.FireRate);
        var forward = rot.ToWorldVec();
        var shots = 0;

        while (vehicle.NextShot <= now && shots < 3)
        {
            var direction = forward;

            if (vehicle.SpreadDegrees > 0f)
            {
                var spread = Angle.FromDegrees(_random.NextFloat(-vehicle.SpreadDegrees, vehicle.SpreadDegrees));
                direction = spread.RotateVec(forward);
            }

            var muzzle = pos + forward * vehicle.MuzzleOffset;
            var projectile = Spawn(vehicle.Projectile, new MapCoordinates(muzzle, map));

            _gun.ShootProjectile(projectile, direction, forward * vehicle.Speed, uid, uid, vehicle.ProjectileSpeed);
            _audio.PlayPvs(vehicle.ShootSound, uid);

            vehicle.NextShot += interval;
            shots++;
        }

        if (vehicle.NextShot < now)
            vehicle.NextShot = now;
    }

    private void Crush(EntityUid uid, Acv30VehicleComponent vehicle, MapId map, Vector2 pos, Angle rot, TimeSpan now)
    {
        var forward = rot.ToWorldVec();
        var right = new Vector2(forward.Y, -forward.X);
        var halfWidth = vehicle.Hull.X / 2f;
        var halfLength = vehicle.Hull.Y / 2f;
        var reach = MathF.Max(vehicle.FrontReach, vehicle.CrushMargin);
        var radius = MathF.Sqrt(halfWidth * halfWidth + halfLength * halfLength) + vehicle.FrontMargin + reach;

        _nearby.Clear();
        _lookup.GetEntitiesIntersecting(
            map,
            Box2.CenteredAround(pos, new Vector2(radius * 2f, radius * 2f)),
            _nearby,
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries);

        var crushed = false;

        foreach (var (ent, entXform) in _nearby)
        {
            if (ent == uid
                || HasComp<MapGridComponent>(ent)
                || HasComp<MapComponent>(ent)
                || HasComp<Acv30VehicleComponent>(ent)
                || HasComp<Acv30WreckComponent>(ent)
                || HasComp<Acv30EffectComponent>(ent)
                || HasComp<ProjectileComponent>(ent)
                || HasComp<GhostComponent>(ent))
            {
                continue;
            }

            var offset = _transform.GetWorldPosition(entXform) - pos;
            var localX = Vector2.Dot(offset, right);
            var localY = Vector2.Dot(offset, forward);

            if (HasComp<MobStateComponent>(ent))
            {
                var inFront = localY > halfLength - vehicle.FrontDepth
                              && localY <= halfLength + vehicle.FrontReach
                              && MathF.Abs(localX) <= halfWidth + vehicle.FrontMargin;

                if (inFront)
                    PushAside(ent, localX, right, vehicle.FrontPushSpeed);
                else if (MathF.Abs(localX) <= halfWidth && MathF.Abs(localY) <= halfLength)
                    PushAside(ent, localX, right, vehicle.PushSpeed);

                continue;
            }

            if (HasComp<ActorComponent>(ent))
                continue;

            if (MathF.Abs(localX) > halfWidth + vehicle.CrushMargin || MathF.Abs(localY) > halfLength + vehicle.CrushMargin)
                continue;

            if (!vehicle.CrushStructures && entXform.Anchored)
                continue;

            if (_destructible.DestroyEntity(ent))
                crushed = true;
        }

        if (!crushed || now < vehicle.NextCrushSound)
            return;

        vehicle.NextCrushSound = now + CrushSoundCooldown;
        _audio.PlayPvs(vehicle.CrushSound, uid);
    }

    private void PushAside(EntityUid mob, float localX, Vector2 right, float pushSpeed)
    {
        if (!TryComp<PhysicsComponent>(mob, out var body) || body.BodyType == BodyType.Static)
            return;

        var side = MathF.Abs(localX) < 0.05f
            ? (mob.Id % 2 == 0 ? 1f : -1f)
            : MathF.Sign(localX);

        var direction = right * side;
        var current = Vector2.Dot(body.LinearVelocity, direction);

        if (current >= pushSpeed)
            return;

        _physics.ApplyLinearImpulse(mob, direction * (pushSpeed - current) * body.Mass, body: body);
    }

    private void Detonate(EntityUid uid, Acv30VehicleComponent vehicle, TransformComponent xform, Vector2 pos, Angle rot)
    {
        var coords = new MapCoordinates(pos, xform.MapID);

        var projectiles = EntityQueryEnumerator<ProjectileComponent>();
        while (projectiles.MoveNext(out var projectileUid, out var projectile))
        {
            if (projectile.Shooter != uid && projectile.Weapon != uid)
                continue;

            projectile.Shooter = null;
            projectile.Weapon = null;
            Dirty(projectileUid, projectile);
        }

        var wreck = Spawn(vehicle.Wreck, coords, rotation: rot);

        _audio.PlayPvs(vehicle.DetonationSound, xform.Coordinates);
        _explosion.QueueExplosion(
            coords,
            vehicle.ExplosionType,
            vehicle.ExplosionIntensity,
            vehicle.ExplosionSlope,
            vehicle.ExplosionMaxTileIntensity,
            wreck);

        QueueDel(uid);
    }

    private void UpdateWrecks(TimeSpan now)
    {
        var query = EntityQueryEnumerator<Acv30WreckComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var wreck, out var xform))
        {
            if (!wreck.Started)
            {
                wreck.Started = true;
                StartWreck(uid, wreck, xform);
            }

            if (now >= wreck.NextFlicker)
            {
                wreck.NextFlicker = now + TimeSpan.FromSeconds(wreck.FlickerInterval);

                if (TryComp<PointLightComponent>(uid, out var light))
                    _light.SetEnergy(uid, _random.NextFloat(wreck.EnergyMin, wreck.EnergyMax), light);
            }

            if (now < wreck.NextIgnite)
                continue;

            wreck.NextIgnite = now + TimeSpan.FromSeconds(wreck.IgniteInterval);
            IgniteNearby(wreck, xform);
        }
    }

    private void StartWreck(EntityUid uid, Acv30WreckComponent wreck, TransformComponent xform)
    {
        var (pos, rot) = _transform.GetWorldPositionRotation(xform);
        var forward = rot.ToWorldVec();
        var right = new Vector2(forward.Y, -forward.X);
        var map = xform.MapID;

        foreach (var slot in wreck.Effects)
        {
            var at = pos + right * slot.Offset.X + forward * slot.Offset.Y;
            wreck.Spawned.Add(Spawn(slot.Proto, new MapCoordinates(at, map)));
        }

        if (wreck.FireAmbience is { } ambience)
            wreck.Spawned.Add(Spawn(ambience, new MapCoordinates(pos, map)));

        if (wreck.ScreamSound == null)
            return;

        var param = AudioParams.Default
            .WithMaxDistance(wreck.ScreamRange)
            .WithRolloffFactor(wreck.ScreamRolloff)
            .WithVolume(wreck.ScreamVolume);

        _audio.PlayPvs(wreck.ScreamSound, uid, param);
    }

    private void IgniteNearby(Acv30WreckComponent wreck, TransformComponent xform)
    {
        var (pos, rot) = _transform.GetWorldPositionRotation(xform);
        var forward = rot.ToWorldVec();
        var right = new Vector2(forward.Y, -forward.X);
        var halfWidth = wreck.Hull.X / 2f + wreck.IgniteMargin;
        var halfLength = wreck.Hull.Y / 2f + wreck.IgniteMargin;
        var radius = MathF.Sqrt(halfWidth * halfWidth + halfLength * halfLength);

        _burnable.Clear();
        _lookup.GetEntitiesIntersecting(
            xform.MapID,
            Box2.CenteredAround(pos, new Vector2(radius * 2f, radius * 2f)),
            _burnable,
            LookupFlags.Dynamic);

        foreach (var (ent, flammable) in _burnable)
        {
            if (!HasComp<MobStateComponent>(ent))
                continue;

            var offset = _transform.GetWorldPosition(ent) - pos;
            if (MathF.Abs(Vector2.Dot(offset, right)) > halfWidth || MathF.Abs(Vector2.Dot(offset, forward)) > halfLength)
                continue;

            _flammable.SetFireStacks(ent, MathF.Max(flammable.FireStacks, wreck.IgniteStacks), flammable, true);
        }
    }
}
