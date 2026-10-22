// Nestation Anarhey CC BY-NC 3.0
using System.Numerics;
using Content.Shared._Nestation.XenoHive;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._Nestation.XenoBrain;

public sealed partial class XenoBrainSystem
{
    private const float DuelIntervalSeconds = 180f;
    private const float DuelChance = 0.3f;

    private TimeSpan _nextDuelRoll;

    private void RollDuels(TimeSpan now)
    {
        if (_nextDuelRoll == TimeSpan.Zero)
            _nextDuelRoll = now + TimeSpan.FromSeconds(DuelIntervalSeconds);

        if (now < _nextDuelRoll)
            return;

        _nextDuelRoll = now + TimeSpan.FromSeconds(DuelIntervalSeconds);

        var n = _xenos.Count;
        if (n < 2)
            return;

        var parent = new int[n];
        for (var i = 0; i < n; i++)
        {
            parent[i] = i;
        }

        int Find(int k)
        {
            while (parent[k] != k)
            {
                parent[k] = parent[parent[k]];
                k = parent[k];
            }

            return k;
        }

        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                var left = _xenos[i];
                var right = _xenos[j];
                if (left.Map == right.Map && (left.Pos - right.Pos).Length() <= left.Brain.PackRadius)
                    parent[Find(i)] = Find(j);
            }
        }

        var herds = new Dictionary<int, List<int>>();
        for (var i = 0; i < n; i++)
        {
            var root = Find(i);
            if (!herds.TryGetValue(root, out var list))
            {
                list = new List<int>();
                herds[root] = list;
            }

            list.Add(i);
        }

        foreach (var members in herds.Values)
        {
            if (members.Count < 2 || !_random.Prob(DuelChance))
                continue;

            var free = new List<int>();
            foreach (var idx in members)
            {
                var cand = _xenos[idx];
                if (cand.Brain.DuelOpponent == null && cand.Brain.Caste != XenoCaste.Burrower && !IsChasingPlayer(cand.Brain, now))
                    free.Add(idx);
            }

            if (free.Count < 2)
                continue;

            var first = _xenos[free[_random.Next(free.Count)]];

            XenoInfo? second = null;
            var bestDist = float.MaxValue;
            foreach (var idx in free)
            {
                var other = _xenos[idx];
                if (other.Uid == first.Uid)
                    continue;

                var d = (other.Pos - first.Pos).Length();
                if (d < bestDist)
                {
                    bestDist = d;
                    second = other;
                }
            }

            if (second is { } s)
                StartDuel(first, s, now);
        }
    }

    private bool IsChasingPlayer(XenoBrainComponent b, TimeSpan now)
    {
        if (b.Mode is XenoMode.Retreat or XenoMode.Ambush or XenoMode.Stalk or XenoMode.Kite or XenoMode.Flank or XenoMode.Feed)
            return true;

        if (now < b.LeapUntil || (b.ForcedTarget != null && now < b.ForcedUntil))
            return true;

        return (b.Mode is XenoMode.Attack or XenoMode.Detour or XenoMode.Rally)
               && b.Target is { } t
               && HasComp<ActorComponent>(t)
               && b.TargetDist <= b.DuelChaseRange;
    }

    private void StartDuel(XenoInfo first, XenoInfo second, TimeSpan now)
    {
        PrepareDuel(first.Brain, second.Uid);
        PrepareDuel(second.Brain, first.Uid);
        SetMode(first, XenoMode.Duel, now);
        SetMode(second, XenoMode.Duel, now);
        Log.Debug($"Xeno duel started: {ToPrettyString(first.Uid)} vs {ToPrettyString(second.Uid)}");
    }

    private static void PrepareDuel(XenoBrainComponent br, EntityUid opponent)
    {
        br.DuelOpponent = opponent;
        br.Target = opponent;
        br.ForcedTarget = null;
        br.StalkTarget = null;
        br.SmashActive = false;
        br.SmashTarget = null;
        br.Route.Clear();
        br.RecentHits.Clear();
    }

    private void ThinkDuel(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;

        if (b.DuelOpponent is not { } opp || !TryGetXeno(opp, x.Map, out var o))
        {
            b.DuelOpponent = null;
            b.Target = null;
            b.GoalPos = null;
            b.RecentHits.Clear();
            SetMode(x, XenoMode.Patrol, c.Now);
            return;
        }

        b.Target = opp;
        b.GoalPos = o.Pos;
        b.TargetDist = (o.Pos - x.Pos).Length();
        b.TargetRanged = false;
        b.ForcedTarget = null;
        b.RecentHits.Clear();
    }

    private bool TryGetXeno(EntityUid uid, MapId map, out XenoInfo info)
    {
        foreach (var a in _xenos)
        {
            if (a.Uid == uid && a.Map == map)
            {
                info = a;
                return true;
            }
        }

        info = default;
        return false;
    }
}
