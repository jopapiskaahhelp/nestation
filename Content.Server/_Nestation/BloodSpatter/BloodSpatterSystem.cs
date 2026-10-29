// Nestation new city CC BY-NC 3.0
using System.Numerics;
using Content.Server.Decals;
using Content.Shared.Body.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Decals;
using Content.Shared.GameTicking;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.BloodSpatter;

public sealed partial class BloodSpatterSystem : EntitySystem
{
    private static readonly string[] Streaks = { "NestationBloodStreakA", "NestationBloodStreakB", "NestationBloodStreakC" };
    private static readonly string[] Dots = { "NestationBloodDotA", "NestationBloodDotB", "NestationBloodDotC" };
    private static readonly string[] DamageTypes = { "Slash", "Piercing", "Blunt" };
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private const float MinDamage = 6f;
    private const float FullDamage = 30f;
    private const float DamagePerDroplet = 3.5f;
    private const int MinDroplets = 2;
    private const int MaxDroplets = 9;
    private const float MaxSpread = 0.45f;
    private const float NearDistance = 0.35f;
    private const float FarDistance = 1.5f;
    private const float MinReach = 0.5f;
    private const float WallPadding = 0.12f;
    private const float MinDropDistance = 0.15f;
    private const float DecalCenterOffset = 0.25f;
    private const float StreakChance = 0.6f;
    private const float StreakZone = 0.65f;

    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Queue<(EntityUid Grid, DecalIndex Index, TimeSpan Expire)> _active = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DamageableComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;

        while (_active.TryPeek(out var next) && next.Expire <= now)
        {
            _active.Dequeue();

            if (Exists(next.Grid))
                _decals.RemoveDecal(next.Grid, next.Index);
        }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _active.Clear();
    }

    private void OnDamageChanged(Entity<DamageableComponent> ent, ref DamageChangedEvent args)
    {
        if (!HasComp<BloodstreamComponent>(ent))
            return;

        if (!args.DamageIncreased || args.DamageDelta is not { } delta || args.Origin is not { } origin)
            return;

        var damage = 0f;
        foreach (var type in DamageTypes)
        {
            if (delta.DamageDict.TryGetValue(type, out var value) && value > 0)
                damage += value.Float();
        }

        if (damage < MinDamage)
            return;

        var xform = Transform(ent);
        if (xform.GridUid is not { } gridUid)
            return;

        var victimPos = _transform.GetWorldPosition(xform);
        var dir = GetSprayDirection(ent, origin, xform, victimPos);
        if (dir == Vector2.Zero)
            return;

        var gridXform = Transform(gridUid);
        var gridRotation = _transform.GetWorldRotation(gridXform);
        var strength = Math.Clamp(damage / FullDamage, 0f, 1f);
        var reach = MinReach + (FarDistance - MinReach) * strength;
        var count = Math.Clamp((int) (damage / DamagePerDroplet) + 1, MinDroplets, MaxDroplets);
        var expire = _timing.CurTime + Lifetime;

        for (var i = 0; i < count; i++)
        {
            var shot = Rotate(dir, _random.NextFloat(-MaxSpread, MaxSpread));
            var distance = NearDistance + MathF.Pow(_random.NextFloat(), 1.4f) * (reach - NearDistance);

            var wall = DistanceToWall(xform.MapID, victimPos, shot, distance + WallPadding, ent.Owner);
            if (wall < distance + WallPadding)
                distance = wall - WallPadding;

            if (distance < MinDropDistance)
                continue;

            var worldPoint = victimPos + shot * distance;
            var coords = _transform.ToCoordinates((gridUid, gridXform), new MapCoordinates(worldPoint, xform.MapID))
                .Offset(new Vector2(-DecalCenterOffset, -DecalCenterOffset));

            var streak = distance < reach * StreakZone && _random.NextFloat() < StreakChance;
            var id = streak ? _random.Pick(Streaks) : _random.Pick(Dots);

            Angle angle;
            if (streak)
                angle = new Angle(MathF.Atan2(-shot.X, shot.Y) + _random.NextFloat(-0.15f, 0.15f)) - gridRotation;
            else
                angle = new Angle(_random.NextFloat(0f, MathF.Tau));

            var color = Color.White.WithAlpha(_random.NextFloat(0.7f, 1f));

            if (_decals.TryAddDecal(id, coords, out var index, color, angle, 0, true))
                _active.Enqueue((gridUid, index, expire));
        }
    }

    private Vector2 GetSprayDirection(EntityUid victim, EntityUid origin, TransformComponent victimXform, Vector2 victimPos)
    {
        if (origin != victim
            && TryComp(origin, out TransformComponent? originXform)
            && originXform.MapID == victimXform.MapID)
        {
            var delta = victimPos - _transform.GetWorldPosition(originXform);
            if (delta.LengthSquared() > 0.01f)
                return Vector2.Normalize(delta);
        }

        if (origin != victim)
            return Vector2.Zero;

        var facing = _transform.GetWorldRotation(victimXform).GetDir().ToVec();
        return facing == Vector2.Zero ? Vector2.Zero : -Vector2.Normalize(facing);
    }

    private float DistanceToWall(MapId map, Vector2 from, Vector2 dir, float length, EntityUid ignore)
    {
        var ray = new CollisionRay(from, dir, (int) CollisionGroup.Impassable);
        var closest = float.MaxValue;

        foreach (var hit in _physics.IntersectRay(map, ray, length, ignore, false))
        {
            if (hit.Distance < closest)
                closest = hit.Distance;
        }

        return closest;
    }

    private static Vector2 Rotate(Vector2 v, float radians)
    {
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }
}
