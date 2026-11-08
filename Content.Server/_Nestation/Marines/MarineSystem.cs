// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Shared._Nestation.Marines;
using Content.Server.NPC.HTN;
using Content.Shared._Nestation.XenoHive;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Doors.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Physics;
using Content.Shared.SSDIndicator;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Server._Nestation.XenoHive;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.Marines;

public sealed partial class MarineSystem : EntitySystem
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
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private SharedDoorSystem _door = default!;
    [Dependency] private SharedEntityStorageSystem _storage = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MarineMissionSystem _mission = default!;

    private const int BlockMask = (int) CollisionGroup.MobMask;
    private const int ShotMask = (int) CollisionGroup.BulletImpassable;

    private const float ThinkInterval = 0.12f;
    private const float ScanInterval = 0.3f;
    private const float Lookahead = 2.4f;
    private const float SideOffset = 0.28f;
    private const float DoorTargetRange = 5.5f;

    private static readonly float[] FanAngles = { 0f, 25f, -25f, 50f, -50f, 90f, -90f, 135f, -135f, 180f };

    public override void Initialize()
    {
        base.Initialize();

        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
        SubscribeLocalEvent<MarineComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MarineComponent, DamageChangedEvent>(OnDamageChanged);
    }

    private void OnMapInit(Entity<MarineComponent> ent, ref MapInitEvent args)
    {
        RemComp<SSDIndicatorComponent>(ent);
        RemComp<SatiationComponent>(ent);
        RemComp<SatiationDamageComponent>(ent);
        RemComp<SatiationSpeedModifierComponent>(ent);
        RemComp<SatiationCounterAlertComponent>(ent);

        _thresholds.SetMobStateThreshold(ent, FixedPoint2.New(ent.Comp.CriticalHealth), MobState.Critical);
        _thresholds.SetMobStateThreshold(ent, FixedPoint2.New(ent.Comp.DeadHealth), MobState.Dead);

        var now = _timing.CurTime;
        ent.Comp.NextThink = now + TimeSpan.FromSeconds(_random.NextFloat(0f, ThinkInterval));
        ent.Comp.NextCover = now + TimeSpan.FromSeconds(_random.NextFloat(3f, 8f));
        ent.Comp.LastPos = _transform.GetWorldPosition(ent);

        if (TryComp<CombatModeComponent>(ent, out var combat))
            _combat.SetInCombatMode(ent, true, combat);

        EnsureGun(ent, ent.Comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<MarineComponent, TransformComponent, InputMoverComponent>();

        while (query.MoveNext(out var uid, out var comp, out var xform, out var mover))
        {
            if (!_mobState.IsAlive(uid))
            {
                comp.MoveDir = Vector2.Zero;
                ApplyMovement(uid, comp, mover);
                continue;
            }

            if (TryComp<MarineParachuteComponent>(uid, out var chute))
            {
                if (now < chute.Start + TimeSpan.FromSeconds(chute.Duration))
                {
                    comp.MoveDir = Vector2.Zero;
                    ApplyMovement(uid, comp, mover);
                    continue;
                }

                RemComp<MarineParachuteComponent>(uid);
            }

            if (now >= comp.NextThink)
            {
                comp.NextThink = now + TimeSpan.FromSeconds(ThinkInterval);
                Think(uid, comp, xform, now);
            }

            ApplyMovement(uid, comp, mover);
        }
    }

    private void ApplyMovement(EntityUid uid, MarineComponent comp, InputMoverComponent mover)
    {
        var input = comp.MoveDir;
        if (input != Vector2.Zero)
            input = (-_mover.GetParentGridAngle(mover)).RotateVec(input);

        mover.CurTickWalkMovement = Vector2.Zero;
        mover.CurTickSprintMovement = input;
        mover.LastInputTick = _timing.CurTick;
        mover.LastInputSubTick = ushort.MaxValue;

        var moving = comp.MoveDir != Vector2.Zero;
        if (moving == comp.WasMoving)
            return;

        comp.WasMoving = moving;
        var ev = new SpriteMoveEvent(moving);
        RaiseLocalEvent(uid, ref ev);
    }

    private void OnDamageChanged(EntityUid uid, MarineComponent comp, DamageChangedEvent args)
    {
        Entity<MarineComponent> ent = (uid, comp);

        if (_mobState.IsDead(ent))
            return;

        var now = _timing.CurTime;

        if (args.DamageIncreased)
            ent.Comp.LastDamaged = now;

        if (!TryComp<DamageableComponent>(ent, out var damageable)
            || !_thresholds.TryGetThresholdForState(ent, MobState.Critical, out var critical)
            || critical.Value <= FixedPoint2.Zero)
        {
            return;
        }

        var fraction = _damageable.GetPositiveDamage((ent, damageable)).GetTotal().Float() / critical.Value.Float();
        var step = Math.Clamp((int) MathF.Floor(fraction * 10f), 0, 10);

        if (step < ent.Comp.LastScreamStep)
        {
            ent.Comp.LastScreamStep = step;
            return;
        }

        if (step == ent.Comp.LastScreamStep)
            return;

        ent.Comp.LastScreamStep = step;

        if (now < ent.Comp.NextScream)
            return;

        ent.Comp.NextScream = now + TimeSpan.FromSeconds(ent.Comp.ScreamCooldown);
        _chat.TryEmoteWithChat(ent, "Scream", forceEmote: true);
    }

    private Entity<GunComponent>? EnsureGun(EntityUid uid, MarineComponent comp)
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
        return held;
    }

    private void Think(EntityUid uid, MarineComponent comp, TransformComponent xform, TimeSpan now)
    {
        var pos = _transform.GetWorldPosition(xform);
        var map = xform.MapID;

        if (TryComp<PullableComponent>(uid, out var pullable) && pullable.BeingPulled)
        {
            comp.MoveDir = Vector2.Zero;

            if (now >= comp.NextResist)
            {
                comp.NextResist = now + TimeSpan.FromSeconds(0.6);
                _pulling.TryStopPull(uid, pullable, uid);
            }

            return;
        }

        if (EnsureGun(uid, comp) is not { } gun)
        {
            comp.MoveDir = Vector2.Zero;
            return;
        }

        if (comp.Target is { } current && (!Exists(current) || !IsValidTarget(current)))
            comp.Target = null;

        if (now >= comp.NextScan)
        {
            comp.NextScan = now + TimeSpan.FromSeconds(ScanInterval);
            AcquireTarget(uid, comp, pos, map, now);
        }

        var targetPos = Vector2.Zero;
        var targetDist = float.MaxValue;
        var hasTarget = false;

        if (comp.Target is { } target)
        {
            targetPos = _transform.GetWorldPosition(target);
            targetDist = (targetPos - pos).Length();
            hasTarget = true;

            if (targetDist > comp.ShootRange + 2f)
            {
                comp.Target = null;
                hasTarget = false;
            }
        }

        if (hasTarget)
            FireAt(uid, comp, gun, xform, pos, targetPos, comp.Target, now);

        Vector2? goal = null;

        if (comp.State == MarineState.Cover)
        {
            if (now >= comp.CoverUntil || comp.CoverPoint == null)
            {
                comp.State = MarineState.Advance;
                comp.CoverPoint = null;
                comp.NextCover = now + TimeSpan.FromSeconds(_random.NextFloat(8f, 14f));
            }
            else if ((comp.CoverPoint.Value - pos).Length() > 0.45f && now < comp.CoverUntil - TimeSpan.FromSeconds(1))
            {
                goal = comp.CoverPoint;
            }
        }

        if (comp.State != MarineState.Cover)
        {
            if (hasTarget && comp.Target is { } t && HasComp<MobStateComponent>(t)
                && ShouldSeekCover(comp, targetDist, now)
                && TryFindCover(uid, comp, pos, targetPos, map, out var cover))
            {
                comp.State = MarineState.Cover;
                comp.CoverPoint = cover;
                comp.CoverUntil = now + TimeSpan.FromSeconds(_random.NextFloat(comp.CoverTimeMin, comp.CoverTimeMax));
                goal = cover;
            }
            else
            {
                goal = Objective(uid, comp, pos, now);
            }
        }

        var sep = GetSeparation(uid, comp, pos, map);

        if (goal is { } g)
        {
            Steer(uid, comp, pos, g, sep, map, now);
        }
        else
        {
            comp.MoveDir = Vector2.Zero;

            if (comp.State != MarineState.Raise && sep.LengthSquared() > 0.25f)
                comp.MoveDir = Vector2.Normalize(sep);

            if (!hasTarget && comp.Flag is { } flagUid && comp.State is MarineState.Raise or MarineState.Guard
                && Exists(flagUid))
            {
                var look = _transform.GetWorldPosition(flagUid) - pos;
                if (look.LengthSquared() > 0.01f)
                    _transform.SetWorldRotation(uid, look.ToWorldAngle());
            }
        }

        if (!hasTarget && comp.MoveDir != Vector2.Zero)
            _transform.SetWorldRotation(uid, comp.MoveDir.ToWorldAngle());

        CheckStuck(uid, comp, pos, map, now);
    }

    private bool IsValidTarget(EntityUid target)
    {
        if (TerminatingOrDeleted(target))
            return false;

        if (TryComp<MobStateComponent>(target, out var state))
            return state.CurrentState != MobState.Dead;

        if (TryComp<DoorComponent>(target, out var door))
            return door.State == DoorState.Closed;

        return true;
    }

    private bool IsXenoMob(EntityUid uid)
    {
        return HasComp<MobStateComponent>(uid)
               && (HasComp<XenoCasteComponent>(uid) || HasComp<XenoHiveMemberComponent>(uid))
               && !_mobState.IsDead(uid);
    }

    private bool IsHostileStructure(EntityUid uid)
    {
        if (HasComp<MobStateComponent>(uid) || HasComp<ItemComponent>(uid) || HasComp<MarineComponent>(uid))
            return false;

        if (TryComp<DoorComponent>(uid, out var door))
            return HasComp<XenoOnlyDoorComponent>(uid) && door.State == DoorState.Closed;

        var id = MetaData(uid).EntityPrototype?.ID;
        var turret = HasComp<HTNComponent>(uid)
                     || (id != null && id.Contains("Turret", StringComparison.OrdinalIgnoreCase));

        if (!turret)
            return false;

        return HasComp<XenoHiveStructureComponent>(uid)
               || (id != null && id.Contains("Xeno", StringComparison.OrdinalIgnoreCase));
    }

    private void AcquireTarget(EntityUid uid, MarineComponent comp, Vector2 pos, MapId map, TimeSpan now)
    {
        if (comp.Ignored.Count > 0)
        {
            var expired = new List<EntityUid>();
            foreach (var (ent, until) in comp.Ignored)
            {
                if (now >= until || !Exists(ent))
                    expired.Add(ent);
            }

            foreach (var ent in expired)
                comp.Ignored.Remove(ent);
        }

        if (comp.Target is { } existing && comp.EngageSince != TimeSpan.Zero
            && !HasComp<MobStateComponent>(existing) && now - comp.EngageSince > TimeSpan.FromSeconds(7))
        {
            comp.Ignored[existing] = now + TimeSpan.FromSeconds(10);
            comp.Target = null;
        }

        var coords = new MapCoordinates(pos, map);
        var flags = LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Uncontained;
        var candidates = new List<(EntityUid Ent, float Score)>();

        foreach (var (ent, _) in _lookup.GetEntitiesInRange<MobStateComponent>(coords, comp.ShootRange, flags))
        {
            if (comp.Ignored.ContainsKey(ent) || !IsXenoMob(ent))
                continue;

            var dist = (_transform.GetWorldPosition(ent) - pos).Length();
            candidates.Add((ent, dist));
        }

        var seen = new HashSet<EntityUid>();

        foreach (var (ent, _) in _lookup.GetEntitiesInRange<XenoHiveStructureComponent>(coords, comp.ShootRange, flags))
        {
            TryAddStructure(uid, comp, pos, ent, seen, candidates);
        }

        foreach (var (ent, _) in _lookup.GetEntitiesInRange<HTNComponent>(coords, comp.ShootRange, flags))
        {
            TryAddStructure(uid, comp, pos, ent, seen, candidates);
        }

        foreach (var (ent, _) in _lookup.GetEntitiesInRange<NpcFactionMemberComponent>(coords, comp.ShootRange, flags))
        {
            TryAddStructure(uid, comp, pos, ent, seen, candidates);
        }

        if (candidates.Count == 0)
        {
            comp.Target = null;
            comp.EngageSince = TimeSpan.Zero;
            return;
        }

        candidates.Sort((a, b) => a.Score.CompareTo(b.Score));

        var tested = 0;
        foreach (var (ent, _) in candidates)
        {
            if (tested++ >= 4)
                break;

            if (!HasClearShot(uid, pos, ent, map))
                continue;

            if (comp.Target != ent)
            {
                comp.Target = ent;
                comp.EngageSince = now;
                comp.AimReadyAt = now + TimeSpan.FromSeconds(_random.NextFloat(comp.AimDelayMin, comp.AimDelayMax));
                comp.BurstLeft = 0;
            }

            return;
        }

        comp.Target = null;
        comp.EngageSince = TimeSpan.Zero;
    }

    private void TryAddStructure(EntityUid self, MarineComponent comp, Vector2 pos, EntityUid ent, HashSet<EntityUid> seen, List<(EntityUid Ent, float Score)> candidates)
    {
        if (!seen.Add(ent) || comp.Ignored.ContainsKey(ent) || HasComp<MobStateComponent>(ent))
            return;

        if (!IsHostileStructure(ent))
            return;

        var dist = (_transform.GetWorldPosition(ent) - pos).Length();
        var isDoor = HasComp<DoorComponent>(ent);

        if (isDoor && dist > DoorTargetRange)
            return;

        candidates.Add((ent, isDoor ? dist + 4f : dist - 4f));
    }

    private bool HasClearShot(EntityUid self, Vector2 from, EntityUid target, MapId map)
    {
        var to = _transform.GetWorldPosition(target);
        var delta = to - from;
        var dist = delta.Length();

        if (dist < 0.5f)
            return true;

        var dir = delta / dist;
        var side = new Vector2(-dir.Y, dir.X) * 0.3f;

        for (var i = -1; i <= 1; i++)
        {
            var origin = from + side * i;
            var ray = new CollisionRay(origin, dir, ShotMask);

            foreach (var result in _physics.IntersectRay(map, ray, dist, self, false))
            {
                var hit = result.HitEntity;

                if (hit == target || hit == self)
                    continue;

                if (TryComp<DoorComponent>(hit, out var door) && door.State == DoorState.Open)
                    continue;

                if (result.Distance > dist - 0.35f)
                    continue;

                if (i != 0 && !HasComp<MobStateComponent>(hit))
                    continue;

                if (HasComp<MobStateComponent>(hit) && IsXenoMob(hit))
                    continue;

                return false;
            }
        }

        return true;
    }

    private void FireAt(EntityUid uid, MarineComponent comp, Entity<GunComponent> gun, TransformComponent xform, Vector2 pos, Vector2 targetPos, EntityUid? target, TimeSpan now)
    {
        var delta = targetPos - pos;
        if (delta.LengthSquared() > 0.0001f)
            _transform.SetWorldRotation(uid, delta.ToWorldAngle());

        if (now < comp.AimReadyAt || now < comp.BurstPauseUntil || now < comp.NextShot)
            return;

        if (target is { } t && !HasClearShot(uid, pos, t, xform.MapID))
            return;

        if (comp.BurstLeft <= 0)
            comp.BurstLeft = _random.Next(comp.BurstMin, comp.BurstMax + 1);

        if (!Shoot(uid, gun, xform, targetPos, target))
            return;

        comp.BurstLeft--;
        comp.NextShot = now + TimeSpan.FromSeconds(comp.ShotGap + _random.NextFloat(0f, 0.12f));

        if (comp.BurstLeft <= 0)
            comp.BurstPauseUntil = now + TimeSpan.FromSeconds(_random.NextFloat(comp.BurstPauseMin, comp.BurstPauseMax));
    }

    private bool Shoot(EntityUid uid, Entity<GunComponent> gun, TransformComponent xform, Vector2 at, EntityUid? target)
    {
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

    private bool ShouldSeekCover(MarineComponent comp, float targetDist, TimeSpan now)
    {
        if (comp.State is MarineState.Raise or MarineState.Cover || now < comp.NextCover)
            return false;

        if (now - comp.LastDamaged < TimeSpan.FromSeconds(1.5))
            return true;

        return targetDist < 9f && _random.Prob(0.03f);
    }

    private bool TryFindCover(EntityUid uid, MarineComponent comp, Vector2 pos, Vector2 enemyPos, MapId map, out Vector2 cover)
    {
        cover = default;
        var bestScore = float.MaxValue;
        var found = false;
        var jitter = _random.NextFloat(0f, 22f);

        for (var i = 0; i < 16; i++)
        {
            var dir = Angle.FromDegrees(i * 22.5f + jitter).ToWorldVec();

            for (var r = 2f; r <= comp.CoverSearchRadius; r += 1.75f)
            {
                var candidate = pos + dir * r;

                if (Cast(null, map, pos, dir, r, BlockMask, uid, out _, out _))
                    break;

                var toCandidate = candidate - enemyPos;
                var len = toCandidate.Length();
                if (len < 3f || len > comp.ShootRange)
                    continue;

                if (!Cast(null, map, enemyPos, toCandidate / len, len, ShotMask, uid, out var hitDist, out var hit))
                    continue;

                if (HasComp<MarineComponent>(hit) || hitDist < len - 2f || hitDist > len - 0.3f)
                    continue;

                if (r < bestScore)
                {
                    bestScore = r;
                    cover = candidate;
                    found = true;
                }

                break;
            }
        }

        return found;
    }

    private Vector2? Objective(EntityUid uid, MarineComponent comp, Vector2 pos, TimeSpan now)
    {
        Entity<MarineFlagpoleComponent>? next = null;
        Entity<MarineFlagpoleComponent>? rally = null;
        var nextPos = Vector2.Zero;
        var rallyPos = Vector2.Zero;

        var flags = EntityQueryEnumerator<MarineFlagpoleComponent, TransformComponent>();
        while (flags.MoveNext(out var flagUid, out var flag, out var flagXform))
        {
            if (flag.MissionId != comp.MissionId)
                continue;

            if (!flag.Raised)
            {
                if (next == null || flag.Order < next.Value.Comp.Order)
                {
                    next = (flagUid, flag);
                    nextPos = _transform.GetWorldPosition(flagXform);
                }
            }
            else if (rally == null || flag.Order >= rally.Value.Comp.Order)
            {
                rally = (flagUid, flag);
                rallyPos = _transform.GetWorldPosition(flagXform);
            }
        }

        if (next is not { } nextFlag)
        {
            comp.Flag = rally?.Owner;
            return Hunt(uid, comp, pos, rally != null ? rallyPos : null, now);
        }

        comp.Flag = nextFlag.Owner;

        if (rally is { } rallyFlag
            && comp.State != MarineState.Raise
            && now - rallyFlag.Comp.RaisedAt < TimeSpan.FromSeconds(comp.GatherMaxWait))
        {
            var near = CountMarinesNear(comp.MissionId, rallyPos, comp.GatherRadius);
            var distToRally = (rallyPos - pos).Length();

            if (near < comp.GatherCount || distToRally > comp.GatherRadius)
            {
                comp.State = MarineState.Gather;

                if (Far(comp, distToRally))
                    return rallyPos + PersonalOffset(uid);

                return null;
            }
        }

        var distToFlag = (nextPos - pos).Length();

        if (nextFlag.Comp.Raiser == uid)
        {
            comp.State = MarineState.Raise;
            return null;
        }

        if (distToFlag <= comp.RaiseRange && nextFlag.Comp.Raiser == null
            && _mission.TryBeginRaise(nextFlag, uid))
        {
            comp.State = MarineState.Raise;
            return null;
        }

        if (nextFlag.Comp.Raiser != null && !Far(comp, distToFlag))
        {
            comp.State = MarineState.Guard;
            return null;
        }

        comp.State = MarineState.Advance;
        comp.Holding = false;
        return nextPos + PersonalOffset(uid);
    }

    private bool Far(MarineComponent comp, float dist)
    {
        var far = comp.Holding ? dist > 3.2f : dist > 2f;
        comp.Holding = !far;
        return far;
    }

    private static Vector2 PersonalOffset(EntityUid uid)
    {
        var angle = uid.Id * 2.39996f;
        var radius = 1f + (uid.Id % 5) * 0.2f;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    private Vector2 GetSeparation(EntityUid uid, MarineComponent comp, Vector2 pos, MapId map)
    {
        var sep = Vector2.Zero;
        var radius = comp.SeparationRadius;
        var coords = new MapCoordinates(pos, map);

        foreach (var (other, _) in _lookup.GetEntitiesInRange<MarineComponent>(coords, radius, LookupFlags.Dynamic | LookupFlags.Uncontained))
        {
            if (other == uid || _mobState.IsDead(other))
                continue;

            var delta = pos - _transform.GetWorldPosition(other);
            var dist = delta.Length();

            if (dist < 0.05f)
            {
                sep += Angle.FromDegrees(_random.NextFloat(0f, 360f)).ToWorldVec();
                continue;
            }

            sep += delta / dist * ((radius - dist) / radius);
        }

        return sep;
    }

    private Vector2? Hunt(EntityUid uid, MarineComponent comp, Vector2 pos, Vector2? anchor, TimeSpan now)
    {
        comp.State = MarineState.Hunt;

        if (now >= comp.NextRoam || comp.RoamPoint == null)
        {
            comp.NextRoam = now + TimeSpan.FromSeconds(_random.NextFloat(2f, 4f));
            comp.RoamPoint = null;

            var xform = Transform(uid);
            var coords = new MapCoordinates(pos, xform.MapID);
            var flags = LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Uncontained;
            var bestDist = float.MaxValue;

            foreach (var (ent, _) in _lookup.GetEntitiesInRange<XenoCasteComponent>(coords, 60f, flags))
            {
                if (_mobState.IsDead(ent))
                    continue;

                var p = _transform.GetWorldPosition(ent);
                var d = (p - pos).Length();
                if (d < bestDist)
                {
                    bestDist = d;
                    comp.RoamPoint = p;
                }
            }

            if (comp.RoamPoint == null)
            {
                foreach (var (ent, _) in _lookup.GetEntitiesInRange<XenoHiveStructureComponent>(coords, 60f, flags))
                {
                    var p = _transform.GetWorldPosition(ent);
                    var d = (p - pos).Length();
                    if (d < bestDist)
                    {
                        bestDist = d;
                        comp.RoamPoint = p;
                    }
                }
            }

            if (comp.RoamPoint == null)
            {
                var center = anchor ?? pos;
                comp.RoamPoint = center + new Vector2(_random.NextFloat(-12f, 12f), _random.NextFloat(-12f, 12f));
            }
        }

        if (comp.RoamPoint is { } point && Far(comp, (point - pos).Length()))
            return point;

        return null;
    }

    private int CountMarinesNear(string missionId, Vector2 center, float radius)
    {
        var count = 0;
        var radiusSq = radius * radius;
        var query = EntityQueryEnumerator<MarineComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var marine, out var xform))
        {
            if (marine.MissionId != missionId || _mobState.IsDead(uid))
                continue;

            if ((_transform.GetWorldPosition(xform) - center).LengthSquared() <= radiusSq)
                count++;
        }

        return count;
    }

    private void Steer(EntityUid uid, MarineComponent comp, Vector2 pos, Vector2 goal, Vector2 sep, MapId map, TimeSpan now)
    {
        var to = goal - pos;
        var dist = to.Length();

        if (dist < 0.3f)
        {
            comp.MoveDir = Vector2.Zero;
            return;
        }

        var want = to / dist;

        if (now < comp.EscapeUntil)
            want = comp.EscapeDir;

        if (sep != Vector2.Zero)
        {
            var mixed = want + sep * 1.6f;
            if (mixed.LengthSquared() > 0.01f)
                want = Vector2.Normalize(mixed);
        }

        var reach = MathF.Min(Lookahead, MathF.Max(dist, 0.8f));

        if (!Probe(map, pos, want, reach, uid, out var straight, out var straightHit))
        {
            comp.MoveDir = want;
            comp.CommitDir = want;
            return;
        }

        if (straight < 1.3f && TryClearObstacle(uid, comp, straightHit, now))
        {
            comp.MoveDir = Vector2.Zero;
            return;
        }

        var best = Vector2.Zero;
        var bestScore = float.MinValue;
        var bestClear = 0f;

        foreach (var angle in FanAngles)
        {
            var rotation = Angle.FromDegrees(angle);
            var dir = rotation.RotateVec(want);
            float clear;

            if (MathF.Abs(angle) <= 50f)
                clear = Probe(map, pos, dir, Lookahead, uid, out var d, out _) ? d : Lookahead;
            else
                clear = Cast(null, map, pos, dir, Lookahead, BlockMask, uid, out var d2, out _) ? d2 : Lookahead;

            var score = clear / Lookahead + MathF.Cos(MathHelper.DegreesToRadians(angle)) * 1.4f;

            if (comp.CommitDir != Vector2.Zero)
                score += Vector2.Dot(dir, comp.CommitDir) * 0.5f;

            if (score > bestScore)
            {
                bestScore = score;
                best = dir;
                bestClear = clear;
            }
        }

        if (bestClear < 0.7f)
        {
            comp.MoveDir = Vector2.Zero;
            comp.EscapeDir = Angle.FromDegrees(_random.Pick(new[] { 90f, -90f, 135f, -135f })).RotateVec(want);
            comp.EscapeUntil = now + TimeSpan.FromSeconds(_random.NextFloat(0.6f, 1.2f));
            return;
        }

        comp.MoveDir = best;
        comp.CommitDir = best;
    }

    private bool TryClearObstacle(EntityUid uid, MarineComponent comp, EntityUid hit, TimeSpan now)
    {
        if (now < comp.NextOpen)
            return false;

        if (TryComp<DoorComponent>(hit, out var door))
        {
            if (door.State == DoorState.Closed)
            {
                if (HasComp<XenoOnlyDoorComponent>(hit))
                {
                    if (comp.Target == null && !comp.Ignored.ContainsKey(hit))
                    {
                        comp.Target = hit;
                        comp.EngageSince = now;
                        comp.AimReadyAt = now + TimeSpan.FromSeconds(_random.NextFloat(comp.AimDelayMin, comp.AimDelayMax));
                    }

                    return false;
                }

                comp.NextOpen = now + TimeSpan.FromSeconds(0.5);
                return _door.TryOpen(hit, door, uid, quiet: true);
            }

            return false;
        }

        if (TryComp<EntityStorageComponent>(hit, out var storage) && !storage.Open)
        {
            comp.NextOpen = now + TimeSpan.FromSeconds(0.6);
            return _storage.TryOpenStorage(uid, hit, true);
        }

        return false;
    }

    private void CheckStuck(EntityUid uid, MarineComponent comp, Vector2 pos, MapId map, TimeSpan now)
    {
        if (now < comp.NextStuckCheck)
            return;

        comp.NextStuckCheck = now + TimeSpan.FromSeconds(1);

        var moved = (pos - comp.LastPos).Length();
        comp.LastPos = pos;

        if (comp.MoveDir == Vector2.Zero || moved >= 0.35f)
        {
            comp.StuckCount = 0;
            return;
        }

        comp.StuckCount++;

        var dir = comp.MoveDir;
        var side = _random.Prob(0.5f) ? 90f : -90f;
        comp.EscapeDir = Angle.FromDegrees(side + _random.NextFloat(-30f, 30f)).RotateVec(dir);
        comp.EscapeUntil = now + TimeSpan.FromSeconds(_random.NextFloat(0.8f, 1.6f));
        comp.CommitDir = Vector2.Zero;

        if (comp.StuckCount < 3)
            return;

        var coords = new MapCoordinates(pos, map);
        foreach (var (ent, door) in _lookup.GetEntitiesInRange<DoorComponent>(coords, 1.6f, LookupFlags.Static))
        {
            if (door.State == DoorState.Closed && !HasComp<XenoOnlyDoorComponent>(ent))
                _door.TryOpen(ent, door, uid, quiet: true);
        }

        foreach (var (ent, storage) in _lookup.GetEntitiesInRange<EntityStorageComponent>(coords, 1.6f, LookupFlags.Static | LookupFlags.Dynamic))
        {
            if (!storage.Open)
                _storage.TryOpenStorage(uid, ent, true);
        }

        if (comp.StuckCount >= 5)
        {
            comp.StuckCount = 0;
            comp.EscapeDir = Angle.FromDegrees(_random.NextFloat(0f, 360f)).ToWorldVec();
            comp.EscapeUntil = now + TimeSpan.FromSeconds(2);
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
