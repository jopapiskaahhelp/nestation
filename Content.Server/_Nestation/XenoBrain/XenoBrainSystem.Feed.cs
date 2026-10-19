// Nestation Anarhey CC BY-NC 3.0
using System.Numerics;
using Content.Shared.Body;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._Nestation.XenoBrain;

public sealed partial class XenoBrainSystem
{
    [Dependency] private BodySystem _body = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private void InitializeFeed()
    {
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Critical)
            return;

        var victim = args.Target;
        if (!HasComp<ActorComponent>(victim) || HasComp<XenoBrainComponent>(victim))
            return;

        var xf = Transform(victim);
        var pos = _transform.GetWorldPosition(xf);
        var now = _timing.CurTime;

        XenoInfo? best = null;
        var bestDist = float.MaxValue;
        foreach (var x in _xenos)
        {
            var b = x.Brain;
            if (x.Map != xf.MapID || b.DuelOpponent != null || b.FeedTarget != null || now < b.NoFeedUntil)
                continue;

            if (b.Mode is XenoMode.Retreat or XenoMode.Ambush)
                continue;

            var d = (x.Pos - pos).Length();
            if (d > b.FeedRange || d >= bestDist)
                continue;

            bestDist = d;
            best = x;
        }

        if (best is { } chosen)
            StartFeed(chosen, victim, now);
    }

    private void StartFeed(XenoInfo x, EntityUid victim, TimeSpan now)
    {
        var b = x.Brain;
        b.FeedTarget = victim;
        b.FeedStart = TimeSpan.Zero;
        b.FeedDeadline = now + TimeSpan.FromSeconds(b.FeedMaxSeconds);
        b.ForcedTarget = null;
        b.StalkTarget = null;
        b.SmashActive = false;
        b.RecentHits.Clear();
        SetMode(x, XenoMode.Feed, now);
    }

    private void ThinkFeed(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;

        if (b.FeedTarget is not { } victim
            || now > b.FeedDeadline
            || !TryGetCritical(victim, x.Map, out var victimPos)
            || (b.LastPlayerAttackTime != TimeSpan.Zero && (now - b.LastPlayerAttackTime).TotalSeconds < 2))
        {
            EndFeed(x, now);
            return;
        }

        var dist = (victimPos - x.Pos).Length();
        b.Target = victim;
        b.GoalPos = victimPos;
        b.TargetDist = dist;
        b.TargetRanged = false;
        b.ForcedTarget = null;

        if (dist > 2.2f)
        {
            b.FeedStart = TimeSpan.Zero;
            return;
        }

        if (b.FeedStart == TimeSpan.Zero)
            b.FeedStart = now;

        if ((now - b.FeedStart).TotalSeconds < b.FeedSeconds)
            return;

        SeverLimb(x.Uid, victim, x.Pos, victimPos);
        EndFeed(x, now);
    }

    private void EndFeed(XenoInfo x, TimeSpan now)
    {
        var b = x.Brain;
        b.FeedTarget = null;
        b.FeedStart = TimeSpan.Zero;
        b.NoFeedUntil = now + TimeSpan.FromSeconds(20);
        b.Target = null;
        b.GoalPos = null;
        SetMode(x, XenoMode.Patrol, now);
    }

    private bool TryGetCritical(EntityUid uid, MapId map, out Vector2 pos)
    {
        pos = default;
        if (TerminatingOrDeleted(uid) || !TryComp<MobStateComponent>(uid, out var ms) || !_mobState.IsCritical(uid, ms))
            return false;

        var xf = Transform(uid);
        if (xf.MapID != map)
            return false;

        pos = _transform.GetWorldPosition(xf);
        return true;
    }

    private static bool HasAny(string id, params string[] parts)
    {
        foreach (var part in parts)
        {
            if (id.Contains(part, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static int SideOf(string id)
    {
        if (HasAny(id, "Left"))
            return 1;

        return HasAny(id, "Right") ? 2 : 0;
    }

    private const float HeadChance = 0.1f;

    private static readonly string[] TearSounds =
    {
        "/Audio/Effects/gib1.ogg",
        "/Audio/Effects/gib2.ogg",
        "/Audio/Effects/gib3.ogg",
    };

    private bool DetachOrgan(EntityUid part, Vector2 victimPos, Vector2 dir, Vector2 jitter)
    {
        if (!_container.TryGetContainingContainer(part, out var container) || !_container.Remove(part, container))
            return false;

        _transform.SetWorldPosition(part, victimPos + dir * 0.5f + jitter);

        if (TryComp<PhysicsComponent>(part, out var phys))
            _physics.SetLinearVelocity(part, dir * 3f + jitter * 2f, body: phys);

        return true;
    }

    private void PlayTear(EntityUid victim)
    {
        var path = TearSounds[_random.Next(TearSounds.Length)];
        _audio.PlayPvs(new SoundPathSpecifier(path), victim, AudioParams.Default.WithVolume(-8f));
    }

    private void SeverLimb(EntityUid xeno, EntityUid victim, Vector2 xenoPos, Vector2 victimPos)
    {
        var organs = new List<(EntityUid Uid, string Id)>();
        foreach (var organ in _body.EnumerateOrgans<MetaDataComponent>(new Entity<BodyComponent?>(victim, null)))
        {
            organs.Add((organ.Owner, organ.Comp2.EntityPrototype?.ID ?? string.Empty));
        }

        var allIds = string.Join(", ", organs.ConvertAll(o => o.Id));

        var away = victimPos - xenoPos;
        var dir = away.LengthSquared() < 0.01f
            ? _random.NextAngle().ToVec()
            : Vector2.Normalize(new Vector2(-away.Y, away.X));

        if (_random.Prob(0.5f))
            dir = -dir;

        if (_random.Prob(HeadChance))
        {
            foreach (var o in organs)
            {
                if (!HasAny(o.Id, "Head"))
                    continue;

                if (DetachOrgan(o.Uid, victimPos, dir, Vector2.Zero))
                {
                    PlayTear(victim);
                    _mobState.ChangeMobState(victim, MobState.Dead, origin: xeno);
                }

                return;
            }

            Log.Warning($"Xeno feed: no head organ found on {ToPrettyString(victim)}, organs: {allIds}");
        }

        var candidates = new List<(EntityUid Uid, string Id)>();
        foreach (var o in organs)
        {
            if (HasAny(o.Id, "Arm", "Leg") && !HasAny(o.Id, "Hand", "Foot", "Feet"))
                candidates.Add(o);
        }

        if (candidates.Count == 0)
        {
            Log.Warning($"Xeno feed: no arm/leg organs found on {ToPrettyString(victim)}, organs: {allIds}");
            return;
        }

        var chosen = candidates[_random.Next(candidates.Count)];
        var isArm = HasAny(chosen.Id, "Arm");
        var side = SideOf(chosen.Id);

        var parts = new List<EntityUid> { chosen.Uid };
        if (side != 0)
        {
            foreach (var o in organs)
            {
                if (o.Uid == chosen.Uid || SideOf(o.Id) != side)
                    continue;

                if (isArm ? HasAny(o.Id, "Hand") : HasAny(o.Id, "Foot", "Feet"))
                    parts.Add(o.Uid);
            }
        }

        if (parts.Count == 1)
            Log.Warning($"Xeno feed: no hand/foot organ found next to {chosen.Id}, organs: {allIds}");

        var detached = false;
        for (var i = 0; i < parts.Count; i++)
        {
            var jitter = i == 0 ? Vector2.Zero : _random.NextAngle().ToVec() * 0.3f;
            detached |= DetachOrgan(parts[i], victimPos, dir, jitter);
        }

        if (detached)
            PlayTear(victim);
    }
}
