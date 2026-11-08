// Nestation new city CC BY-NC 3.0
using System.Numerics;
using Content.Server._Nestation.XenoHive;
using Content.Shared._Nestation.XenoHive;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Random;

namespace Content.Server._Nestation.XenoBrain;

public sealed partial class XenoBrainSystem
{
    [Dependency] private XenoHiveSystem _hive = default!;

    private void DecideBurrower(Ctx c, Entity<XenoClutchComponent> hive, XenoMazePlan plan)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;

        if (now >= b.RepairUntil && TryProvoke(x, now))
            return;

        if (now < b.RepairUntil)
        {
            SetMode(x, XenoMode.Heal, now);
            ThinkRepair(c);
            return;
        }

        SetMode(x, XenoMode.Build, now);
        ThinkBuild(x, hive, plan, now);
    }

    private bool TryProvoke(XenoInfo x, TimeSpan now)
    {
        var b = x.Brain;
        if (b.ForcedTarget != null)
            return false;

        PlayerInfo? victim = null;
        var hit = false;
        if (b.LastPlayerAttacker is { } attacker
            && (now - b.LastPlayerAttackTime).TotalSeconds < 2.5
            && TryGetPlayer(attacker, x.Map, out var attackerInfo))
        {
            victim = attackerInfo;
            hit = true;
        }

        if (!hit)
        {
            var nearDist = float.MaxValue;
            foreach (var p in _players)
            {
                if (p.Map != x.Map)
                    continue;

                var d = (p.Pos - x.Pos).Length();
                if (d < nearDist)
                {
                    nearDist = d;
                    victim = p;
                }
            }

            if (!b.ProvokeOnSight)
                return false;

            if (victim == null || nearDist > b.ProvokeRange || now < b.NextProvokeAt)
                return false;

            b.NextProvokeAt = now + TimeSpan.FromSeconds(b.ProvokeCheckSeconds);
            if (!_random.Prob(b.ProvokeChance))
                return false;
        }

        if (victim is not { } target)
            return false;

        CallReinforcements(x, target.Uid, now);

        b.ForcedTarget = target.Uid;
        b.ForcedUntil = now + TimeSpan.FromSeconds(10);
        b.RecentHits.Clear();
        b.BuildEntry = null;
        b.BuildStand = null;
        Leap(x, now);
        return true;
    }

    private void CallReinforcements(XenoInfo caller, EntityUid player, TimeSpan now)
    {
        var picked = new List<XenoInfo>();
        for (var i = 0; i < caller.Brain.CallCount; i++)
        {
            XenoInfo? best = null;
            var bestDist = float.MaxValue;
            foreach (var a in _xenos)
            {
                if (a.Uid == caller.Uid || a.Map != caller.Map || !a.Brain.HiveOwned || a.Brain.Caste == XenoCaste.Burrower
                    || a.Brain.DuelOpponent != null || a.Brain.Mode == XenoMode.Retreat || picked.Contains(a))
                {
                    continue;
                }

                var d = (a.Pos - caller.Pos).Length();
                if (d > caller.Brain.CallRange || d >= bestDist)
                    continue;

                bestDist = d;
                best = a;
            }

            if (best is not { } chosen)
                break;

            picked.Add(chosen);
            var ab = chosen.Brain;
            ab.ForcedTarget = player;
            ab.ForcedUntil = now + TimeSpan.FromSeconds(12);
            ab.NoRetreatUntil = now + TimeSpan.FromSeconds(10);
            ab.NoLurkUntil = now + TimeSpan.FromSeconds(15);
            ab.RecentHits.Clear();
            Leap(chosen, now);
        }
    }

    private void ThinkBuild(XenoInfo x, Entity<XenoClutchComponent> hive, XenoMazePlan plan, TimeSpan now)
    {
        var b = x.Brain;

        if (b.BuildEntry is { } current)
        {
            if (current.State != XenoEntryState.Pending)
            {
                b.BuildEntry = null;
                b.BuildStand = null;
            }
            else if ((now - b.BuildSince).TotalSeconds > b.BuildStuckSeconds)
            {
                _hive.Skip(plan, current, now, 90f);
                b.BuildEntry = null;
                b.BuildStand = null;
                b.PathActive = false;
            }
        }

        if (b.BuildEntry == null && now >= b.NextPickAt)
        {
            var entry = _hive.PickEntry(hive, plan, x.Pos, x.Uid, now);
            if (entry != null && _hive.TryPickStand(plan, entry, x.Pos, out var stand))
            {
                b.BuildEntry = entry;
                b.BuildSince = now;
                b.BuildIdlePicks = 0;
                b.PathReplans = 0;
                SetWalk(x, plan, stand, now);
            }
            else
            {
                var delay = 2.0;

                if (entry != null)
                {
                    _hive.Skip(plan, entry, now, 90f);
                }
                else
                {
                    b.BuildIdlePicks++;

                    if (_hive.AnyGap(plan))
                    {
                        if (b.BuildIdlePicks >= 15)
                        {
                            hive.Comp.NextFix = TimeSpan.Zero;
                            _hive.ParkUnreachable(plan, x.Pos, now);
                            b.BuildIdlePicks = 0;
                        }
                    }
                    else if (_hive.HasMendWork(hive, plan, now))
                    {
                        StartRepair(x, now, false, x.Pos, false);
                        return;
                    }
                    else if (_hive.TryExpand(hive, plan))
                    {
                        b.BuildIdlePicks = 0;
                        delay = 0.2;
                    }
                    else
                    {
                        StartRepair(x, now, false, x.Pos, true);
                        return;
                    }
                }

                b.NextPickAt = now + TimeSpan.FromSeconds(delay);
            }
        }

        if (b.BuildEntry == null)
        {
            if ((b.BuildStand == null || now >= b.IdlePickAt) && _hive.TryRandomReachable(plan, x.Pos, 40, out var idle))
            {
                SetWalk(x, plan, idle, now);
                b.IdlePickAt = now + TimeSpan.FromSeconds(12);
            }

            StepWalk(x, plan, now);
            b.GoalPos = b.BuildStand != null ? b.BuildStandPos : (Vector2?) null;
            return;
        }

        StepWalk(x, plan, now);
        b.GoalPos = b.BuildStandPos;

        var entryPos = _hive.EntryWorld(plan, b.BuildEntry.Offset);
        if ((entryPos - x.Pos).Length() > b.BuildReach || now < b.NextBuildAt)
            return;

        var done = b.BuildEntry;
        switch (_hive.Place(hive, plan, done, now))
        {
            case XenoPlaceResult.Placed:
                b.NextBuildAt = now + _hive.BuildDelay(hive.Comp, done.Kind == XenoPlanKind.Reinforced
                    ? hive.Comp.ReinforcedBuildInterval
                    : hive.Comp.BuildInterval, now);
                b.BuildEntry = null;
                b.BuildStand = null;
                break;
            case XenoPlaceResult.Skip:
                b.BuildEntry = null;
                b.BuildStand = null;
                break;
        }
    }

    private void SetWalk(XenoInfo x, XenoMazePlan plan, Vector2i goal, TimeSpan now)
    {
        var b = x.Brain;
        b.PathGoal = goal;
        b.PathGoalPos = _hive.EntryWorld(plan, goal);
        b.PathActive = true;
        b.PathIndex = 0;
        b.NextReplanAt = now + TimeSpan.FromSeconds(1);

        if (!_hive.TryWalkPath(plan, x.Pos, goal, b.Path))
            b.Path.Clear();

        ApplyWaypoint(x, plan, now);
    }

    private void StepWalk(XenoInfo x, XenoMazePlan plan, TimeSpan now)
    {
        var b = x.Brain;
        if (!b.PathActive)
            return;

        if (b.BuildStand == null)
        {
            b.PathActive = false;
            return;
        }

        ApplyWaypoint(x, plan, now);
    }

    private void ApplyWaypoint(XenoInfo x, XenoMazePlan plan, TimeSpan now)
    {
        var b = x.Brain;

        if (b.Path.Count == 0)
        {
            SetStand(b, plan, b.PathGoal);
            return;
        }

        var best = b.PathIndex;
        var bestDist = float.MaxValue;
        var window = Math.Min(b.Path.Count - 1, b.PathIndex + 10);
        for (var i = b.PathIndex; i <= window; i++)
        {
            var d = (_hive.EntryWorld(plan, b.Path[i]) - x.Pos).LengthSquared();
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        if (bestDist > 36f && now >= b.NextReplanAt)
        {
            SetWalk(x, plan, b.PathGoal, now);
            return;
        }

        if (best > b.PathIndex)
        {
            b.BuildSince = now;
            b.PathReplans = 0;
        }

        b.PathIndex = best;

        var last = b.Path.Count - 1;
        var pick = Math.Min(last, best + 1);
        for (var i = Math.Min(last, best + 14); i > pick; i--)
        {
            if (!_hive.WalkClear(plan, x.Pos, _hive.EntryWorld(plan, b.Path[i])))
                continue;

            pick = i;
            break;
        }

        SetStand(b, plan, b.Path[pick]);
    }

    private void SetStand(XenoBrainComponent b, XenoMazePlan plan, Vector2i tile)
    {
        b.BuildStand = _hive.EntryCoords(plan, tile);
        b.BuildStandPos = _hive.EntryWorld(plan, tile);
    }

    private void OnBuildStuck(XenoInfo x, TimeSpan now)
    {
        var b = x.Brain;
        if (b.Mode == XenoMode.Heal && b.RepairFleeing)
        {
            b.RepairFleeing = false;
        }
        else if (b.BuildEntry is { } entry && _hive.TryGetHive(x.Map, out var hive) && hive.Comp.Plan is { } plan)
        {
            if (b.PathActive && b.PathReplans < 3)
            {
                b.PathReplans++;
                SetWalk(x, plan, b.PathGoal, now);
                return;
            }

            if (b.Mode == XenoMode.Heal && b.RepairKind != XenoRepairKind.Rebuild)
                _hive.Defer(plan, entry, now, 60f);
            else
                _hive.Skip(plan, entry, now, 60f);
        }

        b.BuildEntry = null;
        b.BuildStand = null;
        b.PathActive = false;
        b.Path.Clear();
        b.IdlePickAt = TimeSpan.Zero;
    }

    private bool TryPickEscort(XenoInfo x, out EntityUid escort)
    {
        escort = default;
        var bestDist = 120f;
        var found = false;

        var query = EntityQueryEnumerator<XenoCasteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var caste, out var xform))
        {
            if (uid == x.Uid || caste.Class < 2 || xform.MapID != x.Map || _mobState.IsDead(uid))
                continue;

            var d = (_transform.GetWorldPosition(xform) - x.Pos).Length();
            if (d >= bestDist)
                continue;

            bestDist = d;
            escort = uid;
            found = true;
        }

        return found;
    }

    public int HiveStage(MapId map) => _hive.GetStage(map);

    public Vector2? HivePosition(MapId map) => _hive.HivePos(map);
}
