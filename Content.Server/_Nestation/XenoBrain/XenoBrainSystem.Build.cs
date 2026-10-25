// Nestation Anarhey CC BY-NC 3.0
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
        var now = c.Now;

        if (TryProvoke(x, now))
            return;

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
                if (a.Uid == caller.Uid || a.Map != caller.Map || a.Brain.Caste == XenoCaste.Burrower
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
            }
        }

        if (b.BuildEntry == null && now >= b.NextPickAt)
        {
            var entry = _hive.PickEntry(hive, plan, x.Pos, x.Uid, now);
            if (entry != null && _hive.TryPickStand(plan, entry, x.Pos, out var stand))
            {
                b.BuildEntry = entry;
                b.BuildSince = now;
                SetStand(b, plan, stand);
            }
            else
            {
                if (entry != null)
                    _hive.Skip(plan, entry, now, 90f);
                else if (_hive.AnyPending(plan))
                    hive.Comp.NextFix = TimeSpan.Zero;

                b.NextPickAt = now + TimeSpan.FromSeconds(2);
            }
        }

        if (b.BuildEntry == null)
        {
            if ((b.BuildStand == null || now >= b.IdlePickAt) && _hive.TryRandomOpen(plan, x.Pos, out var idle))
            {
                SetStand(b, plan, idle);
                b.IdlePickAt = now + TimeSpan.FromSeconds(12);
            }

            b.GoalPos = b.BuildStand != null ? b.BuildStandPos : (Vector2?) null;
            return;
        }

        b.GoalPos = b.BuildStandPos;

        var entryPos = _hive.EntryWorld(plan, b.BuildEntry.Offset);
        if ((entryPos - x.Pos).Length() > b.BuildReach || now < b.NextBuildAt)
            return;

        var done = b.BuildEntry;
        switch (_hive.Place(hive, plan, done, now))
        {
            case XenoPlaceResult.Placed:
                b.NextBuildAt = now + (done.Kind == XenoPlanKind.Reinforced
                    ? hive.Comp.ReinforcedBuildInterval
                    : hive.Comp.BuildInterval);
                b.BuildEntry = null;
                b.BuildStand = null;
                break;
            case XenoPlaceResult.Skip:
                b.BuildEntry = null;
                b.BuildStand = null;
                break;
        }
    }

    private void SetStand(XenoBrainComponent b, XenoMazePlan plan, Vector2i tile)
    {
        b.BuildStand = _hive.EntryCoords(plan, tile);
        b.BuildStandPos = _hive.EntryWorld(plan, tile);
    }

    private void OnBuildStuck(XenoInfo x, TimeSpan now)
    {
        var b = x.Brain;
        if (b.BuildEntry is { } entry && _hive.TryGetHive(x.Map, out var hive) && hive.Comp.Plan is { } plan)
            _hive.Skip(plan, entry, now, 60f);

        b.BuildEntry = null;
        b.BuildStand = null;
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
