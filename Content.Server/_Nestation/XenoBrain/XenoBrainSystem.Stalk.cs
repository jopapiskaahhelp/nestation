// Nestation new city CC BY-NC 3.0
using System.Numerics;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._Nestation.XenoBrain;

public sealed partial class XenoBrainSystem
{
    [Dependency] private SharedRoofSystem _roof = default!;
    [Dependency] private SharedMapSystem _maps = default!;

    private void InitializeStalk()
    {
        SubscribeLocalEvent<GunComponent, GunShotEvent>(OnGunShot);
    }

    private bool IsRoofedOnGrid(EntityUid? gridUid, Vector2 worldPos)
    {
        if (gridUid is not { } g
            || !TryComp<MapGridComponent>(g, out var grid)
            || !TryComp<RoofComponent>(g, out var roof))
        {
            return false;
        }

        var tile = _maps.WorldToTile(g, grid, worldPos);
        return _roof.IsRooved((g, grid, roof), tile);
    }

    private bool IsRoofed(EntityUid ent, Vector2 worldPos)
    {
        return IsRoofedOnGrid(Transform(ent).GridUid, worldPos);
    }

    private bool TryStartStalk(Ctx c, EntityUid target)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;

        b.StalkRollReset = now + TimeSpan.FromSeconds(20);

        if (b.StalkRolled || now < b.NoStalkUntil)
            return false;

        if (now < b.LeapUntil || b.ForcedTarget != null)
            return false;

        if (c.TargetDist < b.StalkDistance - 1f || c.TargetDist > b.SightRange)
            return false;

        if ((now - b.LastHitTime).TotalSeconds < 10)
            return false;

        if (IsRoofed(x.Uid, x.Pos) || IsRoofed(target, c.TargetPos))
            return false;

        foreach (var a in _xenos)
        {
            if (a.Uid != x.Uid && a.Brain.Mode == XenoMode.Stalk && a.Brain.StalkTarget == target)
                return false;
        }

        b.StalkRolled = true;
        if (!_random.Prob(b.StalkChance))
            return false;

        b.StalkTarget = target;
        b.StalkPhase = XenoStalkPhase.Watch;
        b.StalkWaitSpot = null;
        b.StalkSince = now;
        SetMode(x, XenoMode.Stalk, now);
        return true;
    }

    private void ThinkStalk(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;

        if (b.StalkTarget is not { } st || !TryGetPlayer(st, x.Map, out var p))
        {
            b.StalkTarget = null;
            b.StalkWaitSpot = null;
            b.NoStalkUntil = now + TimeSpan.FromSeconds(b.StalkCooldown);
            SetMode(x, XenoMode.Patrol, now);
            return;
        }

        var dist = (p.Pos - x.Pos).Length();

        if (IsRoofed(st, p.Pos))
        {
            if (b.StalkPhase != XenoStalkPhase.Wait)
            {
                b.StalkPhase = XenoStalkPhase.Wait;
                b.StalkWaitSpot = FindWaitSpot(x, st, p.Pos);
            }

            return;
        }

        if (b.StalkPhase == XenoStalkPhase.Wait)
        {
            StalkPounce(x, st, now);
            return;
        }

        var timeUp = b.StalkWatchSeconds > 0f && (now - b.StalkSince).TotalSeconds > b.StalkWatchSeconds;
        if (dist <= b.StalkPounceDist
            || (now - b.LastHitTime).TotalSeconds < 3
            || timeUp
            || AllyEngaged(x, p.Pos))
        {
            StalkPounce(x, st, now);
        }
    }

    private bool AllyEngaged(XenoInfo x, Vector2 playerPos)
    {
        foreach (var a in _xenos)
        {
            if (a.Uid == x.Uid || a.Map != x.Map || a.Brain.Mode != XenoMode.Attack)
                continue;

            if ((a.Pos - playerPos).Length() < 6f)
                return true;
        }

        return false;
    }

    private void StalkPounce(XenoInfo x, EntityUid target, TimeSpan now)
    {
        var b = x.Brain;
        b.StalkTarget = null;
        b.StalkWaitSpot = null;
        b.NoStalkUntil = now + TimeSpan.FromSeconds(b.StalkCooldown);

        b.ForcedTarget = target;
        b.ForcedUntil = now + TimeSpan.FromSeconds(15);
        b.NoLurkUntil = now + TimeSpan.FromSeconds(15);
        b.RecentHits.Clear();
        Leap(x, now);
    }

    private Vector2 FindWaitSpot(XenoInfo x, EntityUid player, Vector2 playerPos)
    {
        var grid = Transform(player).GridUid;
        var delta = playerPos - x.Pos;
        var len = delta.Length();
        if (len < 0.5f)
            return x.Pos;

        var dir = delta / len;
        const float step = 0.5f;
        var last = x.Pos;

        for (var d = step; d <= len; d += step)
        {
            var p = x.Pos + dir * d;
            if (IsRoofedOnGrid(grid, p)
                || RayHit(x.Map, last, dir, step, OpaqueMask, x.Uid, out _, out _, out _))
            {
                break;
            }

            last = p;
        }

        return last - dir * 0.6f;
    }

    private void OnGunShot(Entity<GunComponent> gun, ref GunShotEvent args)
    {
        var shooter = args.User;
        if (!HasComp<ActorComponent>(shooter) || HasComp<XenoBrainComponent>(shooter))
            return;

        var xf = Transform(shooter);
        var pos = _transform.GetWorldPosition(xf);
        var now = _timing.CurTime;

        foreach (var x in _xenos)
        {
            var b = x.Brain;
            if (b.Mode != XenoMode.Stalk || x.Map != xf.MapID)
                continue;

            if (b.StalkTarget != shooter && (x.Pos - pos).Length() > b.HearingRange * 2f)
                continue;

            FleeFromGun(x, pos, now);
        }
    }

    private void FleeFromGun(XenoInfo x, Vector2 shooterPos, TimeSpan now)
    {
        var b = x.Brain;
        b.StalkTarget = null;
        b.StalkWaitSpot = null;
        b.NoStalkUntil = now + TimeSpan.FromSeconds(b.StalkCooldown * 1.5f);

        b.ThreatPos = shooterPos;
        b.RetreatSpot = null;
        b.RecentHits.Clear();
        AddDanger(x.Map, shooterPos, now);
        SetMode(x, XenoMode.Retreat, now);
    }
}
