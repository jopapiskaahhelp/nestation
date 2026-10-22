// Nestation Anarhey CC BY-NC 3.0
using System.Numerics;
using Content.Server._Nestation.XenoHive;
using Content.Shared._Nestation.XenoHive;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._Nestation.XenoBrain;

public sealed partial class XenoBrainSystem
{
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private DamageableSystem _damageable = default!;

    private enum SquadState : byte
    {
        Guard,
        Advance,
        Engage,
    }

    private sealed class Squad
    {
        public EntityUid Tank;
        public MapId Map;
        public Vector2 TankPos;
        public readonly List<EntityUid> Members = new();
        public SquadState State;
        public TimeSpan StateSince;
        public TimeSpan NextRaid;
        public EntityUid? Target;
        public Vector2 Goal;
        public float TankFrac;
        public bool Pounced;
    }

    private const int SquadMax = 4;
    private const int RaidMinMembers = 2;
    private const float RaidRange = 150f;
    private const float RaidIntervalSeconds = 240f;
    private const float PounceDist = 6f;

    private readonly Dictionary<EntityUid, Squad> _squads = new();
    private Dictionary<EntityUid, Squad> _squadOf = new();
    private Dictionary<EntityUid, Squad> _squadOfNext = new();
    private readonly HashSet<EntityUid> _squadAlive = new();
    private TimeSpan _nextSquadUpdate;

    private static bool IsTank(XenoBrainComponent b) => b.Caste is XenoCaste.Ravager or XenoCaste.Praetorian;

    private bool FindXeno(EntityUid uid, out XenoInfo info)
    {
        foreach (var a in _xenos)
        {
            if (a.Uid == uid)
            {
                info = a;
                return true;
            }
        }

        info = default;
        return false;
    }

    private float HpFrac(EntityUid uid)
    {
        if (!TryComp<DamageableComponent>(uid, out var dmg) || !_thresholds.TryGetDeadThreshold(uid, out var dead)
            || dead.Value.Float() <= 0f)
        {
            return 1f;
        }

        var total = _damageable.GetTotalDamage((uid, dmg));
        return Math.Clamp(1f - total.Float() / dead.Value.Float(), 0f, 1f);
    }

    private bool TryFindVisiblePlayer(XenoInfo x, float range, out PlayerInfo found)
    {
        found = default;
        var best = float.MaxValue;
        var ok = false;

        foreach (var p in _players)
        {
            if (p.Map != x.Map)
                continue;

            var delta = p.Pos - x.Pos;
            var d = delta.Length();
            if (d > range || d >= best)
                continue;

            if (d > 0.5f && RayHit(x.Map, x.Pos, delta / d, d, OpaqueMask, x.Uid, out _, out _, out _))
                continue;

            best = d;
            found = p;
            ok = true;
        }

        return ok;
    }

    private bool TryNearestPlayer(MapId map, Vector2 pos, float range, out PlayerInfo found)
    {
        found = default;
        var best = range;
        var ok = false;

        foreach (var p in _players)
        {
            if (p.Map != map)
                continue;

            var d = (p.Pos - pos).Length();
            if (d >= best)
                continue;

            best = d;
            found = p;
            ok = true;
        }

        return ok;
    }

    private void SetStandWorld(XenoBrainComponent b, MapId map, Vector2 pos)
    {
        b.BuildStand = _transform.ToCoordinates(new MapCoordinates(pos, map));
        b.BuildStandPos = pos;
    }

    private bool HandleHeal(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;

        if (b.Caste == XenoCaste.Burrower)
            return false;

        if (b.Mode != XenoMode.Heal)
            return TryStartHeal(c);

        if (TryFindVisiblePlayer(x, b.SightRange, out var seen))
        {
            InterruptHeal(x, now, seen.Uid);
            return false;
        }

        if ((now - b.LastHitTime).TotalSeconds < 2.0)
        {
            EntityUid? attacker = b.LastPlayerAttacker is { } a && (now - b.LastPlayerAttackTime).TotalSeconds < 3.0
                                  && TryGetPlayer(a, x.Map, out var ai)
                ? ai.Uid
                : null;
            InterruptHeal(x, now, attacker);
            return false;
        }

        if (HpFrac(x.Uid) >= b.HealTo || (now - b.ModeSince).TotalSeconds > b.HealMaxSeconds)
        {
            b.NoHealUntil = now + TimeSpan.FromSeconds(10);
            b.BuildStand = null;
            SetMode(x, XenoMode.Patrol, now);
            return false;
        }

        b.GoalPos = b.BuildStandPos;

        if ((b.BuildStandPos - x.Pos).Length() <= 2.5f && now >= b.NextHealAt)
        {
            b.NextHealAt = now + TimeSpan.FromSeconds(1);
            HealTick(x.Uid, b.HealPerSecond);
        }

        return true;
    }

    private bool TryStartHeal(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;

        if (now < b.NoHealUntil || b.DuelOpponent != null || b.Mode is XenoMode.Feed or XenoMode.Stalk)
            return false;

        if ((now - b.LastHitTime).TotalSeconds < 4.0 || now < b.LeapUntil || b.ForcedTarget != null)
            return false;

        if (HpFrac(x.Uid) >= b.HealBelow)
            return false;

        if (!_hive.TryGetHive(x.Map, out var hive) || _hive.GetPlan(hive) is not { } plan)
            return false;

        if (TryFindVisiblePlayer(x, b.SightRange, out _))
            return false;

        if (!_hive.TryPickSpot(plan, 3, 7, out var tile))
            return false;

        SetStand(b, plan, tile);
        b.BuildEntry = null;
        b.RecentHits.Clear();
        SetMode(x, XenoMode.Heal, now);
        return true;
    }

    private void InterruptHeal(XenoInfo x, TimeSpan now, EntityUid? against)
    {
        var b = x.Brain;
        b.NoHealUntil = now + TimeSpan.FromSeconds(20);
        b.BuildStand = null;
        SetMode(x, XenoMode.Patrol, now);

        if (against is not { } target)
            return;

        b.ForcedTarget = target;
        b.ForcedUntil = now + TimeSpan.FromSeconds(10);
        b.RecentHits.Clear();
        Leap(x, now);
    }

    private void HealTick(EntityUid uid, float perSecond)
    {
        if (!TryComp<DamageableComponent>(uid, out var dmg) || !_thresholds.TryGetDeadThreshold(uid, out var dead))
            return;

        var amount = dead.Value.Float() * perSecond;
        _damageable.HealEvenly(new Entity<DamageableComponent?>(uid, dmg), FixedPoint2.New(-amount));
    }

    private bool TrySquadDecide(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;

        if (b.Caste == XenoCaste.Burrower || !_squadOf.TryGetValue(x.Uid, out var squad))
            return false;

        if ((now - b.LastHitTime).TotalSeconds < 3.0)
            return false;

        if (!_hive.TryGetHive(x.Map, out var hive) || _hive.GetPlan(hive) is not { } plan)
            return false;

        var isTank = squad.Tank == x.Uid;

        switch (squad.State)
        {
            case SquadState.Guard:
                ThinkGuard(c, plan);
                return true;
            case SquadState.Advance when isTank:
                if (b.BuildStand == null || (b.BuildStandPos - squad.Goal).Length() > 2f)
                    SetStandWorld(b, x.Map, squad.Goal);

                SetMode(x, XenoMode.Advance, now);
                b.GoalPos = squad.Goal;
                return true;
            case SquadState.Engage when isTank:
                return false;
            case SquadState.Engage when squad.Pounced:
                return false;
            default:
                b.RallyTarget = squad.Tank;
                SetMode(x, XenoMode.Rally, now);
                return true;
        }
    }

    private void ThinkGuard(Ctx c, XenoMazePlan plan)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;

        SetMode(x, XenoMode.Guard, now);

        if (TryFindVisiblePlayer(x, b.AmbushTriggerRange, out var near))
        {
            b.ForcedTarget = near.Uid;
            b.ForcedUntil = now + TimeSpan.FromSeconds(8);
            b.RecentHits.Clear();
            Leap(x, now);
            return;
        }

        if ((b.BuildStand == null || now >= b.IdlePickAt) && _hive.TryPickSpot(plan, 5, plan.Radius - 1, out var tile))
        {
            SetStand(b, plan, tile);
            b.IdlePickAt = now + TimeSpan.FromSeconds(60 + _random.NextFloat() * 60f);
        }

        b.GoalPos = b.BuildStand != null ? b.BuildStandPos : (Vector2?) null;
    }

    private void UpdateSquads(TimeSpan now)
    {
        if (now < _nextSquadUpdate)
            return;

        _nextSquadUpdate = now + TimeSpan.FromSeconds(1);

        _squadAlive.Clear();
        foreach (var x in _xenos)
        {
            if (!IsTank(x.Brain) || x.Brain.DuelOpponent != null || !_hive.TryGetHive(x.Map, out _))
                continue;

            _squadAlive.Add(x.Uid);
            if (!_squads.TryGetValue(x.Uid, out var created))
            {
                created = new Squad
                {
                    Tank = x.Uid,
                    StateSince = now,
                    NextRaid = now + TimeSpan.FromSeconds(RaidIntervalSeconds),
                };
                _squads[x.Uid] = created;
            }

            created.Map = x.Map;
            created.TankPos = x.Pos;
        }

        _tmpKeys.Clear();
        foreach (var key in _squads.Keys)
        {
            if (!_squadAlive.Contains(key))
                _tmpKeys.Add(key);
        }

        foreach (var key in _tmpKeys)
        {
            _squads.Remove(key);
        }

        foreach (var squad in _squads.Values)
        {
            squad.Members.Clear();
        }

        _squadOfNext.Clear();
        foreach (var squad in _squads.Values)
        {
            _squadOfNext[squad.Tank] = squad;
        }

        foreach (var x in _xenos)
        {
            var b = x.Brain;
            if (IsTank(b) || b.Caste == XenoCaste.Burrower || b.DuelOpponent != null)
                continue;

            Squad? chosen = null;
            if (_squadOf.TryGetValue(x.Uid, out var prev) && _squads.TryGetValue(prev.Tank, out var cur)
                && cur.Map == x.Map && cur.Members.Count < SquadMax)
            {
                chosen = cur;
            }
            else
            {
                var bestScore = float.MaxValue;
                foreach (var s in _squads.Values)
                {
                    if (s.Map != x.Map || s.Members.Count >= SquadMax)
                        continue;

                    var score = s.Members.Count * 1000f + (s.TankPos - x.Pos).Length();
                    if (score < bestScore)
                    {
                        bestScore = score;
                        chosen = s;
                    }
                }
            }

            if (chosen == null)
                continue;

            chosen.Members.Add(x.Uid);
            _squadOfNext[x.Uid] = chosen;
        }

        (_squadOf, _squadOfNext) = (_squadOfNext, _squadOf);

        foreach (var squad in _squads.Values)
        {
            if (FindXeno(squad.Tank, out var tank))
                UpdateSquad(squad, tank, now);
        }
    }

    private void UpdateSquad(Squad squad, XenoInfo tank, TimeSpan now)
    {
        var tb = tank.Brain;
        var tankFrac = HpFrac(tank.Uid);
        var ready = tankFrac >= 0.6f && tb.Mode != XenoMode.Heal;

        var readyMembers = 0;
        foreach (var m in squad.Members)
        {
            if (FindXeno(m, out var mi) && mi.Brain.Mode != XenoMode.Heal && HpFrac(m) >= 0.6f)
                readyMembers++;
        }

        switch (squad.State)
        {
            case SquadState.Guard:
                if (ready && TryFindVisiblePlayer(tank, tb.GuardAlertRange, out var seen))
                {
                    StartEngage(squad, tank, seen.Uid, tankFrac, now);
                    break;
                }

                if (now < squad.NextRaid)
                    break;

                if (!ready || readyMembers < RaidMinMembers)
                {
                    squad.NextRaid = now + TimeSpan.FromSeconds(20);
                    break;
                }

                if (TryNearestPlayer(tank.Map, tank.Pos, RaidRange, out var target))
                {
                    squad.State = SquadState.Advance;
                    squad.StateSince = now;
                    squad.Target = target.Uid;
                    squad.Goal = target.Pos;
                    tb.BuildStand = null;
                }
                else
                {
                    squad.NextRaid = now + TimeSpan.FromSeconds(RaidIntervalSeconds / 2f);
                }

                break;

            case SquadState.Advance:
                if (!ready || (now - squad.StateSince).TotalSeconds > 180.0
                    || !TryNearestPlayer(tank.Map, tank.Pos, RaidRange, out var next))
                {
                    EndRaid(squad, now);
                    break;
                }

                squad.Target = next.Uid;
                squad.Goal = next.Pos;

                if (TryFindVisiblePlayer(tank, 18f, out var spotted))
                    StartEngage(squad, tank, spotted.Uid, tankFrac, now);

                break;

            default:
                if (squad.Target is not { } engaged || !TryGetPlayer(engaged, tank.Map, out var victim)
                    || (victim.Pos - tank.Pos).Length() > 45f || (now - squad.StateSince).TotalSeconds > 60.0
                    || tankFrac < 0.35f)
                {
                    EndRaid(squad, now);
                    break;
                }

                tb.ForcedTarget = engaged;
                tb.ForcedUntil = now + TimeSpan.FromSeconds(3);
                tb.NoRetreatUntil = now + TimeSpan.FromSeconds(3);

                if (!squad.Pounced
                    && ((victim.Pos - tank.Pos).Length() <= PounceDist
                        || squad.TankFrac - tankFrac >= 0.15f
                        || (now - squad.StateSince).TotalSeconds > 8.0))
                {
                    Pounce(squad, engaged, now);
                }

                break;
        }
    }

    private void StartEngage(Squad squad, XenoInfo tank, EntityUid target, float tankFrac, TimeSpan now)
    {
        squad.State = SquadState.Engage;
        squad.StateSince = now;
        squad.Target = target;
        squad.TankFrac = tankFrac;
        squad.Pounced = false;

        var tb = tank.Brain;
        tb.ForcedTarget = target;
        tb.ForcedUntil = now + TimeSpan.FromSeconds(3);
        tb.NoRetreatUntil = now + TimeSpan.FromSeconds(3);
        tb.BuildStand = null;
    }

    private void Pounce(Squad squad, EntityUid target, TimeSpan now)
    {
        squad.Pounced = true;

        foreach (var m in squad.Members)
        {
            if (!FindXeno(m, out var mi) || mi.Brain.Mode == XenoMode.Heal)
                continue;

            var mb = mi.Brain;
            mb.ForcedTarget = target;
            mb.ForcedUntil = now + TimeSpan.FromSeconds(12);
            mb.NoRetreatUntil = now + TimeSpan.FromSeconds(10);
            mb.NoLurkUntil = now + TimeSpan.FromSeconds(15);
            mb.RecentHits.Clear();
            Leap(mi, now);
        }
    }

    private void EndRaid(Squad squad, TimeSpan now)
    {
        squad.State = SquadState.Guard;
        squad.StateSince = now;
        squad.Target = null;
        squad.Pounced = false;
        squad.NextRaid = now + TimeSpan.FromSeconds(RaidIntervalSeconds + _random.NextFloat() * 60f);

        if (FindXeno(squad.Tank, out var tank))
        {
            tank.Brain.BuildStand = null;
            tank.Brain.IdlePickAt = TimeSpan.Zero;
        }

        foreach (var m in squad.Members)
        {
            if (!FindXeno(m, out var mi))
                continue;

            mi.Brain.BuildStand = null;
            mi.Brain.IdlePickAt = TimeSpan.Zero;
        }
    }
}
