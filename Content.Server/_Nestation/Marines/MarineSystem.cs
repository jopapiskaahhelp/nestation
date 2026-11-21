// Nestation New City CC BY-NC 3.0
using System.Linq;
using System.Numerics;
using Content.Server._Nestation.NorthKoreanGuard;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;
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
using Content.Shared.Hands.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
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
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Server._Nestation.XenoHive;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
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
    [Dependency] private MarineEmplacementSystem _emplacement = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedRoofSystem _roof = default!;
    [Dependency] private MarineReactionSystem _reaction = default!;
    [Dependency] private StandingStateSystem _standing = default!;

    private const int BlockMask = (int) CollisionGroup.MobMask;
    private const int ShotMask = (int) CollisionGroup.BulletImpassable;

    private const float ThinkInterval = 0.12f;
    private const float ScanInterval = 0.3f;
    private const float Lookahead = 2.4f;
    private const float SideOffset = 0.28f;
    private const float DoorTargetRange = 5.5f;

    private readonly List<(Vector2 Pos, MapId Map)> _passages = new();
    private readonly List<(Vector2 Pos, MapId Map, TimeSpan Time)> _learned = new();
    private TimeSpan _nextPassageRefresh;

    private Vector2 _lastShotPos;
    private TimeSpan _lastShotTime;
    private string _lastShotTeam = string.Empty;

    private static readonly string[] TalkEmotes =
    {
        "что-то тихо говорит",
        "кивает в ответ",
        "качает головой",
        "жестикулирует",
        "вздыхает и что-то шепчет",
        "негромко смеётся",
        "понизив голос, делится новостями",
        "озабоченно хмурится",
    };

    private static readonly float[] FanAngles = { 0f, 25f, -25f, 50f, -50f, 90f, -90f, 135f, -135f, 180f };

    public override void Initialize()
    {
        base.Initialize();

        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
        SubscribeLocalEvent<MarineComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MarineComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<MarineComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<GunComponent, AmmoShotEvent>(OnAnyShot);
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

        PickGun(ent.Comp);

        var now = _timing.CurTime;
        ent.Comp.NextThink = now + TimeSpan.FromSeconds(_random.NextFloat(0f, ThinkInterval));
        ent.Comp.NextCover = now + TimeSpan.FromSeconds(_random.NextFloat(3f, 8f));
        ent.Comp.LastPos = _transform.GetWorldPosition(ent);

        if (ent.Comp.Defender && !ent.Comp.Static)
            ent.Comp.Flanker = _random.Prob(0.3f);

        if (ent.Comp.Passive)
        {
            ent.Comp.Running = false;
            ent.Comp.Boldness = _random.NextFloat();
            ent.Comp.Hunger = _random.NextFloat(0.1f, 0.6f);
            ent.Comp.Thirst = _random.NextFloat(0.1f, 0.5f);
            return;
        }

        if (TryComp<CombatModeComponent>(ent, out var combat))
            _combat.SetInCombatMode(ent, true, combat);

        EnsureGun(ent, ent.Comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        if (now >= _nextPassageRefresh)
        {
            _nextPassageRefresh = now + TimeSpan.FromSeconds(8);
            RefreshPassages(now);
        }

        var query = EntityQueryEnumerator<MarineComponent, TransformComponent, InputMoverComponent>();

        while (query.MoveNext(out var uid, out var comp, out var xform, out var mover))
        {
            if (HasComp<ActorComponent>(uid))
                continue;

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

        mover.CurTickWalkMovement = comp.Running ? Vector2.Zero : input;
        mover.CurTickSprintMovement = comp.Running ? input : Vector2.Zero;
        mover.LastInputTick = _timing.CurTick;
        mover.LastInputSubTick = ushort.MaxValue;

        var moving = comp.MoveDir != Vector2.Zero;
        if (moving == comp.WasMoving)
            return;

        comp.WasMoving = moving;
        var ev = new SpriteMoveEvent(moving);
        RaiseLocalEvent(uid, ref ev);
    }

    private void PickGun(MarineComponent comp)
    {
        if (comp.GunPool.Count == 0)
            return;

        var total = 0f;
        foreach (var weight in comp.GunPool.Values)
        {
            total += weight;
        }

        var roll = _random.NextFloat(0f, total);
        foreach (var (proto, weight) in comp.GunPool)
        {
            roll -= weight;
            if (roll > 0f)
                continue;

            comp.GunProto = proto;
            return;
        }
    }

    private void OnMobState(EntityUid uid, MarineComponent comp, MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        if (comp.DeleteGunOnDeath && comp.Gun is { } gun && Exists(gun))
        {
            comp.IgnoreRemovalUntil = _timing.CurTime + TimeSpan.FromSeconds(2);
            QueueDel(gun);
            comp.Gun = null;
        }

        if (comp.Emplacement is { } emplacement)
            _emplacement.GunnerDied(emplacement);
    }

    private void OnDamageChanged(EntityUid uid, MarineComponent comp, DamageChangedEvent args)
    {
        Entity<MarineComponent> ent = (uid, comp);

        if (args.Origin is { } origin && args.DamageIncreased)
            _reaction.OnAggression(origin, uid, _timing.CurTime);

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

        if (comp.Passive)
        {
            ThinkCivilian(uid, comp, pos, map, now);
            return;
        }

        if (EnsureGun(uid, comp) is not { } gun)
        {
            comp.MoveDir = Vector2.Zero;
            return;
        }

        if (comp.Scene != null)
        {
            ThinkScripted(uid, comp, pos, map, now);
            return;
        }

        if (comp.Target is { } current && (!Exists(current) || !IsValidTarget(current)))
            comp.Target = null;

        if (now >= comp.NextScan)
        {
            comp.NextScan = now + TimeSpan.FromSeconds(ScanInterval);
            AcquireTarget(uid, comp, pos, map, now);
        }

        if (comp.BuildAt is { } buildAt)
        {
            comp.MoveDir = Vector2.Zero;

            if (now >= buildAt)
                FinishBuild(uid, comp, xform, pos, now);

            return;
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
        {
            comp.LastKnown = targetPos;
            comp.LastSeen = now;

            if (comp.Target is { } seen && HasComp<MobStateComponent>(seen))
                UpdateMemory(comp, seen, targetPos, now);

            FireAt(uid, comp, gun, xform, pos, targetPos, comp.Target, now);
        }
        else if (comp.LastKnown is { } lastKnown && now - comp.LastSeen < TimeSpan.FromSeconds(2.5))
        {
            FireAt(uid, comp, gun, xform, pos, lastKnown, null, now);
        }

        if (comp.Defender && comp.CanBuild && hasTarget && comp.State != MarineState.Cover && comp.BuildAt == null
            && comp.Barricades < comp.MaxBarricades && now >= comp.NextBuild && targetDist > 6f && !comp.Static)
        {
            StartBuild(uid, comp, pos, targetPos, xform, now);
        }

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

        if (comp.Static)
            goal = null;

        if (goal is { } direct && comp.State != MarineState.Cover)
            goal = RoutePoint(uid, pos, direct, map);

        var sep = GetSeparation(uid, comp, pos, map);

        if (goal is { } g)
        {
            Steer(uid, comp, pos, g, sep, map, now);
        }
        else
        {
            comp.MoveDir = Vector2.Zero;

            if (!comp.Static && comp.State != MarineState.Raise && sep.LengthSquared() > 0.25f)
                comp.MoveDir = Vector2.Normalize(sep);

            if (!hasTarget && comp.Flag is { } flagUid && comp.State is MarineState.Raise or MarineState.Guard
                && Exists(flagUid))
            {
                var look = _transform.GetWorldPosition(flagUid) - pos;
                if (look.LengthSquared() > 0.01f)
                    _transform.SetWorldRotation(uid, look.ToWorldAngle());
            }
        }

        if (!hasTarget && comp.MoveDir != Vector2.Zero && !comp.Static)
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

    private bool IsEnemy(MarineComponent self, EntityUid other)
    {
        if (self.Passive || !HasComp<MobStateComponent>(other) || _mobState.IsDead(other))
            return false;

        if (self.Team == "north" && HasComp<ActorComponent>(other))
            return _reaction.IsPlayerEnemy(other);

        if (self.Team == "south" && HasComp<NorthKoreanGuardComponent>(other))
            return true;

        if (TryComp<MarineComponent>(other, out var marine))
            return !marine.Neutral && marine.Team != self.Team;

        return self.TargetXeno && IsXenoMob(other);
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
            if (comp.Ignored.ContainsKey(ent) || !IsEnemy(comp, ent))
                continue;

            var dist = (_transform.GetWorldPosition(ent) - pos).Length();
            candidates.Add((ent, dist));
        }

        var seen = new HashSet<EntityUid>();

        if (comp.TargetXenoStructures)
        {
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

            if (!HasClearShot(uid, comp, pos, ent, map))
                continue;

            if (comp.Target != ent)
            {
                comp.Target = ent;
                comp.EngageSince = now;
                comp.AimReadyAt = now + TimeSpan.FromSeconds(_random.NextFloat(comp.AimDelayMin, comp.AimDelayMax));
                comp.BurstLeft = 0;

                AlertAllies(uid, comp, pos, ent, map, now);
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

    private bool HasClearShot(EntityUid self, MarineComponent comp, Vector2 from, EntityUid target, MapId map)
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

                if (HasComp<MobStateComponent>(hit) && IsEnemy(comp, hit))
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

        if (target is { } t && !HasClearShot(uid, comp, pos, t, xform.MapID))
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
        if (comp.Static || comp.State is MarineState.Raise or MarineState.Cover || now < comp.NextCover)
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
        if (comp.Defender)
            return Defend(uid, comp, pos, now);

        if (comp.State != MarineState.Raise)
        {
            var chase = Pursue(uid, comp, pos, now);

            if (chase.Active)
            {
                comp.State = MarineState.Advance;
                return chase.Goal;
            }
        }

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

    private Vector2? Defend(EntityUid uid, MarineComponent comp, Vector2 pos, TimeSpan now)
    {
        comp.Home ??= pos;
        var home = comp.Home.Value;
        comp.State = MarineState.Guard;

        var recentlyFought = comp.Target != null || now - comp.LastSeen < TimeSpan.FromSeconds(6);

        if (DamageFraction(uid) > 0.6f && recentlyFought)
            return Far(comp, (home - pos).Length()) ? home + PersonalOffset(uid) : null;

        if (comp.Target is { } target && Exists(target))
        {
            var targetPos = _transform.GetWorldPosition(target);
            var targetDist = (targetPos - pos).Length();

            if (comp.Flanker && comp.FlankPoint is { } existing && now < comp.FlankUntil && (existing - pos).Length() > 2.5f)
                return existing;

            if (comp.Flanker && comp.FlankPoint == null && targetDist > 10f && !comp.Static)
            {
                var toTarget = Vector2.Normalize(targetPos - pos);
                var side = new Vector2(-toTarget.Y, toTarget.X) * (uid.Id % 2 == 0 ? 1f : -1f);
                comp.FlankPoint = pos + side * 9f + toTarget * 4f;
                comp.FlankUntil = now + TimeSpan.FromSeconds(14);
                return comp.FlankPoint;
            }

            return null;
        }

        if (comp.Hunt is { } hunt && Exists(hunt) && !_mobState.IsDead(hunt))
        {
            var huntPos = _transform.GetWorldPosition(hunt);

            if ((huntPos - pos).Length() > 2.2f)
            {
                comp.State = MarineState.Advance;
                return huntPos;
            }

            return null;
        }

        if (now - comp.LastSeen > TimeSpan.FromSeconds(14))
            comp.FlankPoint = null;

        if (comp.HeardPoint == null && _lastShotTime != TimeSpan.Zero
            && now - _lastShotTime < TimeSpan.FromSeconds(3)
            && _lastShotTeam != comp.Team
            && (_lastShotPos - pos).Length() < 20f
            && (_lastShotPos - home).Length() < comp.Leash + 10f)
        {
            comp.HeardPoint = _lastShotPos + new Vector2(_random.NextFloat(-3f, 3f), _random.NextFloat(-3f, 3f));
            comp.HeardUntil = now + TimeSpan.FromSeconds(12);
        }

        if (comp.HeardPoint is { } heard)
        {
            if (now >= comp.HeardUntil || (heard - pos).Length() < 8f)
            {
                comp.HeardPoint = null;
            }
            else
            {
                comp.State = MarineState.Advance;
                return heard;
            }
        }

        var pursuit = Pursue(uid, comp, pos, now);

        if (pursuit.Active)
        {
            if (pursuit.Goal is { } chase && comp.Home is { } leashHome && (chase - leashHome).Length() > comp.Leash + 15f)
            {
                comp.MemTarget = null;
                comp.MemTrail.Clear();
            }
            else
            {
                comp.State = MarineState.Advance;
                return pursuit.Goal;
            }
        }

        if (comp.PatrolGoal is { } patrol)
            return (patrol - pos).Length() > 1.5f ? patrol : null;

        if (comp.Police && comp.PatrolPoints is { Count: > 0 })
            return null;

        if (now >= comp.NextRoam || comp.RoamPoint == null)
        {
            comp.NextRoam = now + TimeSpan.FromSeconds(_random.NextFloat(4f, 8f));
            comp.RoamPoint = home + new Vector2(_random.NextFloat(-6f, 6f), _random.NextFloat(-6f, 6f));
        }

        if (comp.RoamPoint is { } point && (point - pos).Length() > 1.5f)
            return point;

        return null;
    }

    private void AlertAllies(EntityUid uid, MarineComponent comp, Vector2 pos, EntityUid target, MapId map, TimeSpan now)
    {
        var targetPos = _transform.GetWorldPosition(target);
        var coords = new MapCoordinates(pos, map);
        var isMob = HasComp<MobStateComponent>(target);

        foreach (var (other, otherComp) in _lookup.GetEntitiesInRange<MarineComponent>(coords, 25f, LookupFlags.Dynamic | LookupFlags.Uncontained))
        {
            if (other == uid || otherComp.Team != comp.Team || otherComp.Passive || otherComp.Static || _mobState.IsDead(other))
                continue;

            if (otherComp.Defender)
            {
                otherComp.HeardPoint = targetPos;
                otherComp.HeardUntil = now + TimeSpan.FromSeconds(12);
            }

            if (isMob && now - otherComp.MemSeen > TimeSpan.FromSeconds(3))
            {
                otherComp.MemTarget = target;
                otherComp.MemPos = targetPos;
                otherComp.MemVel = Vector2.Zero;
                otherComp.MemSeen = now;
                otherComp.MemTrail.Clear();
                otherComp.MemTrail.Add(targetPos);
            }
        }
    }

    private void UpdateMemory(MarineComponent comp, EntityUid target, Vector2 targetPos, TimeSpan now)
    {
        if (comp.MemTarget == target)
        {
            var dt = (float) (now - comp.MemSeen).TotalSeconds;

            if (dt > 0.05f && dt < 1.5f)
                comp.MemVel = Vector2.Lerp(comp.MemVel, (targetPos - comp.MemPos) / dt, 0.5f);
        }
        else
        {
            comp.MemVel = Vector2.Zero;
            comp.MemTrail.Clear();
        }

        comp.MemTarget = target;
        comp.MemPos = targetPos;
        comp.MemSeen = now;

        if (comp.MemTrail.Count == 0 || (comp.MemTrail[comp.MemTrail.Count - 1] - targetPos).Length() > 2f)
        {
            comp.MemTrail.Add(targetPos);

            if (comp.MemTrail.Count > 10)
                comp.MemTrail.RemoveAt(0);
        }
    }

    private (bool Active, Vector2? Goal) Pursue(EntityUid uid, MarineComponent comp, Vector2 pos, TimeSpan now)
    {
        if (comp.Target != null)
            return (false, null);

        if (comp.MemTarget is not { } remembered || !Exists(remembered) || _mobState.IsDead(remembered)
            || now - comp.MemSeen > TimeSpan.FromSeconds(comp.MemorySeconds))
        {
            comp.MemTarget = null;
            comp.MemTrail.Clear();
            return (false, null);
        }

        var since = (float) (now - comp.MemSeen).TotalSeconds;

        if (since < 1.2f && comp.Defender)
            return (true, null);

        var map = Transform(uid).MapID;
        var trail = comp.MemTrail;

        for (var i = trail.Count - 1; i >= 0; i--)
        {
            if ((trail[i] - pos).Length() < 2.5f)
            {
                trail.RemoveRange(0, i + 1);
                break;
            }
        }

        Vector2 target;

        if (trail.Count > 0)
        {
            target = trail[trail.Count - 1];

            for (var i = trail.Count - 1; i >= 0; i--)
            {
                var delta = trail[i] - pos;
                var len = delta.Length();

                if (len < 0.5f || !Cast(null, map, pos, delta / len, len, BlockMask, uid, out _, out _))
                {
                    target = trail[i];
                    break;
                }
            }
        }
        else
        {
            var offset = comp.MemVel * MathF.Min(since, 6f);

            if (offset.Length() > 14f)
                offset = Vector2.Normalize(offset) * 14f;

            target = comp.MemPos + offset;

            if ((target - pos).Length() < 3f)
            {
                if (now >= comp.NextSearch || comp.SearchPoint == null)
                {
                    comp.NextSearch = now + TimeSpan.FromSeconds(3);
                    comp.SearchPoint = comp.MemPos + new Vector2(_random.NextFloat(-8f, 8f), _random.NextFloat(-8f, 8f));
                }

                target = comp.SearchPoint ?? target;
            }
        }

        return (true, target);
    }

    private void RefreshPassages(TimeSpan now)
    {
        _passages.Clear();

        var doors = EntityQueryEnumerator<DoorComponent, TransformComponent>();
        while (doors.MoveNext(out var door, out _, out var xform))
        {
            if (HasComp<XenoOnlyDoorComponent>(door))
                continue;

            _passages.Add((_transform.GetWorldPosition(xform), xform.MapID));
        }

        _learned.RemoveAll(l => now - l.Time > TimeSpan.FromMinutes(6));

        foreach (var (learnedPos, learnedMap, _) in _learned)
        {
            _passages.Add((learnedPos, learnedMap));
        }
    }

    private void LearnPassage(Vector2 pos, MapId map, TimeSpan now)
    {
        foreach (var (existing, existingMap, _) in _learned)
        {
            if (existingMap == map && (existing - pos).LengthSquared() < 4f)
                return;
        }

        if (_learned.Count >= 150)
            _learned.RemoveAt(0);

        _learned.Add((pos, map, now));
    }

    private Vector2 RoutePoint(EntityUid uid, Vector2 pos, Vector2 goal, MapId map)
    {
        var delta = goal - pos;
        var dist = delta.Length();

        if (dist < 3f)
            return goal;

        var dir = delta / dist;

        if (!Cast(null, map, pos, dir, MathF.Min(dist, 25f), BlockMask, uid, out _, out _))
            return goal;

        var limit = dist * 2.5f;
        var candidates = new List<(float Score, Vector2 Pos)>();

        foreach (var (passage, passageMap) in _passages)
        {
            if (passageMap != map)
                continue;

            var d1 = (passage - pos).Length();

            if (d1 < 1.5f || d1 > 25f)
                continue;

            var score = d1 + (goal - passage).Length();

            if (score < limit)
                candidates.Add((score, passage));
        }

        candidates.Sort((a, b) => a.Score.CompareTo(b.Score));

        for (var i = 0; i < candidates.Count && i < 6; i++)
        {
            var toPassage = candidates[i].Pos - pos;
            var len = toPassage.Length() - 0.8f;

            if (len < 0.1f || !Cast(null, map, pos, toPassage / (len + 0.8f), len, BlockMask, uid, out _, out _))
                return candidates[i].Pos;
        }

        return goal;
    }

    private float DamageFraction(EntityUid uid)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable)
            || !_thresholds.TryGetThresholdForState(uid, MobState.Critical, out var critical)
            || critical.Value <= FixedPoint2.Zero)
        {
            return 0f;
        }

        return _damageable.GetPositiveDamage((uid, damageable)).GetTotal().Float() / critical.Value.Float();
    }

    private void OnAnyShot(Entity<GunComponent> ent, ref AmmoShotEvent args)
    {
        var holder = Transform(ent).ParentUid;

        _lastShotPos = _transform.GetWorldPosition(ent);
        _lastShotTime = _timing.CurTime;
        _lastShotTeam = TryComp<MarineComponent>(holder, out var marine) ? marine.Team : "player";
    }

    private void StartBuild(EntityUid uid, MarineComponent comp, Vector2 pos, Vector2 targetPos, TransformComponent xform, TimeSpan now)
    {
        comp.NextBuild = now + TimeSpan.FromSeconds(6);

        var dir = targetPos - pos;
        if (dir.LengthSquared() < 0.01f)
            return;

        var spot = pos + Vector2.Normalize(dir) * 1.4f;
        var box = Box2.CenteredAround(spot, new Vector2(0.8f, 0.8f));

        if (_physics.GetCollidingEntities(xform.MapID, in box).Any())
            return;

        foreach (var (other, _) in _lookup.GetEntitiesInRange<MarineComponent>(new MapCoordinates(spot, xform.MapID), 0.8f, LookupFlags.Dynamic | LookupFlags.Uncontained))
        {
            if (other != uid)
                return;
        }

        comp.BuildAt = now + TimeSpan.FromSeconds(1.6);
        comp.BuildPos = spot;
        _transform.SetWorldRotation(uid, dir.ToWorldAngle());
        _popup.PopupEntity(Loc.GetString("marine-barricade-building"), uid, PopupType.Small);
    }

    private void FinishBuild(EntityUid uid, MarineComponent comp, TransformComponent xform, Vector2 pos, TimeSpan now)
    {
        comp.BuildAt = null;
        comp.Barricades++;
        comp.NextBuild = now + TimeSpan.FromSeconds(25);

        Spawn(comp.Barricade, xform.Coordinates.Offset(comp.BuildPos - pos));

        comp.NextCover = now;
        comp.LastDamaged = now;
    }

    public bool ForceShoot(EntityUid uid, EntityUid target)
    {
        if (!TryComp<MarineComponent>(uid, out var comp) || EnsureGun(uid, comp) is not { } gun)
            return false;

        var xform = Transform(uid);
        var from = _transform.GetWorldPosition(xform);
        var to = _transform.GetWorldPosition(target);

        if ((to - from).LengthSquared() > 0.01f)
            _transform.SetWorldRotation(uid, (to - from).ToWorldAngle());

        return Shoot(uid, gun, xform, to, target);
    }

    private void ThinkScripted(EntityUid uid, MarineComponent comp, Vector2 pos, MapId map, TimeSpan now)
    {
        if (comp.ScriptGoal is { } goal && (goal - pos).Length() > 0.9f)
        {
            comp.Running = false;
            Steer(uid, comp, pos, goal, Vector2.Zero, map, now);

            if (comp.MoveDir != Vector2.Zero)
                _transform.SetWorldRotation(uid, comp.MoveDir.ToWorldAngle());
        }
        else
        {
            comp.MoveDir = Vector2.Zero;
        }

        CheckStuck(uid, comp, pos, map, now);
    }

    private void ThinkCivilian(EntityUid uid, MarineComponent comp, Vector2 pos, MapId map, TimeSpan now)
    {
        comp.Home ??= pos;

        if (comp.StandUpAt != TimeSpan.Zero)
        {
            comp.MoveDir = Vector2.Zero;

            if (now >= comp.StandUpAt)
            {
                comp.StandUpAt = TimeSpan.Zero;
                _standing.Stand(uid);
            }

            return;
        }

        if (comp.Frozen || comp.Static)
        {
            comp.MoveDir = Vector2.Zero;
            return;
        }

        comp.Fear = MathF.Max(0f, comp.Fear - 0.0005f);
        comp.Hunger = MathF.Min(1f, comp.Hunger + 0.00018f);
        comp.Thirst = MathF.Min(1f, comp.Thirst + 0.00014f);

        var sep = GetSeparation(uid, comp, pos, map);

        var shotAge = _lastShotTime == TimeSpan.Zero ? TimeSpan.MaxValue : now - _lastShotTime;
        var shotDist = (_lastShotPos - pos).Length();
        var combatNearby = shotAge < TimeSpan.FromSeconds(8) && shotDist < 10f;

        if (comp.Scene == null && shotAge < TimeSpan.FromSeconds(3) && shotDist < 18f && now >= comp.PanicUntil)
        {
            comp.Fear = MathF.Min(1f, comp.Fear + 0.15f);
            comp.PanicUntil = now + TimeSpan.FromSeconds(8f + 8f * comp.Fear);
            comp.PanicFrom = _lastShotPos;
        }
        else if (comp.Scene == null && shotAge < TimeSpan.FromSeconds(1.5) && shotDist >= 18f && shotDist < 35f
                 && now >= comp.NextFlinch)
        {
            comp.NextFlinch = now + TimeSpan.FromSeconds(25);
            comp.ListenUntil = now + TimeSpan.FromSeconds(1.5);
            comp.Fear = MathF.Min(1f, comp.Fear + 0.05f);
            FacePoint(uid, _lastShotPos);
            _chat.TrySendInGameICMessage(uid, "вздрагивает и прислушивается", InGameICChatType.Emote, false);
        }

        if (now < comp.ListenUntil && comp.Scene == null)
        {
            comp.MoveDir = Vector2.Zero;
            return;
        }

        if (now >= comp.NextAwareness)
        {
            comp.NextAwareness = now + TimeSpan.FromSeconds(0.5);
            ScanSurroundings(uid, comp, pos, map, now);
        }

        if (comp.CivMode == MarineCivilianMode.Busy && comp.Scene == null && !comp.Partisan
            && (combatNearby || now < comp.PanicUntil))
        {
            comp.CivMode = MarineCivilianMode.Idle;
            comp.ScriptGoal = null;
        }

        if (comp.ScriptGoal is { } scripted)
        {
            if ((scripted - pos).Length() > 0.8f)
            {
                Steer(uid, comp, pos, scripted, sep, map, now);

                if (comp.MoveDir != Vector2.Zero)
                    _transform.SetWorldRotation(uid, comp.MoveDir.ToWorldAngle());
            }
            else
            {
                comp.MoveDir = Vector2.Zero;
            }

            CheckStuck(uid, comp, pos, map, now);
            return;
        }

        if (comp.CivMode == MarineCivilianMode.Busy)
        {
            comp.MoveDir = Vector2.Zero;
            return;
        }

        if (combatNearby || now < comp.PanicUntil)
        {
            EndCivilianActivity(uid, comp);

            if (now < comp.PanicUntil)
            {
                comp.Running = true;
                var away = pos - comp.PanicFrom;
                var goal = pos + (away.LengthSquared() > 0.01f ? Vector2.Normalize(away) : new Vector2(1f, 0f)) * 10f;
                Steer(uid, comp, pos, goal, sep, map, now);
                _transform.SetWorldRotation(uid, comp.MoveDir.ToWorldAngle());
            }
            else
            {
                comp.MoveDir = Vector2.Zero;
            }

            CheckStuck(uid, comp, pos, map, now);
            return;
        }

        if (comp.PoliceNear && comp.Boldness < 0.7f && comp.CivMode is MarineCivilianMode.Meeting or MarineCivilianMode.Talking)
        {
            if (now >= comp.NextSilence)
            {
                comp.NextSilence = now + TimeSpan.FromSeconds(20);
                _chat.TrySendInGameICMessage(uid, "резко замолкает и отводит взгляд", InGameICChatType.Emote, false);
            }

            EndCivilianActivity(uid, comp);
        }

        comp.Running = false;
        Vector2? target = null;
        var useSeparation = true;

        switch (comp.CivMode)
        {
            case MarineCivilianMode.Meeting:
                target = ThinkMeeting(uid, comp, pos, now);
                useSeparation = false;
                break;

            case MarineCivilianMode.Talking:
                ThinkTalking(uid, comp, pos, now);
                useSeparation = false;
                break;

            case MarineCivilianMode.Route:
                target = ThinkRoute(uid, comp, pos, now);
                break;

            default:
                target = ThinkIdleCivilian(uid, comp, pos, map, now);
                break;
        }

        if (target is { } t)
            Steer(uid, comp, pos, t, useSeparation ? sep : Vector2.Zero, map, now);
        else
            comp.MoveDir = Vector2.Zero;

        if (comp.MoveDir != Vector2.Zero)
            _transform.SetWorldRotation(uid, comp.MoveDir.ToWorldAngle());

        CheckStuck(uid, comp, pos, map, now);
    }

    private void ScanSurroundings(EntityUid uid, MarineComponent comp, Vector2 pos, MapId map, TimeSpan now)
    {
        comp.PoliceNear = false;
        var coords = new MapCoordinates(pos, map);
        var flags = LookupFlags.Dynamic | LookupFlags.Uncontained;

        foreach (var (other, otherComp) in _lookup.GetEntitiesInRange<MarineComponent>(coords, 6f, flags))
        {
            if (otherComp.Team != "north" || otherComp.Passive || _mobState.IsDead(other))
                continue;

            var otherPos = _transform.GetWorldPosition(other);

            if (!HasLineTo(uid, map, pos, otherPos))
                continue;

            comp.PoliceNear = true;
            comp.PoliceFrom = otherPos;
            break;
        }

        if (comp.Scene != null)
            return;

        foreach (var (player, _) in _lookup.GetEntitiesInRange<ActorComponent>(coords, 9f, flags))
        {
            var playerPos = _transform.GetWorldPosition(player);

            if (player == uid || !_reaction.IsMarkedHostile(player) || !HasLineTo(uid, map, pos, playerPos))
                continue;

            if (now >= comp.PanicUntil)
            {
                comp.PanicUntil = now + TimeSpan.FromSeconds(6);
                comp.PanicFrom = playerPos;
            }

            break;
        }
    }

    private bool HasLineTo(EntityUid uid, MapId map, Vector2 from, Vector2 to)
    {
        var delta = to - from;
        var len = delta.Length();

        return len < 0.5f || !Cast(null, map, from, delta / len, len, ShotMask, uid, out _, out _);
    }

    public void Frighten(Vector2 pos, MapId map, float radius, float amount, EntityUid? exclude, bool dropDown)
    {
        var now = _timing.CurTime;
        var flags = LookupFlags.Dynamic | LookupFlags.Uncontained;

        foreach (var (uid, comp) in _lookup.GetEntitiesInRange<MarineComponent>(new MapCoordinates(pos, map), radius, flags))
        {
            if (!comp.Passive || comp.Static || uid == exclude || comp.Scene != null || HasComp<ActorComponent>(uid)
                || _mobState.IsDead(uid))
            {
                continue;
            }

            var civPos = _transform.GetWorldPosition(uid);

            if (!dropDown && !HasLineTo(uid, map, civPos, pos))
                continue;

            comp.Fear = MathF.Min(1f, comp.Fear + amount * (1f - comp.Boldness * 0.5f));

            if (dropDown)
            {
                if (comp.CivMode == MarineCivilianMode.Busy)
                    comp.CivMode = MarineCivilianMode.Idle;

                comp.ScriptGoal = null;
                comp.PanicUntil = now + TimeSpan.FromSeconds(10);
                comp.PanicFrom = pos;
                comp.StandUpAt = now + TimeSpan.FromSeconds(2);
                _standing.Down(uid, false, false);
                continue;
            }

            if (comp.Fear >= 0.5f && now >= comp.PanicUntil)
            {
                comp.PanicUntil = now + TimeSpan.FromSeconds(6);
                comp.PanicFrom = pos;
            }

            if (now >= comp.NextFlinch)
            {
                comp.NextFlinch = now + TimeSpan.FromSeconds(10);
                _chat.TrySendInGameICMessage(uid, "в ужасе отворачивается", InGameICChatType.Emote, false);
            }
        }
    }

    private void EndCivilianActivity(EntityUid uid, MarineComponent comp)
    {
        if (comp.Partner is { } partner && TryComp<MarineComponent>(partner, out var other) && other.Partner == uid)
        {
            other.Partner = null;
            other.CivMode = MarineCivilianMode.Idle;
            other.Arrived = false;
        }

        comp.Partner = null;
        comp.CivMode = MarineCivilianMode.Idle;
        comp.Arrived = false;
        comp.RoutePoints = null;
    }

    private Vector2? ThinkIdleCivilian(EntityUid uid, MarineComponent comp, Vector2 pos, MapId map, TimeSpan now)
    {
        if (comp.PoliceNear && comp.Boldness < 0.7f)
        {
            var away = pos - comp.PoliceFrom;
            var dir = away.LengthSquared() > 0.01f ? Vector2.Normalize(away) : new Vector2(1f, 0f);
            return pos + dir * 3f;
        }

        if (now >= comp.NextActivity)
        {
            comp.NextActivity = now + TimeSpan.FromSeconds(_random.NextFloat(15f, 35f));
            var roll = _random.NextFloat();
            var scared = comp.Fear >= 0.5f;

            if (!scared && roll < 0.45f && TryStartConversation(uid, comp, pos, map, now))
                return null;

            if (!scared && roll < 0.7f && TryStartRoute(comp, pos, now))
                return null;

            comp.RoamPoint = PickRoamPoint(uid, comp, pos, map);
            comp.NextRoam = now + TimeSpan.FromSeconds(_random.NextFloat(8f, 16f));
        }

        if (comp.RoamPoint is { } point && (point - pos).Length() > 1.2f)
            return point;

        return null;
    }

    private Vector2 PickRoamPoint(EntityUid uid, MarineComponent comp, Vector2 pos, MapId map)
    {
        var home = comp.Home ?? pos;
        var scared = comp.Fear >= 0.5f;
        var spread = scared ? 6f : 10f;

        if (_random.Prob(scared ? 1f : 0.75f))
        {
            for (var i = 0; i < 12; i++)
            {
                var candidate = home + new Vector2(_random.NextFloat(-spread, spread), _random.NextFloat(-spread, spread));

                if (!IsRoofedAt(map, candidate))
                    continue;

                var delta = candidate - pos;
                var len = delta.Length();

                if (len < 1.5f || Cast(null, map, pos, delta / len, len, BlockMask, uid, out _, out _))
                    continue;

                return candidate;
            }
        }

        return home + new Vector2(_random.NextFloat(-8f, 8f), _random.NextFloat(-8f, 8f));
    }

    private bool IsRoofedAt(MapId map, Vector2 pos)
    {
        if (!_maps.TryFindGridAt(map, pos, out var gridUid, out var grid) || !TryComp<RoofComponent>(gridUid, out var roof))
            return false;

        var tile = _maps.WorldToTile(gridUid, grid, pos);
        return _roof.IsRooved((gridUid, grid, roof), tile);
    }

    private bool TryStartConversation(EntityUid uid, MarineComponent comp, Vector2 pos, MapId map, TimeSpan now)
    {
        var coords = new MapCoordinates(pos, map);
        var candidates = new List<(EntityUid Ent, MarineComponent Comp)>();

        foreach (var (other, otherComp) in _lookup.GetEntitiesInRange<MarineComponent>(coords, 12f, LookupFlags.Dynamic | LookupFlags.Uncontained))
        {
            if (other == uid || !otherComp.Passive || otherComp.Frozen || otherComp.Scene != null
                || otherComp.CivMode != MarineCivilianMode.Idle || now < otherComp.PanicUntil || _mobState.IsDead(other))
            {
                continue;
            }

            candidates.Add((other, otherComp));
        }

        if (candidates.Count == 0)
            return false;

        var (partner, partnerComp) = _random.Pick(candidates);
        var meet = FindMeetingPoint(uid, pos, map);
        var axis = Angle.FromDegrees(_random.NextFloat(0f, 360f)).ToWorldVec();

        comp.CivMode = MarineCivilianMode.Meeting;
        comp.Partner = partner;
        comp.MeetPoint = meet + axis * 0.7f;
        comp.Arrived = false;
        comp.Speaker = true;
        comp.MeetTimeout = now + TimeSpan.FromSeconds(25);

        partnerComp.CivMode = MarineCivilianMode.Meeting;
        partnerComp.Partner = uid;
        partnerComp.MeetPoint = meet - axis * 0.7f;
        partnerComp.Arrived = false;
        partnerComp.Speaker = false;
        partnerComp.MeetTimeout = now + TimeSpan.FromSeconds(25);
        return true;
    }

    private Vector2 FindMeetingPoint(EntityUid uid, Vector2 pos, MapId map)
    {
        var best = float.MaxValue;
        var found = false;
        var spot = pos;
        var points = EntityQueryEnumerator<MarineCivilianPointComponent, TransformComponent>();

        while (points.MoveNext(out _, out var point, out var xform))
        {
            if (!(point.Talk || point.Kind == MarinePointKind.Talk) || xform.MapID != map)
                continue;

            var p = _transform.GetWorldPosition(xform);
            var d = (p - pos).Length();

            if (d > 25f || d >= best)
                continue;

            var busy = false;
            foreach (var (_, other) in _lookup.GetEntitiesInRange<MarineComponent>(new MapCoordinates(p, map), 2f, LookupFlags.Dynamic | LookupFlags.Uncontained))
            {
                if (other.Passive && other.CivMode is MarineCivilianMode.Meeting or MarineCivilianMode.Talking)
                    busy = true;
            }

            if (busy)
                continue;

            best = d;
            spot = p;
            found = true;
        }

        if (found)
            return spot;

        for (var i = 0; i < 12; i++)
        {
            var candidate = pos + new Vector2(_random.NextFloat(-8f, 8f), _random.NextFloat(-8f, 8f));

            if (!IsRoofedAt(map, candidate))
                continue;

            var delta = candidate - pos;
            var len = delta.Length();

            if (len < 1.5f || Cast(null, map, pos, delta / len, len, BlockMask, uid, out _, out _))
                continue;

            return candidate;
        }

        return pos + new Vector2(_random.NextFloat(-2f, 2f), _random.NextFloat(-2f, 2f));
    }

    private Vector2? ThinkMeeting(EntityUid uid, MarineComponent comp, Vector2 pos, TimeSpan now)
    {
        if (comp.Partner is not { } partner || !TryComp<MarineComponent>(partner, out var other)
            || other.Partner != uid || _mobState.IsDead(partner) || now >= comp.MeetTimeout)
        {
            EndCivilianActivity(uid, comp);
            return null;
        }

        if ((comp.MeetPoint - pos).Length() > 0.5f)
        {
            comp.Arrived = false;
            return comp.MeetPoint;
        }

        comp.Arrived = true;
        FacePoint(uid, _transform.GetWorldPosition(partner));

        if (comp.Speaker && other.Arrived)
        {
            var until = now + TimeSpan.FromSeconds(_random.NextFloat(50f, 70f));
            comp.CivMode = MarineCivilianMode.Talking;
            comp.TalkUntil = until;
            comp.NextEmote = now + TimeSpan.FromSeconds(2);
            other.CivMode = MarineCivilianMode.Talking;
            other.TalkUntil = until;
            other.NextEmote = comp.NextEmote;
        }

        return null;
    }

    private void ThinkTalking(EntityUid uid, MarineComponent comp, Vector2 pos, TimeSpan now)
    {
        if (comp.Partner is not { } partner || !TryComp<MarineComponent>(partner, out var other)
            || other.Partner != uid || _mobState.IsDead(partner))
        {
            EndCivilianActivity(uid, comp);
            return;
        }

        var partnerPos = _transform.GetWorldPosition(partner);
        FacePoint(uid, partnerPos);

        if (now >= comp.TalkUntil)
        {
            if (comp.Fear > 0.3f)
                other.Fear = MathF.Min(1f, other.Fear + 0.08f);

            if (other.Fear > 0.3f)
                comp.Fear = MathF.Min(1f, comp.Fear + 0.08f);

            var away = pos - partnerPos;
            var dir = away.LengthSquared() > 0.01f ? Vector2.Normalize(away) : new Vector2(1f, 0f);

            EndCivilianActivity(uid, comp);
            comp.NextActivity = now + TimeSpan.FromSeconds(_random.NextFloat(20f, 40f));
            comp.RoamPoint = pos + dir * 6f;
            comp.NextRoam = now + TimeSpan.FromSeconds(8);
            return;
        }

        if (!comp.Speaker || now < comp.NextEmote)
            return;

        _chat.TrySendInGameICMessage(uid, _random.Pick(TalkEmotes), InGameICChatType.Emote, false);
        comp.Speaker = false;
        other.Speaker = true;
        comp.NextEmote = now + TimeSpan.FromSeconds(10);
        other.NextEmote = comp.NextEmote;
    }

    private bool TryStartRoute(MarineComponent comp, Vector2 pos, TimeSpan now)
    {
        var map = MapId.Nullspace;
        var routes = new Dictionary<string, List<(int Order, Vector2 Pos)>>();
        var points = EntityQueryEnumerator<MarineCivilianPointComponent, TransformComponent>();

        while (points.MoveNext(out _, out var point, out var xform))
        {
            if (point.Talk || point.Kind != MarinePointKind.Walk)
                continue;

            if (comp.Route.Length > 0 && point.Route != comp.Route)
                continue;

            map = xform.MapID;
            var p = _transform.GetWorldPosition(xform);

            if ((p - pos).Length() > 60f)
                continue;

            if (!routes.TryGetValue(point.Route, out var list))
            {
                list = new List<(int, Vector2)>();
                routes[point.Route] = list;
            }

            list.Add((point.Order, p));
        }

        if (routes.Count == 0)
            return false;

        var chosen = _random.Pick(new List<List<(int Order, Vector2 Pos)>>(routes.Values));

        if (chosen.Count < 2)
            return false;

        chosen.Sort((a, b) => a.Order.CompareTo(b.Order));

        var nearest = 0;
        var bestDist = float.MaxValue;
        for (var i = 0; i < chosen.Count; i++)
        {
            var d = (chosen[i].Pos - pos).Length();
            if (d < bestDist)
            {
                bestDist = d;
                nearest = i;
            }
        }

        comp.RoutePoints = chosen.ConvertAll(x => x.Pos);
        comp.RouteIndex = nearest;
        comp.CivMode = MarineCivilianMode.Route;
        return true;
    }

    private Vector2? ThinkRoute(EntityUid uid, MarineComponent comp, Vector2 pos, TimeSpan now)
    {
        if (comp.RoutePoints == null || comp.RouteIndex >= comp.RoutePoints.Count)
        {
            comp.RoutePoints = null;
            comp.CivMode = MarineCivilianMode.Idle;
            comp.NextActivity = now + TimeSpan.FromSeconds(_random.NextFloat(10f, 20f));
            return null;
        }

        var goal = comp.RoutePoints[comp.RouteIndex];

        if ((goal - pos).Length() < 1.5f)
        {
            comp.RouteIndex++;
            return null;
        }

        return goal;
    }

    private void FacePoint(EntityUid uid, Vector2 point)
    {
        var delta = point - _transform.GetWorldPosition(uid);
        if (delta.LengthSquared() > 0.01f)
            _transform.SetWorldRotation(uid, delta.ToWorldAngle());
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

            foreach (var (ent, _) in _lookup.GetEntitiesInRange<MarineComponent>(coords, 60f, flags))
            {
                if (!IsEnemy(comp, ent))
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

            if (comp.AvoidSide != 0)
                LearnPassage(pos, map, now);

            comp.AvoidSide = 0;
            return;
        }

        if (now >= comp.AvoidUntil || comp.AvoidSide == 0)
        {
            var left = Probe(map, pos, Angle.FromDegrees(60f).RotateVec(want), Lookahead, uid, out var leftDist, out _) ? leftDist : Lookahead;
            var right = Probe(map, pos, Angle.FromDegrees(-60f).RotateVec(want), Lookahead, uid, out var rightDist, out _) ? rightDist : Lookahead;

            comp.AvoidSide = left > right ? 1 : right > left ? -1 : (_random.Prob(0.5f) ? 1 : -1);
            comp.AvoidUntil = now + TimeSpan.FromSeconds(3.5);
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

            if (comp.AvoidSide != 0 && MathF.Abs(angle) >= 25f && MathF.Abs(angle) < 160f)
                score += MathF.Sign(angle) == comp.AvoidSide ? 0.9f : -0.4f;

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
