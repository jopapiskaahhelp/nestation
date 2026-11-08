// Nestation new city CC BY-NC 3.0
using System.Numerics;
using Content.Server._Nestation.XenoHive;
using Content.Shared._Nestation.XenoHive;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Nestation.XenoBrain;

public sealed partial class XenoBrainSystem
{
    private void TrackMeetings(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;

        if (b.Caste != XenoCaste.Burrower || !b.HiveOwned || b.DuelOpponent != null)
            return;

        if (b.Met.Count > 0)
        {
            var expired = new List<NetUserId>();
            foreach (var (id, seenAt) in b.Met)
            {
                if ((now - seenAt).TotalSeconds > b.MeetWindowSeconds)
                    expired.Add(id);
            }

            foreach (var id in expired)
            {
                b.Met.Remove(id);
            }
        }

        var changed = false;

        foreach (var p in _players)
        {
            if (p.Map != x.Map || (p.Pos - x.Pos).Length() > b.MeetRange || !HasSight(x, p.Pos))
                continue;

            if (RecordMeeting(b, p, now))
                changed = true;
        }

        if (b.LastPlayerAttacker is { } attacker
            && (now - b.LastPlayerAttackTime).TotalSeconds < 2.5
            && TryGetPlayer(attacker, x.Map, out var attackerInfo)
            && RecordMeeting(b, attackerInfo, now))
        {
            changed = true;
        }

        if (!changed || b.Met.Count < b.MeetPlayers)
            return;

        Log.Debug($"Xeno burrower {ToPrettyString(x.Uid)} met {b.Met.Count} players, fleeing to repair the hive");
        StartRepair(x, now, true, b.LastMeetPos);
    }

    private bool RecordMeeting(XenoBrainComponent b, PlayerInfo p, TimeSpan now)
    {
        if (!TryComp<ActorComponent>(p.Uid, out var actor))
            return false;

        var id = actor.PlayerSession.UserId;
        var isNew = !b.Met.ContainsKey(id);
        b.Met[id] = now;
        b.LastMeetPos = p.Pos;
        return isNew;
    }

    private void StartRepair(XenoInfo x, TimeSpan now, bool flee, Vector2 threat, bool upgrade = true)
    {
        var b = x.Brain;

        b.Met.Clear();
        b.ForcedTarget = null;
        b.RecentHits.Clear();
        b.LeapUntil = TimeSpan.Zero;
        b.BuildEntry = null;
        b.BuildStand = null;
        b.RepairIdlePicks = 0;
        b.RepairFleeing = false;
        b.RepairUpgrade = upgrade;
        b.PathActive = false;
        b.NextPickAt = now;
        b.NextFleeAt = now + TimeSpan.FromSeconds(3);

        Vector2i fleeTile = default;
        XenoMazePlan? fleePlan = null;

        if (flee)
        {
            b.RepairUntil = now + TimeSpan.FromSeconds(b.RepairMaxSeconds);
            b.RepairMinUntil = now + TimeSpan.FromSeconds(b.RepairMinSeconds);

            if (_hive.TryGetHive(x.Map, out var hive) && _hive.GetPlan(hive) is { } plan
                && TryPickFleeSpot(x, plan, threat, out fleeTile))
            {
                fleePlan = plan;
            }
        }
        else
        {
            b.RepairUntil = now + TimeSpan.FromSeconds(b.MaintainSeconds);
            b.RepairMinUntil = now + TimeSpan.FromSeconds(MathF.Min(20f, b.MaintainSeconds));
        }

        SetMode(x, XenoMode.Heal, now);

        if (fleePlan != null)
        {
            SetWalk(x, fleePlan, fleeTile, now);
            b.RepairFleeing = true;
            b.RepairFleeUntil = now + TimeSpan.FromSeconds(b.RepairFleeSeconds);
        }

        UpdateSpeed(x, now);
    }

    private bool TryPickFleeSpot(XenoInfo x, XenoMazePlan plan, Vector2 threat, out Vector2i best)
    {
        best = default;
        var bestScore = float.MinValue;
        var found = false;

        for (var i = 0; i < 40 && plan.OpenList.Count > 0; i++)
        {
            var tile = plan.OpenList[_random.Next(plan.OpenList.Count)];
            var steps = _hive.WalkSteps(plan, x.Pos, tile);
            if (steps < 0 || steps > 150)
                continue;

            var world = _hive.EntryWorld(plan, tile);
            var score = (world - threat).Length() - 0.5f * steps;
            if (score <= bestScore)
                continue;

            bestScore = score;
            best = tile;
            found = true;
        }

        return found;
    }

    private void DropRepairJob(XenoBrainComponent b, XenoMazePlan plan, TimeSpan now, bool failed)
    {
        if (b.BuildEntry is { } job)
        {
            if (!failed)
                _hive.Release(plan, job);
            else if (b.RepairKind == XenoRepairKind.Rebuild)
                _hive.Skip(plan, job, now, 90f);
            else
                _hive.Defer(plan, job, now, 90f);
        }

        b.BuildEntry = null;
        b.BuildStand = null;
    }

    private void EndRepair(XenoInfo x, TimeSpan now)
    {
        var b = x.Brain;

        if (b.BuildEntry != null && _hive.TryGetHive(x.Map, out var hive) && hive.Comp.Plan is { } plan)
            DropRepairJob(b, plan, now, false);

        b.BuildEntry = null;
        b.BuildStand = null;
        b.RepairUntil = TimeSpan.Zero;
        b.RepairFleeing = false;
        b.BuildIdlePicks = 0;
        b.NextPickAt = now;
        SetMode(x, XenoMode.Build, now);
        UpdateSpeed(x, now);
    }

    private void ThinkRepair(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;

        if (!_hive.TryGetHive(x.Map, out var hive) || _hive.GetPlan(hive) is not { } plan)
        {
            EndRepair(x, now);
            return;
        }

        if (now >= b.NextFleeAt)
        {
            var hit = (now - b.LastHitTime).TotalSeconds < 2.0;
            var seen = TryFindVisiblePlayer(x, b.MeetRange, out var player);
            if (hit || seen)
            {
                StartRepair(x, now, true, hit || !seen ? b.ThreatPos : player.Pos);
                if (b.RepairFleeing)
                    b.GoalPos = b.BuildStandPos;

                return;
            }
        }

        if (b.RepairFleeing)
        {
            var reached = !b.PathActive || (_hive.EntryWorld(plan, b.PathGoal) - x.Pos).Length() <= 2.5f;
            if (!reached && now < b.RepairFleeUntil)
            {
                StepWalk(x, plan, now);
                b.GoalPos = b.BuildStandPos;
                return;
            }

            b.RepairFleeing = false;
            b.BuildStand = null;
            b.PathActive = false;
            b.NextFleeAt = now + TimeSpan.FromSeconds(3);
            UpdateSpeed(x, now);
        }

        if (now >= b.RepairUntil)
        {
            EndRepair(x, now);
            return;
        }

        if (b.BuildEntry is { } current)
        {
            var wanted = b.RepairKind == XenoRepairKind.Rebuild ? XenoEntryState.Pending : XenoEntryState.Built;
            if (current.State != wanted)
            {
                b.BuildEntry = null;
                b.BuildStand = null;
            }
            else if ((now - b.BuildSince).TotalSeconds > b.BuildStuckSeconds)
            {
                DropRepairJob(b, plan, now, true);
            }
        }

        if (b.BuildEntry == null && now >= b.NextPickAt)
        {
            b.NextPickAt = now + TimeSpan.FromSeconds(1.5);

            if (_hive.TryPickRepair(hive, plan, x.Pos, x.Uid, now, b.RepairUpgradeWalls && b.RepairUpgrade, out var entry, out var kind)
                && _hive.TryPickStand(plan, entry, x.Pos, out var stand))
            {
                b.BuildEntry = entry;
                b.RepairKind = kind;
                b.BuildSince = now;
                b.RepairIdlePicks = 0;
                b.PathReplans = 0;
                SetWalk(x, plan, stand, now);
            }
            else
            {
                b.RepairIdlePicks++;
            }
        }

        if (b.BuildEntry == null)
        {
            if (b.RepairIdlePicks >= 3 && now >= b.RepairMinUntil)
            {
                EndRepair(x, now);
                return;
            }

            b.GoalPos = null;
            return;
        }

        StepWalk(x, plan, now);
        b.GoalPos = b.BuildStandPos;

        var entryPos = _hive.EntryWorld(plan, b.BuildEntry.Offset);
        if ((entryPos - x.Pos).Length() > b.BuildReach || now < b.NextBuildAt)
            return;

        var done = b.BuildEntry;
        var doneKind = b.RepairKind;
        switch (_hive.Repair(hive, plan, done, doneKind, now))
        {
            case XenoPlaceResult.Placed:
                b.NextBuildAt = now + _hive.BuildDelay(hive.Comp, doneKind switch
                {
                    XenoRepairKind.Mend => hive.Comp.MendInterval,
                    XenoRepairKind.Upgrade => hive.Comp.ReinforcedBuildInterval,
                    _ => done.Kind == XenoPlanKind.Reinforced
                        ? hive.Comp.ReinforcedBuildInterval
                        : hive.Comp.BuildInterval,
                }, now);
                b.BuildEntry = null;
                b.BuildStand = null;
                b.NextPickAt = now;
                break;
            case XenoPlaceResult.Skip:
                b.BuildEntry = null;
                b.BuildStand = null;
                break;
        }
    }
}
