// Nestation new city CC BY-NC 3.0
using System.Linq;
using System.Numerics;
using Content.Server.Decals;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.Decals;
using Content.Shared.Destructible;
using Content.Shared.GameTicking;
using Content.Shared.Gibbing;
using Content.Shared.Hands.Components;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.XenoTurret;

public sealed partial class XenoTurretSystem : EntitySystem
{
    private static readonly string[] Splats = { "NestationXenoToxinSplatA", "NestationXenoToxinSplatB", "NestationXenoToxinSplatC" };
    private static readonly string[] Streaks = { "NestationXenoToxinStreakA", "NestationXenoToxinStreakB", "NestationXenoToxinStreakC" };
    private static readonly string[] Dots = { "NestationXenoToxinDotA", "NestationXenoToxinDotB", "NestationXenoToxinDotC" };

    private static readonly ProtoId<OrganCategoryPrototype>[] LimbCategories =
    {
        "ArmLeft",
        "ArmRight",
        "LegLeft",
        "LegRight",
        "Head",
    };

    private static readonly ProtoId<OrganCategoryPrototype> TorsoCategory = "Torso";

    private const float WallPadding = 0.12f;
    private const float DecalCenterOffset = 0.5f;
    private const float StreakHalfLength = 0.4f;

    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private SharedDestructibleSystem _destructible = default!;
    [Dependency] private DetachableOrganSystem _detachable = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private GibbingSystem _gibbing = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly List<PendingKill> _pending = new();
    private readonly List<FlyingPart> _flying = new();
    private readonly HashSet<EntityUid> _busy = new();
    private readonly Queue<(EntityUid Grid, DecalIndex Index, TimeSpan Expire)> _decalQueue = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<XenoTurretComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<XenoTurretComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
        SubscribeLocalEvent<XenoTurretShotComponent, ProjectileHitEvent>(OnShotHit);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnMapInit(Entity<XenoTurretComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextFire = _timing.CurTime + ent.Comp.Interval;
    }

    private void OnBeforeDamageChanged(Entity<XenoTurretComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Origin is not { } origin || origin == ent.Owner)
            return;

        if (HasComp<NpcFactionMemberComponent>(origin) && _faction.IsEntityFriendly(ent.Owner, origin))
            args.Cancelled = true;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _pending.Clear();
        _flying.Clear();
        _busy.Clear();
        _decalQueue.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        ScanTurrets(now);
        ProcessPending();
        UpdateFlying(frameTime);
        ExpireDecals(now);
    }

    private void ScanTurrets(TimeSpan now)
    {
        var query = EntityQueryEnumerator<XenoTurretComponent>();
        while (query.MoveNext(out var uid, out var turret))
        {
            if (now < turret.NextFire || now < turret.NextScan)
                continue;

            turret.NextScan = now + turret.ScanInterval;

            if (!TryFindTarget((uid, turret), out var target))
                continue;

            Fire((uid, turret), target);
            turret.NextFire = now + turret.Interval;
        }
    }

    private bool TryFindTarget(Entity<XenoTurretComponent> turret, out EntityUid target)
    {
        target = default;

        var origin = _transform.GetWorldPosition(turret.Owner);
        var best = float.MaxValue;
        var found = false;

        foreach (var candidate in _faction.GetNearbyHostiles(new Entity<NpcFactionMemberComponent?, FactionExceptionComponent?>(turret.Owner, null, null), turret.Comp.Range))
        {
            if (_busy.Contains(candidate) || !_mobState.IsAlive(candidate))
                continue;

            var distance = (_transform.GetWorldPosition(candidate) - origin).Length();

            if (distance > turret.Comp.Range || distance >= best)
                continue;

            if (!_interaction.InRangeUnobstructed(turret.Owner, candidate, turret.Comp.Range + 0.5f))
                continue;

            best = distance;
            target = candidate;
            found = true;
        }

        return found;
    }

    private void Fire(Entity<XenoTurretComponent> turret, EntityUid target)
    {
        var origin = _transform.GetWorldPosition(turret.Owner);
        var direction = _transform.GetWorldPosition(target) - origin;

        if (direction.LengthSquared() < 0.0001f)
            return;

        var normal = direction.Normalized();
        var coordinates = _transform.GetMapCoordinates(turret.Owner);
        var projectile = Spawn(turret.Comp.Projectile, new MapCoordinates(coordinates.Position + normal * turret.Comp.MuzzleOffset, coordinates.MapId));

        var shot = EnsureComp<XenoTurretShotComponent>(projectile);
        shot.Turret = turret.Owner;
        shot.Direction = normal;

        _gun.ShootProjectile(projectile, normal, Vector2.Zero, turret.Owner, turret.Owner, turret.Comp.ProjectileSpeed);
        _audio.PlayPvs(turret.Comp.ShootSound, turret.Owner);
    }

    private void OnShotHit(Entity<XenoTurretShotComponent> shot, ref ProjectileHitEvent args)
    {
        var target = args.Target;
        var turret = shot.Comp.Turret;

        if (turret == null || !TryComp<XenoTurretComponent>(turret, out var config))
            return;

        if (!IsValidVictim(turret.Value, target))
        {
            var impact = _transform.GetMapCoordinates(shot.Owner);
            Spawn(config.BurstLight, impact);
            _audio.PlayPvs(config.BurstSound, _transform.ToCoordinates(impact));
            return;
        }

        _busy.Add(target);
        _pending.Add(new PendingKill
        {
            Target = target,
            Config = config,
            Direction = shot.Comp.Direction,
        });
    }

    private bool IsValidVictim(EntityUid turret, EntityUid target)
    {
        if (_busy.Contains(target) || !HasComp<MobStateComponent>(target) || !_mobState.IsAlive(target))
            return false;

        if (!HasComp<NpcFactionMemberComponent>(target))
            return true;

        return !_faction.IsEntityFriendly(turret, target);
    }

    private void ProcessPending()
    {
        if (_pending.Count == 0)
            return;

        var batch = _pending.ToArray();
        _pending.Clear();

        foreach (var kill in batch)
        {
            Dismember(kill);
            _busy.Remove(kill.Target);
        }
    }

    private void Dismember(PendingKill kill)
    {
        var target = kill.Target;
        var config = kill.Config;

        if (Deleted(target))
            return;

        var xform = Transform(target);
        var map = xform.MapID;
        var grid = xform.GridUid;
        var coordinates = xform.Coordinates;
        var pos = _transform.GetWorldPosition(xform);
        var dir = kill.Direction;

        Spawn(config.BurstLight, new MapCoordinates(pos, map));
        _audio.PlayPvs(config.BurstSound, coordinates);

        foreach (var sound in PickGibSounds(config, target))
            _audio.PlayPvs(sound, coordinates);

        SprayDecals(config, map, grid, pos, dir, target);

        if (!HasComp<BodyComponent>(target))
        {
            _gibbing.Gib(target);
            return;
        }

        _mobState.ChangeMobState(target, MobState.Dead);

        foreach (var category in LimbCategories)
            TearOff(config, target, category, pos, dir, map, false);

        TearOff(config, target, TorsoCategory, pos, dir, map, true);

        var items = _inventory.GetHandOrInventoryEntities(new Entity<HandsComponent?, InventoryComponent?>(target, null, null)).ToList();
        foreach (var item in items)
        {
            _transform.DropNextTo(item, (target, Transform(target)));
        }

        if (!_destructible.DestroyEntity(target))
            QueueDel(target);
    }

    private List<SoundSpecifier> PickGibSounds(XenoTurretComponent config, EntityUid target)
    {
        if (TryComp<HumanoidProfileComponent>(target, out var profile)
            && config.GibSounds.TryGetValue(profile.Species, out var sounds))
        {
            return sounds;
        }

        return config.DefaultGibSounds;
    }

    private void TearOff(XenoTurretComponent config, EntityUid target, ProtoId<OrganCategoryPrototype> category, Vector2 pos, Vector2 dir, MapId map, bool torso)
    {
        var organ = FindOrgan(target, category);
        if (organ == null)
            return;

        var detached = _detachable.Detach(organ.Value);
        if (detached == null)
            return;

        var jitter = _random.NextAngle().ToVec() * _random.NextFloat(0.05f, 0.25f);
        _transform.SetWorldPosition(detached.Value, pos + jitter);
        _audio.PlayPvs(config.LimbTearSound, Transform(detached.Value).Coordinates);

        var heading = dir == Vector2.Zero ? _random.NextAngle().ToVec() : Rotate(dir, _random.NextFloat(-config.LimbSpread, config.LimbSpread));
        var speed = torso ? config.TorsoSpeed : _random.NextFloat(config.LimbSpeedMin, config.LimbSpeedMax);

        _flying.Add(new FlyingPart
        {
            Uid = detached.Value,
            Config = config,
            Map = map,
            Velocity = heading * speed,
            Spin = torso ? 0f : _random.NextFloat(-config.LimbSpin, config.LimbSpin),
        });
    }

    private EntityUid? FindOrgan(EntityUid target, ProtoId<OrganCategoryPrototype> category)
    {
        foreach (var organ in _body.EnumerateOrgans<DetachableOrganComponent>(target))
        {
            if (organ.Comp1.Category == category)
                return organ.Owner;
        }

        return null;
    }

    private void UpdateFlying(float frameTime)
    {
        for (var i = _flying.Count - 1; i >= 0; i--)
        {
            var part = _flying[i];

            if (Deleted(part.Uid))
            {
                _flying.RemoveAt(i);
                continue;
            }

            var config = part.Config;
            var pos = _transform.GetWorldPosition(part.Uid);
            var speed = part.Velocity.Length();
            var landed = speed < config.LimbMinSpeed;

            if (!landed)
            {
                var heading = part.Velocity / speed;
                var step = speed * frameTime;
                var wall = DistanceToWall(part.Map, pos, heading, step + WallPadding, part.Uid);

                if (wall < step + WallPadding)
                {
                    step = MathF.Max(0f, wall - WallPadding);
                    landed = true;
                }

                pos += heading * step;
                _transform.SetWorldPosition(part.Uid, pos);

                if (part.Spin != 0f && !landed)
                    _transform.SetWorldRotation(part.Uid, _transform.GetWorldRotation(part.Uid) + new Angle(part.Spin * frameTime));

                part.Trail += step;

                if (!landed && part.Trail >= config.TrailStep)
                {
                    part.Trail = 0f;
                    AddDecal(config, _random.Pick(Dots), Transform(part.Uid).GridUid, part.Map, pos, _random.NextAngle(), _random.NextFloat(0.75f, 1f));
                }

                part.Velocity *= MathF.Exp(-config.LimbDrag * frameTime);
            }

            if (!landed)
                continue;

            AddDecal(config, _random.Pick(Splats), Transform(part.Uid).GridUid, part.Map, pos, _random.NextAngle(), _random.NextFloat(0.85f, 1f));
            _flying.RemoveAt(i);
        }
    }

    private void SprayDecals(XenoTurretComponent config, MapId map, EntityUid? grid, Vector2 pos, Vector2 dir, EntityUid ignore)
    {
        if (grid == null)
            return;

        for (var i = 0; i < config.SplatCount; i++)
        {
            var offset = _random.NextAngle().ToVec() * _random.NextFloat(0f, 0.45f);
            AddDecal(config, _random.Pick(Splats), grid, map, pos + offset, _random.NextAngle(), _random.NextFloat(0.85f, 1f));
        }

        if (dir == Vector2.Zero)
            return;

        for (var i = 0; i < config.SprayCount; i++)
        {
            var shot = Rotate(dir, _random.NextFloat(-config.SprayArc, config.SprayArc));
            var distance = 0.5f + MathF.Pow(_random.NextFloat(), 1.15f) * (config.SprayReach - 0.5f);
            var need = distance + StreakHalfLength + WallPadding;
            var wall = DistanceToWall(map, pos, shot, need, ignore);

            if (wall < need)
                distance = wall - WallPadding - StreakHalfLength;

            if (distance < 0.3f)
                continue;

            var angle = new Angle(MathF.Atan2(-shot.X, shot.Y) + _random.NextFloat(-0.12f, 0.12f));
            AddDecal(config, _random.Pick(Streaks), grid, map, pos + shot * distance, angle, _random.NextFloat(0.8f, 1f));
        }

        for (var i = 0; i < config.DotCount; i++)
        {
            var shot = Rotate(dir, _random.NextFloat(-1.4f, 1.4f));
            var distance = _random.NextFloat(0.3f, config.SprayReach * 0.8f);
            var wall = DistanceToWall(map, pos, shot, distance + WallPadding, ignore);

            if (wall < distance + WallPadding)
                distance = wall - WallPadding;

            if (distance < 0.2f)
                continue;

            AddDecal(config, _random.Pick(Dots), grid, map, pos + shot * distance, _random.NextAngle(), _random.NextFloat(0.75f, 1f));
        }
    }

    private void AddDecal(XenoTurretComponent config, string id, EntityUid? grid, MapId map, Vector2 world, Angle angle, float alpha)
    {
        if (grid == null)
            return;

        var gridXform = Transform(grid.Value);
        var gridRotation = _transform.GetWorldRotation(gridXform);
        var coords = _transform.ToCoordinates((grid.Value, gridXform), new MapCoordinates(world, map))
            .Offset(new Vector2(-DecalCenterOffset, -DecalCenterOffset));

        if (_decals.TryAddDecal(id, coords, out var index, Color.White.WithAlpha(alpha), angle - gridRotation, 0, true))
            _decalQueue.Enqueue((grid.Value, index, _timing.CurTime + TimeSpan.FromSeconds(config.DecalLifetime)));
    }

    private void ExpireDecals(TimeSpan now)
    {
        while (_decalQueue.TryPeek(out var next) && next.Expire <= now)
        {
            _decalQueue.Dequeue();

            if (Exists(next.Grid))
                _decals.RemoveDecal(next.Grid, next.Index);
        }
    }

    private float DistanceToWall(MapId map, Vector2 from, Vector2 dir, float length, EntityUid ignore)
    {
        var ray = new CollisionRay(from, dir, (int) CollisionGroup.Impassable);
        var closest = float.MaxValue;

        foreach (var hit in _physics.IntersectRay(map, ray, length, ignore, false))
        {
            if (hit.Distance < closest)
                closest = hit.Distance;
        }

        return closest;
    }

    private static Vector2 Rotate(Vector2 v, float radians)
    {
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    private sealed class PendingKill
    {
        public EntityUid Target;
        public XenoTurretComponent Config = default!;
        public Vector2 Direction;
    }

    private sealed class FlyingPart
    {
        public EntityUid Uid;
        public XenoTurretComponent Config = default!;
        public MapId Map;
        public Vector2 Velocity;
        public float Spin;
        public float Trail;
    }
}
