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
using Content.Shared.Item;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Physics;
using Content.Shared.SSDIndicator;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
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
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedWieldableSystem _wieldable = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private SharedDoorSystem _door = default!;
    [Dependency] private SharedEntityStorageSystem _storage = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPointLightSystem _lights = default!;

    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    private const int BlockMask = (int) CollisionGroup.MobMask;
    private const int SightMask = (int) (CollisionGroup.Impassable | CollisionGroup.InteractImpassable);

    private const string GlowColor = "#39a1ff";
    private const float GlowRadius = 6f;
    private const float GlowEnergy = 7.5f;
    private const float GlowTime = 3f;

    private const float ThinkInterval = 0.1f;
    private const float HealthCheckInterval = 0.5f;
    private const float WallStop = 1.05f;
    private const float OpenReach = 1.2f;
    private const float SideOffset = 0.28f;
    private const float WaypointRadiusSq = 0.1225f;
    private const float WallCost = 12f;
    private const float OpenableCost = 3f;
    private const int PlanBudget = 20000;

    private const string VoicePath = "/Audio/_Nestation/Unbalanced/";

    private readonly record struct VoiceLine(string Name, float Length);

    private static readonly VoiceLine[] IdleLines =
    {
        new("idle_blood", 5.43f), new("idle_footsteps", 3.72f), new("idle_radio", 1.74f), new("idle_anybody", 3.89f),
    };

    private static readonly VoiceLine[] SpotLines =
    {
        new("spot_contact", 3.19f), new("spot_arms_up", 5.83f),
    };

    private static readonly VoiceLine[] CloseLines =
    {
        new("close_stay_away_1", 5.11f), new("close_stay_away_2", 7.16f), new("close_skull", 2.2f),
    };

    private static readonly VoiceLine[] KillLines =
    {
        new("kill_walk_into_shot", 3.19f), new("kill_no_next_time", 4.7f), new("kill_one_down", 2.42f),
    };

    private static readonly VoiceLine[] FleeLines =
    {
        new("flee_cowards", 4.24f), new("flee_keep_running", 4.97f),
    };

    private static readonly VoiceLine ScreamLine = new("scream_phase2", 3.29f);
    private static readonly VoiceLine RecoveredLine = new("hit_still_fighting", 4.82f);
    private static readonly VoiceLine AmbushedLine = new("hit_noo", 1.46f);
    private static readonly VoiceLine MissLine = new("miss_aiming", 2.82f);
    private static readonly VoiceLine OutOfShellsLine = new("out_of_shells", 1.57f);
    private static readonly VoiceLine ReloadLine = new("reload", 1.6f);

    private static readonly (float At, string Path)[] ReloadSounds =
    {
        (0.1f, "/Audio/Weapons/Guns/Cock/shotgun_open.ogg"),
        (0.6f, "/Audio/Weapons/Guns/MagIn/shotgun_insert.ogg"),
        (1.05f, "/Audio/Weapons/Guns/MagIn/shotgun_insert.ogg"),
        (1.5f, "/Audio/Weapons/Guns/MagIn/shotgun_insert.ogg"),
        (1.95f, "/Audio/Weapons/Guns/MagIn/shotgun_insert.ogg"),
        (2.4f, "/Audio/Weapons/Guns/MagIn/shotgun_insert.ogg"),
        (2.95f, "/Audio/Weapons/Guns/Cock/shotgun_close.ogg"),
    };

    private static readonly Vector2i[] Dirs =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
    };

    public override void Initialize()
    {
        base.Initialize();

        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
        SubscribeLocalEvent<UnbalancedComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<UnbalancedComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<ItemComponent, GunShotEvent>(OnAnyGunShot);
    }

    private void OnMapInit(Entity<UnbalancedComponent> ent, ref MapInitEvent args)
    {
        RemComp<SSDIndicatorComponent>(ent);
        RemComp<SatiationComponent>(ent);
        RemComp<SatiationDamageComponent>(ent);
        RemComp<SatiationSpeedModifierComponent>(ent);
        RemComp<SatiationCounterAlertComponent>(ent);

        var now = _timing.CurTime;
        ent.Comp.NextThink = now;
        ent.Comp.NextRegen = now + TimeSpan.FromSeconds(ent.Comp.RegenInterval);
        ent.Comp.NextIdleLine = now + TimeSpan.FromSeconds(_random.NextFloat(8f, 20f));
        ent.Comp.ReloadSoundStep = ReloadSounds.Length;

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
            HandleGlow(uid, comp, now);

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

            HandleScream(uid, comp, now);

            if (!_mobState.IsAlive(uid))
            {
                comp.MoveDir = Vector2.Zero;
                ApplyMovement(uid, comp, mover);
                continue;
            }

            HandleVoiceTimers(uid, comp, now);

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

    private bool TryGetHealth(EntityUid uid, out float health)
    {
        health = 1f;

        if (!TryComp<DamageableComponent>(uid, out var damageable)
            || !_thresholds.TryGetThresholdForState(uid, MobState.Critical, out var critical)
            || critical.Value <= FixedPoint2.Zero)
        {
            return false;
        }

        var total = _damageable.GetPositiveDamage((uid, damageable)).GetTotal();
        health = 1f - total.Float() / critical.Value.Float();
        return true;
    }

    private void HandleScream(EntityUid uid, UnbalancedComponent comp, TimeSpan now)
    {
        if (now >= comp.NextHealthCheck)
        {
            comp.NextHealthCheck = now + TimeSpan.FromSeconds(HealthCheckInterval);

            if (TryGetHealth(uid, out var health))
            {
                if (!comp.Screaming && health < comp.ScreamBelow)
                {
                    comp.Screaming = true;
                    comp.NextScream = now;
                    StartGlow(uid, comp, now);
                }
                else if (comp.Screaming && health >= comp.ScreamUntil)
                {
                    comp.Screaming = false;

                    if (!_mobState.IsDead(uid))
                        Say(uid, comp, now, RecoveredLine, true);
                }
            }
        }

        if (!comp.Screaming || now < comp.NextScream)
            return;

        comp.NextScream = now + TimeSpan.FromSeconds(comp.ScreamInterval);
        Say(uid, comp, now, ScreamLine, true);
    }

    private void StartGlow(EntityUid uid, UnbalancedComponent comp, TimeSpan now)
    {
        var light = EnsureComp<PointLightComponent>(uid);
        _lights.SetColor(uid, Color.FromHex(GlowColor), light);
        _lights.SetRadius(uid, GlowRadius, light);
        _lights.SetEnergy(uid, GlowEnergy, light);
        _lights.SetEnabled(uid, true, light);
        comp.GlowUntil = now + TimeSpan.FromSeconds(GlowTime);
    }

    private void HandleGlow(EntityUid uid, UnbalancedComponent comp, TimeSpan now)
    {
        if (comp.GlowUntil == TimeSpan.Zero || now < comp.GlowUntil)
            return;

        comp.GlowUntil = TimeSpan.Zero;
        RemComp<PointLightComponent>(uid);
    }

    private bool Say(EntityUid uid, UnbalancedComponent comp, TimeSpan now, VoiceLine line, bool force)
    {
        if (!force && now < comp.SpeechBusyUntil)
            return false;

        if (force)
            comp.Voice = _audio.Stop(comp.Voice);

        comp.Voice = _audio.PlayPvs(new SoundPathSpecifier(VoicePath + line.Name + ".ogg"), uid)?.Entity;
        comp.SpeechBusyUntil = now + TimeSpan.FromSeconds(line.Length + 0.3f);
        return true;
    }

    private bool SayAny(EntityUid uid, UnbalancedComponent comp, TimeSpan now, VoiceLine[] lines, bool force)
    {
        return Say(uid, comp, now, _random.Pick(lines), force);
    }

    private void HandleVoiceTimers(EntityUid uid, UnbalancedComponent comp, TimeSpan now)
    {
        while (comp.ReloadSoundStep < ReloadSounds.Length
               && now >= comp.ReloadStartedAt + TimeSpan.FromSeconds(ReloadSounds[comp.ReloadSoundStep].At))
        {
            _audio.PlayPvs(new SoundPathSpecifier(ReloadSounds[comp.ReloadSoundStep].Path), uid);
            comp.ReloadSoundStep++;
        }

        if (comp.ReloadSayAt != TimeSpan.Zero && now >= comp.ReloadSayAt)
        {
            comp.ReloadSayAt = TimeSpan.Zero;
            Say(uid, comp, now, ReloadLine, true);
        }

        if (comp.MissCheckAt != TimeSpan.Zero && now >= comp.MissCheckAt)
        {
            comp.MissCheckAt = TimeSpan.Zero;

            if (comp.LastDamaged < comp.MissShotAt && now >= comp.NextMissLine)
            {
                comp.NextMissLine = now + TimeSpan.FromSeconds(_random.NextFloat(6f, 12f));
                Say(uid, comp, now, MissLine, false);
            }
        }
    }

    private void OnDamageChanged(EntityUid uid, UnbalancedComponent comp, DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.Origin is not { } origin || origin == uid || _mobState.IsDead(uid))
            return;

        var now = _timing.CurTime;
        comp.LastDamaged = now;

        if (comp.AimTarget != null || comp.Screaming || now < comp.NextHitLine)
            return;

        comp.NextHitLine = now + TimeSpan.FromSeconds(_random.NextFloat(5f, 10f));
        Say(uid, comp, now, AmbushedLine, false);
    }

    private void OnAnyGunShot(Entity<ItemComponent> ent, ref GunShotEvent args)
    {
        var shooter = args.User;
        if (HasComp<UnbalancedComponent>(shooter))
            return;

        var shooterXform = Transform(shooter);
        var shooterPos = _transform.GetWorldPosition(shooterXform);
        var facing = _transform.GetWorldRotation(shooterXform).ToWorldVec();
        var now = _timing.CurTime;

        var query = EntityQueryEnumerator<UnbalancedComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.MapID != shooterXform.MapID || comp.MissCheckAt != TimeSpan.Zero || _mobState.IsDead(uid))
                continue;

            var delta = _transform.GetWorldPosition(xform) - shooterPos;
            var dist = delta.Length();
            if (dist < 1f || dist > 25f)
                continue;

            if (Vector2.Dot(facing, delta / dist) < 0.9f)
                continue;

            comp.MissShotAt = now;
            comp.MissCheckAt = now + TimeSpan.FromSeconds(0.8);
        }
    }

    private bool TryFire(EntityUid uid, UnbalancedComponent comp, Entity<GunComponent> gun, TransformComponent xform, Vector2 from, Vector2 at, EntityUid? target, TimeSpan now)
    {
        if (now < comp.ReloadUntil)
        {
            var delta = at - from;
            if (delta.LengthSquared() > 0.0001f)
                _transform.SetWorldRotation(uid, delta.ToWorldAngle());

            return false;
        }

        if (!Shoot(uid, gun, xform, from, at, target))
            return false;

        if (comp.ReloadTime <= 0f)
            return true;

        comp.ShotsInMagazine++;
        if (comp.ShotsInMagazine < comp.MagazineSize)
            return true;

        comp.ShotsInMagazine = 0;
        comp.ReloadUntil = now + TimeSpan.FromSeconds(comp.ReloadTime);
        comp.ReloadStartedAt = now;
        comp.ReloadSoundStep = 0;
        comp.ReloadSayAt = now + TimeSpan.FromSeconds(OutOfShellsLine.Length + 0.1f);
        Say(uid, comp, now, OutOfShellsLine, true);
        return true;
    }

    private Entity<GunComponent>? EnsureGun(EntityUid uid, UnbalancedComponent comp)
    {
        if (!_gun.TryGetGun(uid, out var held))
        {
            if (comp.Gun is not { } gunUid || !Exists(gunUid))
            {
                gunUid = Spawn(comp.GunProto, Transform(uid).Coordinates);
                comp.Gun = gunUid;
            }

            _hands.TryForcePickupAnyHand(uid, gunUid, checkActionBlocker: false);

            if (!_gun.TryGetGun(uid, out held))
                return null;
        }

        comp.Gun = held.Owner;

        if (TryComp<WieldableComponent>(held, out var wieldable) && !wieldable.Wielded)
            _wieldable.TryWield((held.Owner, wieldable), uid);

        return held;
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

        if (TryShootLiving(uid, comp, gun, xform, pos, now))
        {
            comp.MoveDir = Vector2.Zero;
            return;
        }

        if (now >= comp.NextIdleLine)
        {
            comp.NextIdleLine = now + TimeSpan.FromSeconds(_random.NextFloat(15f, 35f));

            if (!comp.Screaming && now - comp.LastDamaged > TimeSpan.FromSeconds(6))
                SayAny(uid, comp, now, IdleLines, false);
        }

        Patrol(uid, comp, gun, xform, pos, now);
    }

    private bool TryShootLiving(EntityUid uid, UnbalancedComponent comp, Entity<GunComponent> gun, TransformComponent xform, Vector2 pos, TimeSpan now)
    {
        var map = xform.MapID;

        if (comp.AimTarget is { } previous && (!Exists(previous) || _mobState.IsDead(previous)))
        {
            if (comp.EngagedShots > 0)
                SayAny(uid, comp, now, KillLines, true);

            comp.AimTarget = null;
            comp.EngagedShots = 0;
        }

        EntityUid? best = null;
        var bestPos = Vector2.Zero;
        var bestDist = float.MaxValue;
        var keepCurrent = false;
        var currentPos = Vector2.Zero;

        var flags = LookupFlags.Dynamic | LookupFlags.Uncontained;
        foreach (var (target, _) in _lookup.GetEntitiesInRange<MobStateComponent>(new MapCoordinates(pos, map), comp.ShootRange, flags))
        {
            if (target == uid || _mobState.IsDead(target))
                continue;

            var targetPos = _transform.GetWorldPosition(target);
            var dist = (targetPos - pos).Length();

            if (dist > 0.2f)
            {
                var dir = (targetPos - pos) / dist;
                if (Cast(null, map, pos, dir, dist - 0.05f, SightMask, uid, out _, out _))
                    continue;
            }

            if (target == comp.AimTarget)
            {
                keepCurrent = true;
                currentPos = targetPos;
            }

            if (dist >= bestDist)
                continue;

            best = target;
            bestPos = targetPos;
            bestDist = dist;
        }

        if (keepCurrent && comp.AimTarget is { } current)
        {
            best = current;
            bestPos = currentPos;
        }

        if (best == null)
        {
            if (comp.AimTarget != null && comp.EngagedShots > 0)
                SayAny(uid, comp, now, FleeLines, false);

            comp.AimTarget = null;
            comp.EngagedShots = 0;
            return false;
        }

        if (comp.AimTarget != best)
        {
            var first = comp.AimTarget == null;
            comp.AimTarget = best;
            comp.AimReadyAt = now + TimeSpan.FromSeconds(comp.AimDelay);
            comp.EngagedShots = 0;

            if (first)
                SayAny(uid, comp, now, SpotLines, true);
        }

        if (now >= comp.NextCloseLine && (bestPos - pos).Length() <= 3.5f)
        {
            comp.NextCloseLine = now + TimeSpan.FromSeconds(_random.NextFloat(10f, 18f));
            SayAny(uid, comp, now, CloseLines, false);
        }

        if (now < comp.AimReadyAt)
        {
            var delta = bestPos - pos;
            if (delta.LengthSquared() > 0.0001f)
                _transform.SetWorldRotation(uid, delta.ToWorldAngle());

            return true;
        }

        if (TryFire(uid, comp, gun, xform, pos, bestPos, best, now))
            comp.EngagedShots++;

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

    private UnbalancedGridMemory GetMemory(UnbalancedComponent comp, EntityUid gridUid)
    {
        if (!comp.Memory.TryGetValue(gridUid, out var memory))
        {
            memory = new UnbalancedGridMemory();
            comp.Memory[gridUid] = memory;
        }

        return memory;
    }

    private bool IsProtoBlacklisted(UnbalancedComponent comp, EntityUid entity)
    {
        return MetaData(entity).EntityPrototype?.ID is { } id && comp.UnbreakableProtos.Contains(id);
    }

    private bool IsBlacklisted(UnbalancedComponent comp, UnbalancedGridMemory memory, EntityUid entity, Vector2i tile)
    {
        return comp.UnbreakableEnts.Contains(entity)
               || memory.Unbreakable.Contains(tile)
               || IsProtoBlacklisted(comp, entity);
    }

    private void BlacklistEntity(UnbalancedComponent comp, EntityUid entity)
    {
        comp.UnbreakableEnts.Add(entity);

        if (MetaData(entity).EntityPrototype?.ID is { } id)
            comp.UnbreakableProtos.Add(id);
    }

    private void Blacklist(UnbalancedComponent comp, UnbalancedGridMemory memory, EntityUid entity, Vector2i tile)
    {
        BlacklistEntity(comp, entity);
        memory.Unbreakable.Add(tile);
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
            ResetPath(comp);
            comp.Grid = gridUid;
        }

        var memory = GetMemory(comp, gridUid);
        var tile = _maps.WorldToTile(gridUid, grid, pos);
        MarkVisited(memory, tile);
        CheckStuck(comp, memory, pos, now);

        if (comp.Obstacle != null)
        {
            WorkObstacle(uid, comp, memory, gun, xform, pos, now);
            return;
        }

        if (now < comp.NextAction)
        {
            comp.MoveDir = Vector2.Zero;
            return;
        }

        while (comp.Path.Count > 0 && (TileCenter(gridUid, grid, comp.Path[0]) - pos).LengthSquared() < WaypointRadiusSq)
            comp.Path.RemoveAt(0);

        if (comp.Path.Count == 0 && !Plan(comp, memory, gridUid, grid, tile))
        {
            memory.Visited.Clear();
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
        if (!Probe(map, pos, moveDir, probeLen, uid, out var hitDist, out var hit))
            return;

        var hitTile = _maps.WorldToTile(gridUid, grid, _transform.GetWorldPosition(hit));

        if (IsBlacklisted(comp, memory, hit, hitTile))
        {
            var onPath = comp.Path[0] == hitTile || (comp.Path.Count > 1 && comp.Path[1] == hitTile);
            if (onPath && hitDist <= WallStop + 0.3f)
            {
                Blacklist(comp, memory, hit, hitTile);
                comp.Path.Clear();
                comp.Goal = null;
                comp.MoveDir = Vector2.Zero;
            }

            return;
        }

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

    private void WorkObstacle(EntityUid uid, UnbalancedComponent comp, UnbalancedGridMemory memory, Entity<GunComponent> gun, TransformComponent xform, Vector2 pos, TimeSpan now)
    {
        comp.MoveDir = Vector2.Zero;

        var obstacle = comp.Obstacle!.Value;
        if (!Exists(obstacle) || TerminatingOrDeleted(obstacle))
        {
            ClearObstacle(comp);
            comp.Path.Clear();
            return;
        }

        if (!comp.ObstacleInfinite && IsProtoBlacklisted(comp, obstacle))
        {
            ClearObstacle(comp);
            comp.Path.Clear();
            comp.Goal = null;
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

            if (comp.Grid is { } gridUid && TryComp<MapGridComponent>(gridUid, out var grid))
            {
                var obstacleTile = _maps.WorldToTile(gridUid, grid, obstaclePos);
                Blacklist(comp, memory, obstacle, obstacleTile);
            }
            else
            {
                BlacklistEntity(comp, obstacle);
            }

            ClearObstacle(comp);
            comp.Path.Clear();
            comp.Goal = null;
            comp.NextAction = now + TimeSpan.FromSeconds(0.3);
            return;
        }

        if (TryFire(uid, comp, gun, xform, pos, obstaclePos, obstacle, now))
        {
            comp.ObstacleShots++;
            comp.LastShot = now;
        }
    }

    private void CheckStuck(UnbalancedComponent comp, UnbalancedGridMemory memory, Vector2 pos, TimeSpan now)
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
                    memory.Visited.Add(goal);

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

    private void ResetPath(UnbalancedComponent comp)
    {
        comp.Path.Clear();
        comp.Goal = null;
        comp.Obstacle = null;
        comp.ObstacleShots = 0;
        comp.ObstacleInfinite = false;
    }

    private void MarkVisited(UnbalancedGridMemory memory, Vector2i tile)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                memory.Visited.Add(new Vector2i(tile.X + dx, tile.Y + dy));
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

    private float TileCost(UnbalancedComponent comp, UnbalancedGridMemory memory, EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        if (memory.Unbreakable.Contains(tile))
            return -1f;

        if (!_maps.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
            return -1f;

        var cost = 1f;
        foreach (var anchored in _maps.GetAnchoredEntities(gridUid, grid, tile))
        {
            if (!IsBlocking(anchored))
                continue;

            if (comp.UnbreakableEnts.Contains(anchored) || IsProtoBlacklisted(comp, anchored))
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

    private bool Plan(UnbalancedComponent comp, UnbalancedGridMemory memory, EntityUid gridUid, MapGridComponent grid, Vector2i start)
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

            if (current != start && !memory.Visited.Contains(current) && GetCost(current) <= 1f)
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

            var value = TileCost(comp, memory, gridUid, grid, tile);
            costs[tile] = value;
            return value;
        }
    }

    private bool Probe(MapId map, Vector2 pos, Vector2 dir, float len, EntityUid self, out float dist, out EntityUid hit)
    {
        dist = float.MaxValue;
        hit = default;
        var found = false;
        var side = new Vector2(-dir.Y, dir.X) * SideOffset;

        for (var i = -1; i <= 1; i++)
        {
            var origin = pos + side * i;
            if (Cast(null, map, origin, dir, len, BlockMask, self, out var d, out var h) && d < dist)
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
