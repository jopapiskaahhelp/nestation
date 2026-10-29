// Nestation new city CC BY-NC 3.0
using System.Numerics;
using Content.Server._Nestation.XenoHive;
using Content.Shared._Nestation.XenoHive;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._Nestation.XenoBrain;

public sealed partial class XenoBrainSystem
{
    private bool HasSight(XenoInfo x, Vector2 pos)
    {
        var delta = pos - x.Pos;
        var d = delta.Length();
        if (d < 0.5f)
            return true;

        return !RayHit(x.Map, x.Pos, delta / d, d, OpaqueMask, x.Uid, out _, out _, out _);
    }

    private int CountOthers(EntityUid target, MapId map, Vector2 pos, float range)
    {
        var n = 0;
        foreach (var o in _players)
        {
            if (o.Uid != target && o.Map == map && (o.Pos - pos).Length() <= range)
                n++;
        }

        return n;
    }

    private float TargetScore(XenoInfo x, PlayerInfo p, float dist, TimeSpan now)
    {
        var b = x.Brain;
        var score = dist;
        var others = CountOthers(p.Uid, p.Map, p.Pos, 9f);

        if (others == 0)
            score -= 6f;
        else if (others >= 2 && b.Boldness < 0.8f)
            score += 8f * others;

        if (HpFrac(p.Uid) < 0.5f)
            score -= 5f;

        if (IsRangedDanger(p.Uid, now))
            score += 6f * (1.2f - b.Boldness);

        return score;
    }

    private void TrackIdle(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;

        if (b.Mode is not (XenoMode.Patrol or XenoMode.Detour) || b.Caste == XenoCaste.Burrower
            || _squadOf.ContainsKey(x.Uid))
        {
            b.IdleAnchor = x.Pos;
            b.IdleSince = now;
            b.OscAnchor = x.Pos;
            b.OscLast = x.Pos;
            b.OscSince = now;
            b.OscPath = 0f;
            return;
        }

        b.OscPath += (x.Pos - b.OscLast).Length();
        b.OscLast = x.Pos;

        if ((now - b.OscSince).TotalSeconds >= 20.0)
        {
            var wander = b.OscPath > 10f && (x.Pos - b.OscAnchor).Length() < 4.5f;
            b.OscAnchor = x.Pos;
            b.OscSince = now;
            b.OscPath = 0f;

            if (wander)
            {
                RememberRoam(x.Uid, x.Pos);
                b.AvoidPoints.Add((x.Pos, now + TimeSpan.FromSeconds(60)));
                StartUnstick(x, now);
                return;
            }
        }

        if ((x.Pos - b.IdleAnchor).Length() >= 2f)
        {
            b.IdleAnchor = x.Pos;
            b.IdleSince = now;
            return;
        }

        if ((now - b.IdleSince).TotalSeconds < 8.0)
            return;

        b.IdleAnchor = x.Pos;
        b.IdleSince = now;
        b.LastRoamDir = Vector2.Zero;
        b.CautiousUntil = TimeSpan.Zero;
        StartUnstick(x, now);
    }

    private void TrackChase(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;

        if (!c.TargetIsPlayer || c.Target is not { } target || b.ForcedTarget != null
            || b.Mode is not (XenoMode.Attack or XenoMode.Detour) || _squadOf.ContainsKey(x.Uid))
        {
            b.ChaseTarget = null;
            return;
        }

        if (b.ChaseTarget != target)
        {
            b.ChaseTarget = target;
            b.ChaseSince = now;
            b.ChaseStartDist = c.TargetDist;
            b.ChasePrevPos = c.TargetPos;
            b.ChaseAbortAfter = b.ChaseAbortMin + _random.NextFloat() * (b.ChaseAbortMax - b.ChaseAbortMin);
            b.ChaseNextCheck = now + TimeSpan.FromSeconds(1);
            return;
        }

        if (now < b.ChaseNextCheck)
            return;

        b.ChaseNextCheck = now + TimeSpan.FromSeconds(1);
        var velocity = c.TargetPos - b.ChasePrevPos;
        b.ChasePrevPos = c.TargetPos;

        if (b.ChaseStartDist - c.TargetDist >= 1.5f || c.TargetDist <= 4f)
        {
            b.ChaseSince = now;
            b.ChaseStartDist = c.TargetDist;
            b.ChaseAbortAfter = b.ChaseAbortMin + _random.NextFloat() * (b.ChaseAbortMax - b.ChaseAbortMin);
            return;
        }

        if ((now - b.ChaseSince).TotalSeconds < b.ChaseAbortAfter)
            return;

        if (!HeadsToHumans(target, c.TargetPos, velocity, x.Map))
            return;

        b.AbortTarget = target;
        b.AbortUntil = now + TimeSpan.FromSeconds(20 + _random.NextFloat() * 25f);
        b.ChaseTarget = null;
        StartHide(x, c.TargetPos, now);
    }

    private bool HeadsToHumans(EntityUid target, Vector2 pos, Vector2 velocity, MapId map)
    {
        if (CountOthers(target, map, pos, 7f) >= 1)
            return true;

        if (velocity.Length() < 0.4f)
            return false;

        var dir = Vector2.Normalize(velocity);
        foreach (var o in _players)
        {
            if (o.Uid == target || o.Map != map)
                continue;

            var delta = o.Pos - pos;
            var d = delta.Length();
            if (d > 18f || d < 0.5f)
                continue;

            if (Vector2.Dot(dir, delta / d) > 0.55f)
                return true;
        }

        return false;
    }

    private void StartHide(XenoInfo x, Vector2 threatPos, TimeSpan now)
    {
        var b = x.Brain;
        b.RecentHits.Clear();
        b.RetreatSpot = null;
        b.ThreatPos = threatPos;
        b.BuildStand = null;
        b.NextTacticAt = now + TimeSpan.FromSeconds(8);
        SetMode(x, XenoMode.Retreat, now);
    }

    private bool TryTactic(Ctx c, EntityUid target)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;

        if (b.Caste == XenoCaste.Burrower || b.DuelOpponent != null || now < b.NextTacticAt
            || _squadOf.ContainsKey(x.Uid))
            return false;

        if (now < b.LeapUntil || b.ForcedTarget != null)
            return false;

        b.NextTacticAt = now + TimeSpan.FromSeconds(3 + _random.NextFloat() * 5f);

        var dist = c.TargetDist;
        var hp = HpFrac(x.Uid);
        var los = HasSight(x, c.TargetPos);
        var others = CountOthers(target, x.Map, c.TargetPos, 10f);

        XenoMazePlan? plan = null;
        if (_hive.TryGetHive(x.Map, out var hive))
            plan = _hive.GetPlan(hive);

        var wHide = dist >= 6f && dist <= 22f ? (b.TargetRanged ? 28f : 14f) * (1.3f - b.Boldness) : 0f;
        var wBait = dist >= 9f && dist <= 22f && los && others <= 1 ? 22f * (1.2f - b.Boldness * 0.5f) : 0f;
        var wWeave = b.TargetRanged && los && dist >= 4.5f && dist <= 22f ? 45f * (0.5f + b.Boldness) : 0f;
        var wRush = dist >= 4f && dist <= 10f && !b.TargetRanged ? 18f * b.Boldness : 0f;
        var wLure = plan != null && hp < 0.65f && dist <= 14f ? 40f : 0f;
        const float wNone = 30f;

        var roll = _random.NextFloat() * (wHide + wBait + wWeave + wRush + wLure + wNone);

        if ((roll -= wHide) < 0f)
        {
            StartHide(x, c.TargetPos, now);
            return true;
        }

        if ((roll -= wBait) < 0f)
        {
            b.TacticPhase = 0;
            b.TacticSince = now;
            b.TacticUntil = now + TimeSpan.FromSeconds(18);
            b.BuildStand = null;
            SetMode(x, XenoMode.Bait, now);
            return true;
        }

        if ((roll -= wWeave) < 0f)
        {
            b.WeaveSide = _random.Prob(0.5f) ? 1 : -1;
            b.NextWeaveAt = now;
            b.TacticUntil = now + TimeSpan.FromSeconds(14);
            b.BuildStand = null;
            SetMode(x, XenoMode.Weave, now);
            return true;
        }

        if ((roll -= wRush) < 0f)
        {
            b.ForcedTarget = target;
            b.ForcedUntil = now + TimeSpan.FromSeconds(6);
            b.RecentHits.Clear();
            Leap(x, now);
            return true;
        }

        if ((roll -= wLure) < 0f && plan != null && _hive.TryPickSpot(plan, 5, plan.Radius - 1, out var tile, x.Pos))
        {
            SetStand(b, plan, tile);
            b.BuildEntry = null;
            b.TacticUntil = now + TimeSpan.FromSeconds(35);
            SetMode(x, XenoMode.Lure, now);
            return true;
        }

        return false;
    }

    private bool ThinkTactic(Ctx c)
    {
        return c.X.Brain.Mode switch
        {
            XenoMode.Bait => ThinkBait(c),
            XenoMode.Weave => ThinkWeave(c),
            XenoMode.Lure => ThinkLure(c),
            XenoMode.Unstick => ThinkUnstick(c),
            _ => false,
        };
    }

    private void StartUnstick(XenoInfo x, TimeSpan now)
    {
        var b = x.Brain;
        b.StallStrikes = 0;
        b.TacticUntil = now + TimeSpan.FromSeconds(8);
        b.BuildStand = null;
        b.BuildEntry = null;
        b.AvoidPoints.Add((x.Pos, now + TimeSpan.FromSeconds(45)));
        if (b.AvoidPoints.Count > 8)
            b.AvoidPoints.RemoveAt(0);

        SetMode(x, XenoMode.Unstick, now);
    }

    private void HandleStall(XenoInfo x, TimeSpan now)
    {
        var b = x.Brain;
        if ((now - b.LastStallAt).TotalSeconds > 30.0)
            b.StallStrikes = 0;

        b.LastStallAt = now;
        b.StallStrikes++;

        var free = b.Mode is XenoMode.Patrol or XenoMode.Detour or XenoMode.Rally
                   && b.Caste != XenoCaste.Burrower && !_squadOf.ContainsKey(x.Uid);

        if (free && b.StallStrikes >= 2)
        {
            StartUnstick(x, now);
            return;
        }

        OnStuck(x, now);
    }

    private bool ThinkUnstick(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;

        if (now > b.TacticUntil || (c.Target != null && c.TargetIsPlayer && c.TargetDist < 14f))
        {
            b.WalkAnchor = x.Pos;
            b.WalkSince = now;
            b.IdleAnchor = x.Pos;
            b.IdleSince = now;
            return EndTactic(x, now);
        }

        if (b.BuildStand != null && (b.BuildStandPos - x.Pos).Length() >= 1.2f)
        {
            b.GoalPos = b.BuildStandPos;
            return true;
        }

        var baseAngle = _random.NextAngle();
        var bestScore = 0f;
        var bestClear = 0f;
        var bestDir = Vector2.Zero;
        for (var k = 0; k < 16; k++)
        {
            var dir = (baseAngle + Angle.FromDegrees(22.5 * k)).ToVec();
            var clear = ClearDistance(x.Map, x.Pos, dir, 10f, x.Uid);
            var score = clear;

            if (b.LastRoamDir != Vector2.Zero && Vector2.Dot(dir, b.LastRoamDir) < -0.4f)
                score *= 0.3f;

            if (IsRecentRoam(x.Uid, x.Pos + dir * MathF.Min(clear, 6f), 3f))
                score *= 0.2f;

            if (score > bestScore)
            {
                bestScore = score;
                bestClear = clear;
                bestDir = dir;
            }
        }

        if (bestClear < 1.5f)
            return EndTactic(x, now);

        b.LastRoamDir = bestDir;
        RememberRoam(x.Uid, x.Pos);
        SetStandWorld(b, x.Map, x.Pos + bestDir * MathF.Min(bestClear - 0.8f, 7f));
        b.GoalPos = b.BuildStand != null ? b.BuildStandPos : (Vector2?) null;
        return true;
    }

    private bool EndTactic(XenoInfo x, TimeSpan now)
    {
        var b = x.Brain;
        b.BuildStand = null;
        b.NextTacticAt = now + TimeSpan.FromSeconds(4);
        SetMode(x, XenoMode.Patrol, now);
        return false;
    }

    private bool ThinkBait(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;

        if (now > b.TacticUntil || c.Target == null || !c.TargetIsPlayer)
            return EndTactic(x, now);

        var los = HasSight(x, c.TargetPos);

        if (b.TacticPhase == 0)
        {
            if (c.TargetDist <= b.BaitDistance + 3f && los)
            {
                b.TacticPhase = 1;
                b.TacticSince = now;
                b.BuildStand = null;
                b.GoalPos = null;
                return true;
            }

            var dir = x.Pos - c.TargetPos;
            var len = dir.Length();
            if (len < 0.1f)
                return EndTactic(x, now);

            var spot = c.TargetPos + dir / len * b.BaitDistance;
            if (b.BuildStand == null || (b.BuildStandPos - spot).Length() > 3f)
                SetStandWorld(b, x.Map, spot);

            b.GoalPos = b.BuildStandPos;
            return true;
        }

        b.GoalPos = null;
        var show = 1.2 + 1.3 * b.Patience;
        if (c.TargetDist < 7f || (now - b.TacticSince).TotalSeconds >= show)
        {
            StartHide(x, c.TargetPos, now);
            return true;
        }

        return true;
    }

    private bool ThinkWeave(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;

        if (now > b.TacticUntil || c.Target == null || !c.TargetIsPlayer || !b.TargetRanged || c.TargetDist > 26f)
            return EndTactic(x, now);

        if (c.TargetDist <= 3.2f)
        {
            b.ForcedTarget = c.Target;
            b.ForcedUntil = now + TimeSpan.FromSeconds(6);
            b.RecentHits.Clear();
            Leap(x, now);
            return true;
        }

        if (now >= b.NextWeaveAt)
        {
            b.NextWeaveAt = now + TimeSpan.FromSeconds(0.6 + _random.NextFloat() * 0.6f);
            if (_random.Prob(0.75f))
                b.WeaveSide = -b.WeaveSide;

            var dir = x.Pos - c.TargetPos;
            var len = MathF.Max(dir.Length(), 0.1f);
            dir /= len;
            var forward = MathF.Max(2.5f, len - 2.2f);
            var perp = new Vector2(-dir.Y, dir.X);
            var spot = c.TargetPos + dir * forward + perp * (b.WeaveSide * (1.8f + _random.NextFloat() * 1.4f));
            SetStandWorld(b, x.Map, spot);
        }

        b.GoalPos = b.BuildStand != null ? b.BuildStandPos : (Vector2?) null;
        return true;
    }

    private bool ThinkLure(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;

        if (b.BuildStand == null || (now > b.TacticUntil && !_squadOf.ContainsKey(x.Uid)))
            return EndTactic(x, now);

        if ((b.BuildStandPos - x.Pos).Length() <= 2.5f)
        {
            b.BuildStand = null;
            b.AmbushUntil = now + TimeSpan.FromSeconds(25);
            SetMode(x, XenoMode.Ambush, now);
            return true;
        }

        b.GoalPos = b.BuildStandPos;
        return true;
    }
}
