// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Server._Nestation.Marines;
using Content.Server.Chat.Systems;
using Content.Shared._NeStation.Marine;
using Content.Shared.Access.Systems;
using Content.Shared.Chat;
using Content.Shared.CombatMode;
using Content.Shared.Doors.Components;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.SSDIndicator;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.NorthKoreanGuard;

public sealed class NorthKoreanGuardSystem : EntitySystem
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
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private IComponentFactory _compFactory = default!;

    private const int BlockMask = (int) CollisionGroup.MobMask;
    private const int ShotMask = (int) CollisionGroup.BulletImpassable;

    private const float ScanInterval = 0.25f;
    private const float ArriveDistance = 0.3f;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
        SubscribeLocalEvent<NorthKoreanGuardComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NorthKoreanGuardFortificationComponent, PreventCollideEvent>(OnFortificationPreventCollide);
    }

    private void OnMapInit(Entity<NorthKoreanGuardComponent> ent, ref MapInitEvent args)
    {
        RemComp<SSDIndicatorComponent>(ent);
        RemComp<SatiationComponent>(ent);
        RemComp<SatiationDamageComponent>(ent);
        RemComp<SatiationSpeedModifierComponent>(ent);
        RemComp<SatiationCounterAlertComponent>(ent);

        ent.Comp.PostCoords = Transform(ent).Coordinates;
        ent.Comp.PostAngle = _transform.GetWorldRotation(ent);
        ent.Comp.HasPost = true;
        ent.Comp.State = NorthKoreanGuardState.Post;
        ent.Comp.NextScan = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(0f, ScanInterval));
        ent.Comp.LastPos = _transform.GetWorldPosition(ent);

        EnsureGun(ent, ent.Comp);
    }

    private void OnFortificationPreventCollide(Entity<NorthKoreanGuardFortificationComponent> ent, ref PreventCollideEvent args)
    {
        if (TryComp<ProjectileComponent>(args.OtherEntity, out var projectile) && projectile.Shooter == ent.Comp.Guard)
            args.Cancelled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<NorthKoreanGuardComponent, TransformComponent, InputMoverComponent>();

        while (query.MoveNext(out var uid, out var comp, out var xform, out var mover))
        {
            if (!_mobState.IsAlive(uid))
            {
                comp.MoveDir = Vector2.Zero;
                ApplyMovement(uid, comp, mover);
                continue;
            }

            if (now >= comp.NextScan)
            {
                comp.NextScan = now + TimeSpan.FromSeconds(ScanInterval);
                Scan(uid, comp, xform, now);
            }

            Act(uid, comp, xform, now);
            ApplyMovement(uid, comp, mover);
        }
    }

    private void ApplyMovement(EntityUid uid, NorthKoreanGuardComponent comp, InputMoverComponent mover)
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

    private void Scan(EntityUid uid, NorthKoreanGuardComponent comp, TransformComponent xform, TimeSpan now)
    {
        var pos = _transform.GetWorldPosition(xform);
        var map = xform.MapID;
        var coords = new MapCoordinates(pos, map);
        var flags = LookupFlags.Dynamic | LookupFlags.Uncontained;

        EntityUid? marine = null;
        var marineDist = float.MaxValue;
        EntityUid? hostile = null;
        var hostileDist = float.MaxValue;
        var near = new HashSet<EntityUid>();

        foreach (var (ent, _) in _lookup.GetEntitiesInRange<MobStateComponent>(coords, comp.ThreatRange, flags))
        {
            if (ent == uid || HasComp<NorthKoreanGuardComponent>(ent))
                continue;

            if (TryComp<MarineComponent>(ent, out var npc) && !HasComp<ActorComponent>(ent) && npc.Team != "south")
                continue;

            if (!_mobState.IsAlive(ent) || !IsHumanoid(ent))
                continue;

            var dist = (_transform.GetWorldPosition(ent) - pos).Length();

            if (IsMarine(ent))
            {
                if (dist < marineDist && _examine.InRangeUnOccluded(uid, ent, comp.ThreatRange))
                {
                    marine = ent;
                    marineDist = dist;
                }

                continue;
            }

            if (dist <= comp.ClearRange)
            {
                near.Add(ent);
                Provoke(uid, comp, ent, dist, now);
            }

            if (!comp.Provocations.TryGetValue(ent, out var record) || !record.Hostile)
                continue;

            if (dist >= hostileDist || !_examine.InRangeUnOccluded(uid, ent, comp.ThreatRange))
                continue;

            hostile = ent;
            hostileDist = dist;
            record.LastNear = now;
        }

        List<EntityUid>? drop = null;
        foreach (var (ent, record) in comp.Provocations)
        {
            if (!Exists(ent) || !_mobState.IsAlive(ent))
            {
                drop ??= new List<EntityUid>();
                drop.Add(ent);
                continue;
            }

            if (near.Contains(ent))
                continue;

            record.InZone = false;

            if (now - record.LastNear > TimeSpan.FromSeconds(comp.ForgetAfter))
            {
                drop ??= new List<EntityUid>();
                drop.Add(ent);
            }
        }

        if (drop != null)
        {
            foreach (var ent in drop)
                comp.Provocations.Remove(ent);
        }

        var wasMarine = comp.FightingMarine;
        comp.FightingMarine = marine != null;

        if (marine is { } marineTarget)
        {
            comp.Target = marineTarget;
            comp.LastDanger = now;

            if (comp.State is NorthKoreanGuardState.Post or NorthKoreanGuardState.Return
                || (comp.State == NorthKoreanGuardState.Fight && !wasMarine))
            {
                BeginMarineFight(uid, comp, pos, map, _transform.GetWorldPosition(marineTarget));
            }

            return;
        }

        if (hostile is { } hostileTarget)
        {
            comp.Target = hostileTarget;
            comp.LastDanger = now;

            if (comp.State is NorthKoreanGuardState.Post or NorthKoreanGuardState.Return)
            {
                SetCombat(uid, true);
                comp.State = NorthKoreanGuardState.Fight;
            }

            return;
        }

        comp.Target = null;

        if (comp.State is NorthKoreanGuardState.Build or NorthKoreanGuardState.Fight
            && now - comp.LastDanger >= TimeSpan.FromSeconds(comp.CalmDownTime))
        {
            comp.Building = false;
            comp.State = NorthKoreanGuardState.Return;
        }
    }

    private void Provoke(EntityUid uid, NorthKoreanGuardComponent comp, EntityUid target, float dist, TimeSpan now)
    {
        if (!comp.Provocations.TryGetValue(target, out var record))
        {
            record = new NorthKoreanGuardProvocation();
            comp.Provocations[target] = record;
        }

        record.LastNear = now;

        if (record.Hostile)
            return;

        if (dist <= comp.WarnRange)
        {
            if (!record.Warned)
            {
                record.Warned = true;
                record.InZone = true;
                record.Approaches = 1;
                record.WarnedAt = now;
                _chat.TrySendInGameICMessage(uid, Loc.GetString(comp.WarnMessage), InGameICChatType.Speak, false);
                return;
            }

            if (!record.InZone)
            {
                record.InZone = true;
                record.Approaches++;
            }

            if (record.Approaches >= comp.MaxApproaches || now - record.WarnedAt >= TimeSpan.FromSeconds(comp.WarnGrace))
                record.Hostile = true;

            return;
        }

        record.InZone = false;

        if (record.Warned && now - record.WarnedAt >= TimeSpan.FromSeconds(comp.LoiterLimit))
            record.Hostile = true;
    }

    private bool IsHumanoid(EntityUid uid)
    {
        if (!_compFactory.TryGetRegistration("HumanoidAppearance", out var registration))
            return true;

        return EntityManager.HasComponent(uid, registration.Type);
    }

    private bool IsMarine(EntityUid uid)
    {
        if (TryComp<MarineComponent>(uid, out var marine))
            return marine.Team == "south";

        return _idCard.TryFindIdCard(uid, out var card) && HasComp<MarineIdCardComponent>(card.Owner);
    }

    private void BeginMarineFight(EntityUid uid, NorthKoreanGuardComponent comp, Vector2 pos, MapId map, Vector2 threatPos)
    {
        SetCombat(uid, true);
        comp.Building = false;
        comp.StuckCount = 0;

        if (comp.NestFire == null && !TryPlanNest(uid, comp, pos, map, threatPos))
        {
            comp.State = NorthKoreanGuardState.Fight;
            return;
        }

        comp.State = NorthKoreanGuardState.Build;
    }

    private bool TryPlanNest(EntityUid uid, NorthKoreanGuardComponent comp, Vector2 pos, MapId map, Vector2 threatPos)
    {
        var origin = PostWorld(comp, pos);
        var toThreat = threatPos - origin;
        var forward = MathF.Abs(toThreat.X) >= MathF.Abs(toThreat.Y)
            ? new Vector2(MathF.Sign(toThreat.X), 0f)
            : new Vector2(0f, MathF.Sign(toThreat.Y));

        if (forward == Vector2.Zero)
            forward = new Vector2(0f, -1f);

        var side = new Vector2(-forward.Y, forward.X);
        var step = MathF.Max(1f, comp.FortifyDistance - 1f);
        var firePos = origin + forward * step;

        if (!TrySnap(map, firePos, out var fireCoords))
            return false;

        var fireWorld = ToWorld(fireCoords);
        var path = fireWorld - origin;
        var pathLen = path.Length();

        if (pathLen > 0.1f && Cast(map, origin, path / pathLen, pathLen, uid))
            return false;

        var front = fireWorld + forward;
        var candidates = new[]
        {
            front,
            front + side,
            front - side,
            fireWorld + side,
            fireWorld - side,
        };

        var slots = new List<EntityCoordinates>();
        foreach (var candidate in candidates)
        {
            if (!TrySnap(map, candidate, out var slot))
                continue;

            var slotWorld = ToWorld(slot);
            var delta = slotWorld - fireWorld;
            var len = delta.Length();

            if (len > 0.5f && Cast(map, fireWorld, delta / len, len - 0.3f, uid))
                continue;

            slots.Add(slot);
        }

        if (slots.Count == 0)
            return false;

        comp.NestFire = fireCoords;
        comp.NestSlots = slots;
        comp.NestBuilt = new List<EntityUid?>();
        foreach (var _ in slots)
            comp.NestBuilt.Add(null);

        return true;
    }

    private bool TrySnap(MapId map, Vector2 world, out EntityCoordinates coords)
    {
        coords = default;

        if (!_maps.TryFindGridAt(map, world, out var gridUid, out var grid))
            return false;

        var local = _maps.WorldToLocal(gridUid, grid, world);
        var size = grid.TileSize;
        var snapped = new Vector2(
            MathF.Floor(local.X / size) * size + size / 2f,
            MathF.Floor(local.Y / size) * size + size / 2f);

        coords = new EntityCoordinates(gridUid, snapped);
        return true;
    }

    private Vector2 ToWorld(EntityCoordinates coords)
    {
        return _transform.ToMapCoordinates(coords).Position;
    }

    private Vector2 PostWorld(NorthKoreanGuardComponent comp, Vector2 fallback)
    {
        return comp.HasPost ? ToWorld(comp.PostCoords) : fallback;
    }

    private Vector2 HoldPosition(NorthKoreanGuardComponent comp, Vector2 fallback)
    {
        if (comp.FightingMarine && comp.NestFire is { } fire)
            return ToWorld(fire);

        return PostWorld(comp, fallback);
    }

    private bool HasMissingSlots(NorthKoreanGuardComponent comp)
    {
        foreach (var built in comp.NestBuilt)
        {
            if (built is not { } ent || !Exists(ent))
                return true;
        }

        return false;
    }

    private void BuildNest(EntityUid uid, NorthKoreanGuardComponent comp)
    {
        for (var i = 0; i < comp.NestSlots.Count; i++)
        {
            if (comp.NestBuilt[i] is { } existing && Exists(existing))
                continue;

            var barricade = Spawn(comp.FortificationProto, comp.NestSlots[i]);
            EnsureComp<NorthKoreanGuardFortificationComponent>(barricade).Guard = uid;
            comp.NestBuilt[i] = barricade;
        }
    }

    private void Act(EntityUid uid, NorthKoreanGuardComponent comp, TransformComponent xform, TimeSpan now)
    {
        var pos = _transform.GetWorldPosition(xform);
        comp.MoveDir = Vector2.Zero;

        switch (comp.State)
        {
            case NorthKoreanGuardState.Post:
                ActPost(uid, comp, pos, now);
                break;
            case NorthKoreanGuardState.Build:
                ActBuild(uid, comp, pos, now);
                break;
            case NorthKoreanGuardState.Fight:
                ActFight(uid, comp, xform, pos, now);
                break;
            case NorthKoreanGuardState.Return:
                ActReturn(uid, comp, pos, now);
                break;
        }
    }

    private void ActPost(EntityUid uid, NorthKoreanGuardComponent comp, Vector2 pos, TimeSpan now)
    {
        var post = PostWorld(comp, pos);

        if ((post - pos).Length() > ArriveDistance)
        {
            Walk(uid, comp, pos, post, now);
            return;
        }

        comp.StuckCount = 0;
        _transform.SetWorldRotation(uid, comp.PostAngle);
    }

    private void ActBuild(EntityUid uid, NorthKoreanGuardComponent comp, Vector2 pos, TimeSpan now)
    {
        if (comp.NestFire is not { } fire)
        {
            comp.State = NorthKoreanGuardState.Fight;
            return;
        }

        var firePos = ToWorld(fire);

        if ((firePos - pos).Length() > ArriveDistance)
        {
            if (Walk(uid, comp, pos, firePos, now))
            {
                comp.NestFire = null;
                comp.NestSlots.Clear();
                comp.NestBuilt.Clear();
                comp.StuckCount = 0;
                comp.State = NorthKoreanGuardState.Fight;
            }

            return;
        }

        if (!HasMissingSlots(comp))
        {
            comp.Building = false;
            comp.State = NorthKoreanGuardState.Fight;
            return;
        }

        if (!comp.Building)
        {
            comp.Building = true;
            comp.BuildDoneAt = now + TimeSpan.FromSeconds(comp.BuildTime);
            return;
        }

        if (now < comp.BuildDoneAt)
            return;

        BuildNest(uid, comp);
        comp.Building = false;
        comp.State = NorthKoreanGuardState.Fight;
    }

    private void ActFight(EntityUid uid, NorthKoreanGuardComponent comp, TransformComponent xform, Vector2 pos, TimeSpan now)
    {
        var hold = HoldPosition(comp, pos);

        if ((hold - pos).Length() > ArriveDistance)
        {
            if (Walk(uid, comp, pos, hold, now))
                comp.StuckCount = 0;
        }

        if (comp.Target is not { } target || !Exists(target) || !_mobState.IsAlive(target))
            return;

        if (EnsureGun(uid, comp) is not { } gun)
            return;

        var targetPos = _transform.GetWorldPosition(target);
        var delta = targetPos - pos;

        if (comp.MoveDir != Vector2.Zero)
            return;

        if (delta.LengthSquared() > 0.0001f)
            _transform.SetWorldRotation(uid, delta.ToWorldAngle());

        if (comp.AimingAt != target)
        {
            comp.AimingAt = target;
            comp.AimReadyAt = now + TimeSpan.FromSeconds(comp.AimDelay);
        }

        if (now < comp.AimReadyAt || now < comp.NextShot)
            return;

        if (!HasClearShot(uid, pos, target, xform.MapID))
            return;

        if (Shoot(uid, gun, xform, targetPos, target))
            comp.NextShot = now + TimeSpan.FromSeconds(_random.NextFloat(comp.ShotGapMin, comp.ShotGapMax));
    }

    private void ActReturn(EntityUid uid, NorthKoreanGuardComponent comp, Vector2 pos, TimeSpan now)
    {
        var post = PostWorld(comp, pos);

        if ((post - pos).Length() > ArriveDistance && !Walk(uid, comp, pos, post, now))
            return;

        comp.State = NorthKoreanGuardState.Post;
        comp.Target = null;
        comp.AimingAt = null;
        comp.StuckCount = 0;
        SetCombat(uid, false);
        _transform.SetWorldRotation(uid, comp.PostAngle);
    }

    private bool Walk(EntityUid uid, NorthKoreanGuardComponent comp, Vector2 pos, Vector2 goal, TimeSpan now)
    {
        var delta = goal - pos;
        var len = delta.Length();

        if (len < 0.05f)
            return false;

        comp.MoveDir = delta / len;
        _transform.SetWorldRotation(uid, delta.ToWorldAngle());

        if (!comp.WasMoving)
        {
            comp.LastPos = pos;
            comp.NextStuckCheck = now + TimeSpan.FromSeconds(1);
            comp.StuckCount = 0;
            return false;
        }

        if (now < comp.NextStuckCheck)
            return false;

        comp.NextStuckCheck = now + TimeSpan.FromSeconds(1);
        var moved = (pos - comp.LastPos).Length();
        comp.LastPos = pos;

        if (moved >= 0.3f)
        {
            comp.StuckCount = 0;
            return false;
        }

        comp.StuckCount++;
        return comp.StuckCount >= 3;
    }

    private void SetCombat(EntityUid uid, bool value)
    {
        if (TryComp<CombatModeComponent>(uid, out var combat))
            _combat.SetInCombatMode(uid, value, combat);
    }

    private Entity<GunComponent>? EnsureGun(EntityUid uid, NorthKoreanGuardComponent comp)
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

    private bool Shoot(EntityUid uid, Entity<GunComponent> gun, TransformComponent xform, Vector2 at, EntityUid target)
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
            var ray = new CollisionRay(from + side * i, dir, ShotMask);

            foreach (var result in _physics.IntersectRay(map, ray, dist, self, false))
            {
                var hit = result.HitEntity;

                if (hit == target || hit == self)
                    continue;

                if (HasComp<NorthKoreanGuardFortificationComponent>(hit))
                    continue;

                if (TryComp<DoorComponent>(hit, out var door) && door.State == DoorState.Open)
                    continue;

                if (result.Distance > dist - 0.35f)
                    continue;

                if (i != 0 && !HasComp<MobStateComponent>(hit))
                    continue;

                return false;
            }
        }

        return true;
    }

    private bool Cast(MapId map, Vector2 from, Vector2 dir, float len, EntityUid self)
    {
        if (len < 0.05f)
            return false;

        var ray = new CollisionRay(from, dir, BlockMask);
        foreach (var result in _physics.IntersectRay(map, ray, len, self, false))
        {
            var entity = result.HitEntity;

            if (HasComp<MobStateComponent>(entity))
                continue;

            if (TryComp<DoorComponent>(entity, out var door) && door.State == DoorState.Open)
                continue;

            return true;
        }

        return false;
    }
}
