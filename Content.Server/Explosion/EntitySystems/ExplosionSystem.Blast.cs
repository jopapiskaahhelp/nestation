// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Server.Decals;
using Content.Shared.Explosion;
using Content.Shared.Stunnable;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server.Explosion.EntitySystems;

public sealed partial class ExplosionSystem
{
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private SharedStunSystem _stun = default!;

    public const int BlastMarksPerExplosion = 400;

    private const float BlastSootChanceBonus = 0.2f;
    private const float BlastScorchSeverity = 0.3f;
    private const float BlastCraterSeverity = 0.85f;
    private const float BlastCraterChance = 0.35f;
    private const float BlastBigCraterSeverity = 0.6f;
    private const float BlastBigCraterDistanceSquared = 0.5f;
    private const float BlastMarkJitter = 0.2f;

    private const float ConcussionMinDamage = 10f;
    private const float ConcussionDamagePerSecond = 12f;
    private const float ConcussionMaxSeconds = 8f;

    private static readonly string[] BlastCraters = { "BlastCrater1", "BlastCrater2" };
    private static readonly string[] BlastScorches = { "BlastScorch1", "BlastScorch2", "BlastScorch3" };
    private static readonly string[] BlastSoot = { "BlastSoot1", "BlastSoot2" };

    internal bool MarkBlastTile(
        Entity<MapGridComponent> grid,
        Vector2i tile,
        float intensity,
        ExplosionPrototype type,
        MapCoordinates epicenter)
    {
        var severity = type.TileBreakChance(intensity);

        if (type.FireStacks is > 0f)
            severity = MathF.Max(severity, 0.5f);

        if (severity <= 0f)
            return false;

        if (!_robustRandom.Prob(MathF.Min(1f, severity + BlastSootChanceBonus)))
            return false;

        var center = _map.GridTileToLocal(grid.Owner, grid.Comp, tile);
        var delta = _transformSystem.ToMapCoordinates(center).Position - epicenter.Position;
        var angle = _robustRandom.NextAngle();

        if (delta.LengthSquared() > 0.001f)
            angle = delta.ToWorldAngle() - _transformSystem.GetWorldRotation(grid.Owner);

        if (severity >= BlastBigCraterSeverity && delta.LengthSquared() < BlastBigCraterDistanceSquared)
        {
            _decals.TryAddDecal("BlastCraterBig", center, out _, rotation: angle, zIndex: 3, cleanable: false);
            _decals.TryAddDecal("BlastScorchBig", center, out _, rotation: _robustRandom.NextAngle(), zIndex: 1, cleanable: true);
            return true;
        }

        var coords = center.Offset(new Vector2(
            _robustRandom.NextFloat(-BlastMarkJitter, BlastMarkJitter),
            _robustRandom.NextFloat(-BlastMarkJitter, BlastMarkJitter)));

        string id;
        var cleanable = true;
        var zIndex = 0;

        if (severity >= BlastCraterSeverity && _robustRandom.Prob(BlastCraterChance))
        {
            id = _robustRandom.Pick(BlastCraters);
            cleanable = false;
            zIndex = 3;
        }
        else if (severity >= BlastScorchSeverity)
        {
            id = _robustRandom.Pick(BlastScorches);
            zIndex = 2;
        }
        else
        {
            id = _robustRandom.Pick(BlastSoot);
        }

        return _decals.TryAddDecal(id, coords, out _, rotation: angle, zIndex: zIndex, cleanable: cleanable);
    }

    private void ApplyBlastConcussion(EntityUid uid, float damage)
    {
        if (damage < ConcussionMinDamage)
            return;

        var seconds = Math.Clamp(damage / ConcussionDamagePerSecond, 1f, ConcussionMaxSeconds);
        _stun.TryKnockdown(uid, TimeSpan.FromSeconds(seconds));
    }
}
