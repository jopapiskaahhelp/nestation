// Nestation Anarhey CC BY-NC 3.0
using System.Numerics;
using Content.Server.NPC.HTN;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Physics;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.XenoBrain;

public sealed partial class XenoBrainSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private IComponentFactory _compFactory = default!;

    public const int BlockMask = (int) CollisionGroup.MobMask;
    public const int OpaqueMask = (int) CollisionGroup.Opaque;

    private readonly record struct PlayerInfo(EntityUid Uid, MapId Map, Vector2 Pos);
    private readonly record struct XenoInfo(EntityUid Uid, XenoBrainComponent Brain, MapId Map, Vector2 Pos);
    private readonly record struct Ctx(
        XenoInfo X, TimeSpan Now, int PackSize, XenoInfo Leader,
        EntityUid? Target, bool TargetIsPlayer, Vector2 TargetPos, float TargetDist, int MapXenos,
        float NearestDist, bool Closing);

    private readonly List<PlayerInfo> _players = new();
    private readonly List<XenoInfo> _xenos = new();
    private readonly List<XenoInfo> _pack = new();
    private readonly List<EntityUid> _newXenos = new();
    private readonly List<Vector2> _gapBuf = new();

    private readonly Dictionary<EntityUid, float> _threat = new();
    private readonly List<EntityUid> _tmpKeys = new();
    private readonly List<(MapId Map, Vector2 Pos, TimeSpan Expire)> _danger = new();
    private readonly HashSet<EntityUid> _unbreakable = new();
    private readonly List<Passage> _passages = new();

    private sealed class Passage
    {
        public MapId Map;
        public Vector2 Entry;
        public Vector2 Exit;
        public TimeSpan Expire;
    }

    private float _accum;
    private TimeSpan _lastDecay;
    private TimeSpan _nextScan;

    public override void Initialize()
    {
        base.Initialize();
        InitializeChat();
        SubscribeLocalEvent<XenoBrainComponent, DamageChangedEvent>(OnDamage);
        SubscribeLocalEvent<XenoBrainComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnDamage(Entity<XenoBrainComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased)
            return;

        var b = ent.Comp;
        var now = _timing.CurTime;
        b.LastHitTime = now;

        if (args.Origin is not { } origin || origin == ent.Owner || HasComp<XenoBrainComponent>(origin))
            return;

        if (HasComp<ActorComponent>(origin))
        {
            var dmg = args.DamageDelta?.GetTotal().Float() ?? 0f;
            var originPos = _transform.GetWorldPosition(origin);
            var ranged = (originPos - _transform.GetWorldPosition(ent.Owner)).Length() > 3.2f;
            LearnHit(origin, dmg, ranged, originPos, Transform(ent.Owner).MapID, now);

            b.RecentHits.Add(now);
            b.LastPlayerAttacker = origin;
            b.LastPlayerAttackTime = now;
            b.ThreatPos = originPos;
            AddThreat(origin, 0.3f + dmg / 30f);

            if (b.LuredBy != null && (now - b.LuredAt).TotalSeconds < 15)
            {
                b.LureHits++;
                if (b.LureHits >= 3)
                    PunishLure(b);
            }
        }
        else
        {
            b.Retaliator = origin;
            b.RetaliatorTime = now;
        }
    }

    private void OnRefreshSpeed(Entity<XenoBrainComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.SpeedMult, ent.Comp.SpeedMult);
    }

    public float GetThreat(EntityUid player) => _threat.TryGetValue(player, out var v) ? v : 0f;

    public bool IsDangerous(MapId map, Vector2 pos)
    {
        foreach (var d in _danger)
        {
            if (d.Map == map && (d.Pos - pos).Length() < 10f)
                return true;
        }

        return false;
    }

    public bool RayHit(MapId map, Vector2 from, Vector2 dir, float len, int mask, EntityUid? ignore,
        out float dist, out Vector2 hitPos, out EntityUid hit)
    {
        dist = len;
        hitPos = from + dir * len;
        hit = default;

        if (len < 0.05f)
            return false;

        var ray = new CollisionRay(from, dir, mask);
        var found = false;
        foreach (var r in _physics.IntersectRay(map, ray, len, ignore, false))
        {
            if (!found || r.Distance < dist)
            {
                found = true;
                dist = r.Distance;
                hitPos = r.HitPos;
                hit = r.HitEntity;
            }
        }

        return found;
    }

    public Vector2 ForwardDir(EntityUid uid, XenoBrainComponent b, Vector2 pos)
    {
        if (b.GoalPos is { } g && (g - pos).LengthSquared() > 0.04f)
            return Vector2.Normalize(g - pos);

        return _transform.GetWorldRotation(uid).ToWorldVec();
    }

    public void FindGaps(MapId map, Vector2 pos, Vector2 forward, float maxLateral, EntityUid ignore, List<Vector2> result)
    {
        result.Clear();

        var perp = new Vector2(-forward.Y, forward.X);
        var steps = (int) (maxLateral / 0.9f);
        var openPlus = true;
        var openMinus = true;

        for (var i = 1; i <= steps; i++)
        {
            for (var s = 0; s < 2; s++)
            {
                if (s == 0 ? !openPlus : !openMinus)
                    continue;

                var sign = s == 0 ? 1f : -1f;
                var from = pos + perp * sign * ((i - 1) * 0.9f);
                var origin = pos + perp * sign * (i * 0.9f);

                if (RayHit(map, from, perp * sign, 0.9f, BlockMask, ignore, out _, out _, out _))
                {
                    if (s == 0)
                        openPlus = false;
                    else
                        openMinus = false;

                    continue;
                }

                if (!ClearForward(map, origin, forward, 3f, ignore))
                    continue;

                var via = origin + forward * 2.5f;
                var dup = false;
                foreach (var r in result)
                {
                    if ((r - via).Length() < 2.5f)
                        dup = true;
                }

                if (!dup)
                    result.Add(via);
            }
        }
    }

    public Vector2 StrikeGoal(XenoBrainComponent b, Vector2 me, Vector2 targetPos, Vector2 targetVel,
        float orbitRadius, float leadSeconds, float fanRadius, float fanSwitch)
    {
        var center = targetPos + targetVel * leadSeconds;
        var toMe = me - targetPos;
        var dist = toMe.Length();
        var bearingMe = dist > 0.01f ? MathF.Atan2(toMe.Y, toMe.X) : 0f;

        Vector2 goal;
        if (dist >= fanSwitch)
        {
            var toLeader = b.PackLeaderPos - targetPos;
            float bearing;
            if (b.TargetRanged && b.TargetFacing.LengthSquared() > 0.01f)
                bearing = MathF.Atan2(-b.TargetFacing.Y, -b.TargetFacing.X);
            else if (toLeader.LengthSquared() > 0.01f)
                bearing = MathF.Atan2(toLeader.Y, toLeader.X);
            else
                bearing = bearingMe;
            var idx = b.PackIndex;
            var side = idx == 0 ? 0f : (idx % 2 == 1 ? 1f : -1f);
            var rank = (idx + 1) / 2;
            var ang = bearing + side * rank * 0.9f;
            goal = center + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * fanRadius;
        }
        else
        {
            var sign = b.PackIndex % 2 == 0 ? 1f : -1f;
            var ang = bearingMe + sign * 0.9f;
            goal = center + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * orbitRadius;
        }

        foreach (var ally in b.Allies)
        {
            var d = goal - ally;
            var len = d.Length();
            if (len < 1.8f)
                goal += (len > 0.05f ? d / len : _random.NextAngle().ToVec()) * (1.8f - len);
        }

        return goal;
    }

    private bool ClearForward(MapId map, Vector2 origin, Vector2 forward, float len, EntityUid ignore)
    {
        var side = new Vector2(-forward.Y, forward.X) * 0.35f;
        return !RayHit(map, origin, forward, len, BlockMask, ignore, out _, out _, out _)
               && !RayHit(map, origin + side, forward, len, BlockMask, ignore, out _, out _, out _)
               && !RayHit(map, origin - side, forward, len, BlockMask, ignore, out _, out _, out _);
    }

    public override void Update(float frameTime)
    {
        _accum += frameTime;
        if (_accum < 0.1f)
            return;

        _accum = 0f;
        var now = _timing.CurTime;

        ScanForNewXenos(now);
        Snapshot(now);
        DecayMemory(now);

        foreach (var x in _xenos)
        {
            if (now < x.Brain.NextThink)
                continue;

            x.Brain.NextThink = now + TimeSpan.FromSeconds(0.15 + _random.NextFloat() * 0.1);
            Think(x, now);
        }
    }

    private void ScanForNewXenos(TimeSpan now)
    {
        if (now < _nextScan)
            return;

        _nextScan = now + TimeSpan.FromSeconds(2);

        _newXenos.Clear();
        var q = EntityQueryEnumerator<HTNComponent>();
        while (q.MoveNext(out var uid, out var htn))
        {
            if (HasComp<XenoBrainComponent>(uid))
                continue;

            if (htn.RootTask.Task.ToString() != "XenoCompound")
                continue;

            _newXenos.Add(uid);
        }

        foreach (var uid in _newXenos)
        {
            var brain = EnsureComp<XenoBrainComponent>(uid);
            if (TryComp<HTNComponent>(uid, out var htn))
                htn.PlanCooldown = brain.PlanCooldown;
        }
    }

    private void Snapshot(TimeSpan now)
    {
        _players.Clear();
        _xenos.Clear();

        var pq = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (pq.MoveNext(out var uid, out _, out var ms, out var xf))
        {
            if (!_mobState.IsAlive(uid, ms) || HasComp<XenoBrainComponent>(uid))
                continue;

            _players.Add(new PlayerInfo(uid, xf.MapID, _transform.GetWorldPosition(xf)));
        }

        var xq = EntityQueryEnumerator<XenoBrainComponent, TransformComponent>();
        while (xq.MoveNext(out var uid, out var brain, out var xf))
        {
            if (xf.MapID == MapId.Nullspace || HasComp<ActorComponent>(uid))
                continue;

            var pos = _transform.GetWorldPosition(xf);

            if (_mobState.IsDead(uid))
            {
                if (!brain.DeathRecorded)
                {
                    brain.DeathRecorded = true;
                    LearnFromDeath(brain, now);
                    if (brain.LastPlayerAttacker is { } killer && (now - brain.LastPlayerAttackTime).TotalSeconds < 15)
                    {
                        AddThreat(killer, 3f);
                        AddDanger(xf.MapID, pos, now);
                    }
                }

                continue;
            }

            _xenos.Add(new XenoInfo(uid, brain, xf.MapID, pos));
        }
    }

    private void DecayMemory(TimeSpan now)
    {
        if ((now - _lastDecay).TotalSeconds < 1.0)
            return;

        var dt = (float) (now - _lastDecay).TotalSeconds;
        _lastDecay = now;

        _tmpKeys.Clear();
        _tmpKeys.AddRange(_threat.Keys);
        foreach (var k in _tmpKeys)
        {
            var v = _threat[k] - dt * 0.03f;
            if (v <= 0f || Deleted(k))
                _threat.Remove(k);
            else
                _threat[k] = v;
        }

        _danger.RemoveAll(d => d.Expire < now);
        _passages.RemoveAll(p => p.Expire < now);
        PruneLearning();
        _unbreakable.RemoveWhere(e => Deleted(e));
    }

    private void AddThreat(EntityUid player, float amount)
    {
        _threat[player] = GetThreat(player) + amount;
    }

    private void AddDanger(MapId map, Vector2 pos, TimeSpan now)
    {
        _danger.Add((map, pos, now + TimeSpan.FromSeconds(90)));
        if (_danger.Count > 40)
            _danger.RemoveAt(0);
    }

    private void Think(XenoInfo x, TimeSpan now)
    {
        var b = x.Brain;
        b.RecentHits.RemoveAll(t => (now - t).TotalSeconds > b.HitWindow);
        b.AvoidPoints.RemoveAll(a => a.Expire < now);

        PlayerInfo? near = null;
        PlayerInfo? nearest = null;
        var nearDist = float.MaxValue;
        var nearestDist = float.MaxValue;
        foreach (var p in _players)
        {
            if (p.Map != x.Map)
                continue;

            var d = (p.Pos - x.Pos).Length();
            if (d < nearestDist)
            {
                nearestDist = d;
                nearest = p;
            }

            if (d <= b.SightRange && d < nearDist)
            {
                nearDist = d;
                near = p;
            }
        }

        var closing = b.PrevPlayerDist < float.MaxValue && nearestDist < float.MaxValue
                      && b.PrevPlayerDist - nearestDist >= 0.35f;
        b.PrevPlayerDist = nearestDist;

        _pack.Clear();
        var mapXenos = 0;
        foreach (var a in _xenos)
        {
            if (a.Map != x.Map)
                continue;

            mapXenos++;
            if ((a.Pos - x.Pos).Length() <= b.PackRadius)
                _pack.Add(a);
        }

        var leader = x;
        foreach (var a in _pack)
        {
            if (a.Uid.Id < leader.Uid.Id)
                leader = a;
        }

        b.Allies.Clear();
        var packIndex = 0;
        foreach (var a in _pack)
        {
            if (a.Uid == x.Uid)
                continue;

            b.Allies.Add(a.Pos);
            if (a.Uid.Id < x.Uid.Id)
                packIndex++;
        }

        b.PackIndex = packIndex;
        b.PackLeaderPos = leader.Pos;

        EntityUid? target = null;
        var targetIsPlayer = false;
        var targetPos = Vector2.Zero;

        PlayerInfo lead = default;
        var leaderHasTarget = leader.Uid != x.Uid
                              && leader.Brain.Target is { } lt
                              && TryGetPlayer(lt, x.Map, out lead)
                              && (lead.Pos - x.Pos).Length() <= b.SightRange * 1.5f;

        if (leaderHasTarget)
        {
            target = lead.Uid;
            targetPos = lead.Pos;
            targetIsPlayer = true;
        }
        else if (near is { } n)
        {
            target = n.Uid;
            targetPos = n.Pos;
            targetIsPlayer = true;
        }
        else if (TryGetRetaliator(x, now, out var rt, out var rpos))
        {
            target = rt;
            targetPos = rpos;
        }
        else if (nearest is { } f)
        {
            target = f.Uid;
            targetPos = f.Pos;
            targetIsPlayer = true;
        }

        if (target != null && now < b.NoHuntUntil && (targetPos - x.Pos).Length() > 6f)
        {
            target = null;
            targetIsPlayer = false;
        }

        if (b.ForcedTarget is { } forced)
        {
            if (now < b.ForcedUntil && TryGetPlayer(forced, x.Map, out var forcedInfo))
            {
                target = forcedInfo.Uid;
                targetPos = forcedInfo.Pos;
                targetIsPlayer = true;
            }
            else
            {
                if (now < b.ForcedUntil && b.LuredBy == forced)
                    RewardLure(b);

                b.ForcedTarget = null;
            }
        }

        b.TargetRanged = false;
        if (target != null && targetIsPlayer)
        {
            b.TargetRanged = IsRangedDanger(target.Value, now);
            if (b.TargetRanged)
                b.TargetFacing = _transform.GetWorldRotation(target.Value).ToWorldVec();
        }

        b.Target = target;
        if (target != null)
            b.GoalPos = targetPos;
        else if (leader.Uid != x.Uid)
            b.GoalPos = leader.Pos;
        else
            b.GoalPos = null;

        var ctx = new Ctx(x, now, _pack.Count, leader, target, targetIsPlayer, targetPos,
            target != null ? (targetPos - x.Pos).Length() : float.MaxValue, mapXenos, nearestDist, closing);

        b.TargetDist = ctx.TargetDist;

        if (b.Mode is not (XenoMode.Retreat or XenoMode.Ambush) && ShouldRetreat(b, now, ctx.PackSize))
        {
            StartRetreat(x, now);
        }
        else
        {
            switch (b.Mode)
            {
                case XenoMode.Retreat:
                    ThinkRetreat(ctx);
                    break;
                case XenoMode.Ambush:
                    ThinkAmbush(ctx);
                    break;
                case XenoMode.Detour:
                {
                    var reached = b.DetourPoint is { } dp && (dp - x.Pos).Length() < 2.5f;
                    if (reached && b.StuckPoint is { } sp && b.GoalPos is { } goalPos)
                        RecordIfCrossing(x.Map, sp, x.Pos, goalPos, now);

                    if (reached || (now - b.ModeSince).TotalSeconds > 8.0)
                        Decide(ctx);

                    break;
                }
                default:
                    Decide(ctx);
                    break;
            }
        }

        UpdateSpeed(x, now);
        CheckStuck(ctx);
        if (b.SmashActive && b.SmashTarget is { } smashed && TerminatingOrDeleted(smashed))
        {
            RecordPassage(x.Map, b.SmashFrom, b.SmashPos + b.SmashDir * 2.2f, now);
            b.SmashActive = false;
            b.SmashTarget = null;
        }

        TrySmash(x);
    }

    private void Decide(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;
        var elapsed = (now - b.ModeSince).TotalSeconds;

        if (b.Mode == XenoMode.Rally && elapsed > 25)
            b.NoRallyUntil = now + TimeSpan.FromSeconds(20);
        if (b.Mode == XenoMode.Flank && elapsed > 12)
            b.NoFlankUntil = now + TimeSpan.FromSeconds(20);

        if (b.ForcedTarget != null && now < b.ForcedUntil && c.Target == b.ForcedTarget)
        {
            SetMode(x, XenoMode.Attack, now);
            return;
        }

        XenoMode want;

        if (c.Target is not { } target)
        {
            if (c.Leader.Uid != x.Uid)
            {
                b.RallyTarget = c.Leader.Uid;
                want = XenoMode.Rally;
            }
            else
            {
                want = XenoMode.Patrol;
            }
        }
        else if (!c.TargetIsPlayer)
        {
            want = XenoMode.Attack;
        }
        else
        {
            var required = RequiredPack(target, c.MapXenos, b.TargetRanged);
            var engaged = false;
            foreach (var a in _pack)
            {
                if (a.Uid != x.Uid && (a.Pos - c.TargetPos).Length() < 6f)
                    engaged = true;
            }

            var leaping = now < b.LeapUntil;

            if (!engaged && !leaping && c.PackSize < required && c.TargetDist > 7f)
            {
                var other = NearestOtherXeno(x);
                if (other is { } o && now >= b.NoRallyUntil)
                {
                    b.RallyTarget = o.Uid;
                    want = XenoMode.Rally;
                }
                else if (now >= b.NoLurkUntil)
                {
                    b.AmbushUntil = now + TimeSpan.FromSeconds(b.AmbushSeconds);
                    want = XenoMode.Ambush;
                }
                else
                {
                    want = XenoMode.Attack;
                }
            }
            else if (!leaping && (c.PackSize == 2 || (c.PackSize >= 3 && b.TargetRanged)) && c.TargetDist < 16f && TrySkirmish(c, out var striker))
            {
                b.LastSkirmishAt = now;

                if (striker == x.Uid)
                {
                    if (c.TargetDist <= 2.8f)
                    {
                        if (b.ContactSince == TimeSpan.Zero)
                            b.ContactSince = now;

                        var gotHit = b.LastHitTime > b.ContactSince;
                        if ((now - b.ContactSince).TotalSeconds >= b.StrikeContactSeconds || gotHit)
                        {
                            b.ContactSince = TimeSpan.Zero;
                            c.Leader.Brain.Striker = NextStriker(x.Uid);

                            want = XenoMode.Kite;
                        }
                        else
                        {
                            want = XenoMode.Attack;
                        }
                    }
                    else
                    {
                        b.ContactSince = TimeSpan.Zero;
                        want = XenoMode.Attack;
                    }
                }
                else if (c.NearestDist <= 3.5f)
                {
                    Leap(x, now);
                    return;
                }
                else
                {
                    want = XenoMode.Kite;
                }
            }
            else
            {
                want = XenoMode.Attack;
            }
        }

        SetMode(x, want, now);
    }

    private bool TrySkirmish(Ctx c, out EntityUid striker)
    {
        striker = default;
        var l = c.Leader.Brain;

        if (c.Leader.Uid == c.X.Uid && c.Now >= l.SkirmishRollAt)
        {
            l.SkirmishActive = _random.Prob(Math.Clamp(c.X.Brain.SkirmishChance + _skirmishBias, 0f, 1f));
            l.SkirmishRollAt = c.Now + TimeSpan.FromSeconds(40);
        }

        if (c.X.Brain.TargetRanged && !l.SkirmishActive)
        {
            l.SkirmishActive = true;
            l.SkirmishRollAt = c.Now + TimeSpan.FromSeconds(40);
        }

        if (!l.SkirmishActive)
            return false;

        var current = l.Striker;
        var valid = false;
        if (current is { } cu)
        {
            foreach (var a in _pack)
            {
                if (a.Uid == cu)
                    valid = true;
            }
        }

        if (!valid)
        {
            current = c.Leader.Uid;
            l.Striker = current;
        }

        striker = current!.Value;
        return true;
    }

    private bool ShouldRetreat(XenoBrainComponent b, TimeSpan now, int packSize)
    {
        if (now < b.NoRetreatUntil || b.RecentHits.Count < b.RetreatHits)
            return false;

        if (b.LastPlayerAttacker is not { } attacker)
            return false;

        if (packSize >= 3 && GetThreat(attacker) < 2f)
            return false;

        return true;
    }

    private void StartRetreat(XenoInfo x, TimeSpan now)
    {
        var b = x.Brain;
        b.RecentHits.Clear();
        b.RetreatSpot = null;

        if (b.LastPlayerAttacker is { } a && Exists(a))
            b.ThreatPos = _transform.GetWorldPosition(a);

        AddDanger(x.Map, x.Pos, now);
        SetMode(x, XenoMode.Retreat, now);
    }

    private void ThinkRetreat(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;
        var elapsed = (c.Now - b.ModeSince).TotalSeconds;

        if (elapsed >= 1.0
            && (c.NearestDist <= b.RetreatEngageRange || (c.Closing && c.NearestDist <= b.ClosingEngageRange)))
        {
            Leap(x, c.Now);
            return;
        }

        if (b.LastPlayerAttacker is { } att
            && (c.Now - b.LastPlayerAttackTime).TotalSeconds < 1.5
            && b.RecentHits.Count >= 2
            && Exists(att)
            && (_transform.GetWorldPosition(att) - x.Pos).Length() < 3f)
        {
            Leap(x, c.Now);
            return;
        }

        var reached = b.RetreatSpot is { } s && (s - x.Pos).Length() < 2.5f;
        if (reached || elapsed > b.RetreatMaxSeconds)
        {
            b.AmbushUntil = c.Now + TimeSpan.FromSeconds(b.AmbushSeconds);
            SetMode(x, XenoMode.Ambush, c.Now);
        }
    }

    private void ThinkAmbush(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;

        if (c.NearestDist <= b.AmbushTriggerRange
            || (c.Closing && c.NearestDist <= b.AmbushTriggerRange + 3f))
        {
            Leap(x, c.Now);
            return;
        }

        if (c.PackSize >= 2 && c.Leader.Uid != x.Uid
            && c.Leader.Brain.Mode == XenoMode.Attack && c.Leader.Brain.Target != null)
        {
            SetMode(x, XenoMode.Attack, c.Now);
            return;
        }

        if (c.Now >= b.AmbushUntil)
        {
            var cautious = c.Now + TimeSpan.FromSeconds(b.CautiousSeconds);
            b.CautiousUntil = cautious;
            b.NoLurkUntil = cautious;
            SetMode(x, XenoMode.Patrol, c.Now);
        }
    }

    private void Leap(XenoInfo x, TimeSpan now)
    {
        var b = x.Brain;
        b.LeapUntil = now + TimeSpan.FromSeconds(b.LeapSeconds);
        b.NoRetreatUntil = now + TimeSpan.FromSeconds(b.NoRetreatSeconds);
        b.NoLurkUntil = now + TimeSpan.FromSeconds(20);
        SetMode(x, XenoMode.Attack, now);
        UpdateSpeed(x, now);
    }

    private void CheckStuck(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;

        bool wants;
        switch (b.Mode)
        {
            case XenoMode.Ambush:
                wants = false;
                break;
            case XenoMode.Attack:
                wants = c.Target != null && (c.TargetDist > 2.5f || LineBlocked(x, c.TargetPos));
                break;
            case XenoMode.Kite:
                wants = c.Target != null && (c.TargetDist < 8.5f || c.TargetDist > 14f);
                break;
            case XenoMode.Rally:
                wants = b.RallyTarget is { } rt && !TerminatingOrDeleted(rt)
                        && (_transform.GetWorldPosition(rt) - x.Pos).Length() > 5.5f;
                break;
            default:
                wants = true;
                break;
        }

        var moved = (x.Pos - b.StuckAnchor).Length() >= b.StuckMinMove;
        if (!wants || moved)
        {
            if (moved)
                b.SmashActive = false;

            b.StuckAnchor = x.Pos;
            b.StuckSince = c.Now;
            return;
        }

        var limit = b.Mode == XenoMode.Attack && c.Target != null ? b.StuckSecondsAggro : b.StuckSeconds;
        if ((c.Now - b.StuckSince).TotalSeconds < limit)
            return;

        b.StuckAnchor = x.Pos;
        b.StuckSince = c.Now;
        OnStuck(x, c.Now);
    }

    private void OnStuck(XenoInfo x, TimeSpan now)
    {
        var b = x.Brain;

        b.AvoidPoints.Add((x.Pos, now + TimeSpan.FromSeconds(60)));
        if (b.AvoidPoints.Count > 8)
            b.AvoidPoints.RemoveAt(0);

        if (HandleObstacle(x, now))
            return;

        if (now < b.PersistUntil)
            return;

        if ((x.Pos - b.LastDetourStuckPos).Length() < 4f && (now - b.LastDetourTime).TotalSeconds < 40)
            b.DetourCount++;
        else
            b.DetourCount = 1;

        b.LastDetourStuckPos = x.Pos;
        b.LastDetourTime = now;

        if (b.DetourCount > b.MaxDetoursPerSpot)
        {
            b.DetourCount = 0;

            if (b.SmashTarget is { } st && _unbreakable.Contains(st))
            {
                b.NoHuntUntil = now + TimeSpan.FromSeconds(15);
            }
            else
            {
                b.PersistUntil = now + TimeSpan.FromSeconds(15);
            }

            return;
        }

        b.StuckPoint = x.Pos;
        b.DetourPoint = null;
        SetMode(x, XenoMode.Detour, now);
        b.ModeSince = now;
    }

    private bool HandleObstacle(XenoInfo x, TimeSpan now)
    {
        var b = x.Brain;
        var forward = ForwardDir(x.Uid, b, x.Pos);

        var ob = FindObstacle(x, forward);
        if (ob is not { } obstacle)
        {
            b.SmashActive = false;
            return false;
        }

        if (_unbreakable.Contains(obstacle))
        {
            b.SmashTarget = obstacle;
            b.SmashActive = false;
            return false;
        }

        if (!CanXenoBreak(obstacle, b))
        {
            _unbreakable.Add(obstacle);
            b.SmashTarget = obstacle;
            b.SmashActive = false;
            return false;
        }

        if (b.SmashActive && b.SmashTarget == obstacle)
            return true;

        b.SmashTarget = obstacle;

        FindGaps(x.Map, x.Pos, forward, 5f, x.Uid, _gapBuf);
        var bypassNearby = _gapBuf.Count > 0;
        var detoursFailed = (x.Pos - b.LastDetourStuckPos).Length() < 4f
                            && (now - b.LastDetourTime).TotalSeconds < 40
                            && b.DetourCount >= b.MaxDetoursPerSpot;

        if (!bypassNearby || detoursFailed)
        {
            b.SmashActive = true;
            b.SmashFrom = x.Pos;
            b.SmashPos = _transform.GetWorldPosition(obstacle);
            b.SmashDir = forward;
            return true;
        }

        b.SmashActive = false;
        return false;
    }

    private bool CanXenoBreak(EntityUid ob, XenoBrainComponent b)
    {
        if (!HasComp<DamageableComponent>(ob))
            return false;

        foreach (var name in b.UnbreakableComponents)
        {
            if (_compFactory.TryGetRegistration(name, out var reg) && HasComp(ob, reg.Type))
                return false;
        }

        var proto = MetaData(ob).EntityPrototype?.ID;
        if (proto != null)
        {
            foreach (var prefix in b.UnbreakablePrefixes)
            {
                if (proto.StartsWith(prefix, StringComparison.Ordinal))
                    return false;
            }
        }

        return true;
    }

    private bool LineBlocked(XenoInfo x, Vector2 to)
    {
        var delta = to - x.Pos;
        var len = delta.Length();
        if (len < 0.3f)
            return false;

        return RayHit(x.Map, x.Pos, delta / len, MathF.Max(0.1f, len - 0.2f), BlockMask, x.Uid,
            out _, out _, out _);
    }

    private void TrySmash(XenoInfo x)
    {
        var b = x.Brain;
        if (!b.SmashActive || b.SmashTarget is not { } ob || TerminatingOrDeleted(ob))
            return;

        if ((_transform.GetWorldPosition(ob) - x.Pos).Length() > 2.5f)
            return;

        if (_melee.TryGetWeapon(x.Uid, out var weaponUid, out var weapon))
            _melee.AttemptLightAttack(x.Uid, weaponUid, weapon, ob);
    }

    public Vector2? GetBypass(EntityUid owner, XenoBrainComponent b, MapId map, Vector2 me, Vector2 goal, TimeSpan now)
    {
        if (b.Route.Count > 0)
        {
            if (now > b.BypassUntil)
            {
                b.Route.Clear();
            }
            else
            {
                while (b.Route.Count > 0 && (b.Route[0] - me).Length() < 1.5f)
                {
                    b.Route.RemoveAt(0);
                }

                if (b.Route.Count > 0)
                    return b.Route[0];

                FinishRoute(b, map, me, goal, now);
            }
        }

        var delta = goal - me;
        var len = delta.Length();
        if (len < 0.6f)
            return null;

        var dir = delta / len;
        if (!RayHit(map, me, dir, MathF.Min(len, 12f), BlockMask, owner, out _, out _, out var hit))
            return null;

        if (b.SmashActive && b.SmashTarget == hit)
            return null;

        if (TryGetPassageRoute(map, me, goal, now, out var entry, out var exit))
        {
            b.Route.Clear();
            if ((entry - me).Length() > 2f)
                b.Route.Add(entry);

            b.Route.Add(exit);
            b.RouteStart = me;
            b.RouteKnown = true;
            b.BypassUntil = now + TimeSpan.FromSeconds(12);
            return b.Route[0];
        }

        FindGaps(map, me, dir, 6f, owner, _gapBuf);
        foreach (var gap in _gapBuf)
        {
            var avoided = false;
            foreach (var (pos, _) in b.AvoidPoints)
            {
                if ((pos - gap).Length() < 2f)
                    avoided = true;
            }

            if (avoided)
                continue;

            b.Route.Clear();
            b.Route.Add(gap);
            b.RouteStart = me;
            b.RouteKnown = false;
            b.BypassUntil = now + TimeSpan.FromSeconds(4);
            return gap;
        }

        return null;
    }

    private void FinishRoute(XenoBrainComponent b, MapId map, Vector2 me, Vector2 goal, TimeSpan now)
    {
        if (b.RouteKnown)
        {
            foreach (var p in _passages)
            {
                if (p.Map == map && (p.Exit - me).Length() < 3.5f)
                    p.Expire = now + TimeSpan.FromSeconds(180);
            }

            return;
        }

        RecordIfCrossing(map, b.RouteStart, me, goal, now);
    }

    private bool SegmentBlocked(MapId map, Vector2 a, Vector2 b)
    {
        var d = b - a;
        var l = d.Length();
        if (l < 0.3f)
            return false;

        return RayHit(map, a, d / l, l, BlockMask, null, out _, out _, out _);
    }

    private void RecordIfCrossing(MapId map, Vector2 entry, Vector2 exit, Vector2 goal, TimeSpan now)
    {
        if (!SegmentBlocked(map, entry, goal) || SegmentBlocked(map, exit, goal))
            return;

        RecordPassage(map, entry, exit, now);
    }

    private void RecordPassage(MapId map, Vector2 entry, Vector2 exit, TimeSpan now)
    {
        var expire = now + TimeSpan.FromSeconds(180);
        foreach (var p in _passages)
        {
            if (p.Map == map && (p.Entry - entry).Length() < 3f && (p.Exit - exit).Length() < 3f)
            {
                p.Expire = expire;
                return;
            }
        }

        _passages.Add(new Passage { Map = map, Entry = entry, Exit = exit, Expire = expire });
        if (_passages.Count > 40)
            _passages.RemoveAt(0);
    }

    public bool TryGetPassageRoute(MapId map, Vector2 me, Vector2 goal, TimeSpan now, out Vector2 entry, out Vector2 exit)
    {
        entry = default;
        exit = default;

        Passage? best = null;
        var bestScore = float.MaxValue;
        var myDist = (me - goal).Length();

        foreach (var p in _passages)
        {
            if (p.Map != map || p.Expire < now)
                continue;

            var toEntry = (p.Entry - me).Length();
            var exitToGoal = (p.Exit - goal).Length();
            if (toEntry > 25f || myDist - exitToGoal < 2f)
                continue;

            var score = toEntry + exitToGoal;
            if (score < bestScore)
            {
                bestScore = score;
                best = p;
            }
        }

        if (best == null)
            return false;

        entry = best.Entry;
        exit = best.Exit;
        return true;
    }

    private EntityUid? FindObstacle(XenoInfo x, Vector2 forward)
    {
        var side = new Vector2(-forward.Y, forward.X) * 0.3f;
        EntityUid? best = null;
        var bestDist = float.MaxValue;

        for (var i = -1; i <= 1; i++)
        {
            if (RayHit(x.Map, x.Pos + side * i, forward, 1.8f, BlockMask, x.Uid, out var d, out _, out var hit)
                && d < bestDist)
            {
                bestDist = d;
                best = hit;
            }
        }

        return best;
    }

    private void SetMode(XenoInfo x, XenoMode mode, TimeSpan now)
    {
        var b = x.Brain;
        if (b.Mode == mode)
            return;

        b.Mode = mode;
        b.ModeSince = now;
        b.StuckAnchor = x.Pos;
        b.StuckSince = now;
    }

    private void UpdateSpeed(XenoInfo x, TimeSpan now)
    {
        var b = x.Brain;
        var m = b.BaseSpeed;

        if (b.Mode == XenoMode.Retreat)
            m = b.RetreatSpeed;
        else if (now < b.LeapUntil)
            m = b.LeapSpeed;
        else if (b.Mode == XenoMode.Patrol && now < b.CautiousUntil)
            m = b.CautiousSpeed;

        if (MathF.Abs(m - b.SpeedMult) < 0.01f)
            return;

        b.SpeedMult = m;
        _speed.RefreshMovementSpeedModifiers(x.Uid);
    }

    private int RequiredPack(EntityUid player, int mapXenos, bool ranged)
    {
        var t = GetThreat(player);
        var req = t >= 4f ? 3 : t >= 1.5f ? 2 : 1;
        if (ranged)
            req = Math.Max(req, 2);

        return Math.Min(req, Math.Max(1, mapXenos));
    }

    private bool TryGetPlayer(EntityUid uid, MapId map, out PlayerInfo info)
    {
        foreach (var p in _players)
        {
            if (p.Uid == uid && p.Map == map)
            {
                info = p;
                return true;
            }
        }

        info = default;
        return false;
    }

    private XenoInfo? NearestOtherXeno(XenoInfo x)
    {
        XenoInfo? best = null;
        var bestDist = float.MaxValue;
        foreach (var a in _xenos)
        {
            if (a.Uid == x.Uid || a.Map != x.Map)
                continue;

            var d = (a.Pos - x.Pos).Length();
            if (d < bestDist)
            {
                bestDist = d;
                best = a;
            }
        }

        return best;
    }

    private bool TryGetRetaliator(XenoInfo x, TimeSpan now, out EntityUid target, out Vector2 pos)
    {
        target = default;
        pos = default;

        var b = x.Brain;
        if (b.Retaliator is not { } r)
            return false;

        if ((now - b.RetaliatorTime).TotalSeconds > b.RetaliateSeconds)
            return false;

        if (TerminatingOrDeleted(r) || _mobState.IsDead(r))
            return false;

        if (Transform(r).MapID != x.Map)
            return false;

        pos = _transform.GetWorldPosition(r);
        if ((pos - x.Pos).Length() > 25f)
            return false;

        target = r;
        return true;
    }
}
