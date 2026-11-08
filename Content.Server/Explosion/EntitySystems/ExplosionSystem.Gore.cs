// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Shared.Body;
using Content.Shared.Explosion;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Robust.Shared.Audio;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Spawners;

namespace Content.Server.Explosion.EntitySystems;

internal readonly record struct BlastContext(float Fraction, float Power, bool Physical);

public sealed partial class ExplosionSystem
{
    [Dependency] private BodySystem _body = default!;
    [Dependency] private DetachableOrganSystem _detachableOrgan = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedVisualBodySystem _visualBody = default!;

    private const float TearPower = 1900f;
    private const float TearFractionBase = 0.3f;
    private const float TearFractionPerPower = 0.12f;
    private const float IgniteSeverity = 0.1f;

    private const float TearDistanceMin = 3f;
    private const float TearDistanceMax = 7f;
    private const float LimbDistanceMin = 1.5f;
    private const float LimbDistanceMax = 3.5f;
    private const float ItemDistanceMin = 3f;
    private const float ItemDistanceMax = 8f;
    private const float BlastPartThrowSpeed = 12f;
    private const float BlastPartSpreadDegrees = 35f;

    private const float FireLifetimeFactor = 0.85f;

    private static readonly EntProtoId BlastFire = "BlastFire";

    private static readonly ProtoId<OrganCategoryPrototype> ArmLeftCategory = "ArmLeft";
    private static readonly ProtoId<OrganCategoryPrototype> ArmRightCategory = "ArmRight";

    private static readonly SoundSpecifier BlastTearSound =
        new SoundCollectionSpecifier("gib", AudioParams.Default.WithVariation(0.05f));

    private static readonly HashSet<ProtoId<OrganCategoryPrototype>> TearCategories = new()
    {
        "Head",
        "ArmLeft",
        "ArmRight",
        "LegLeft",
        "LegRight",
    };

    private static readonly HashSet<ProtoId<OrganCategoryPrototype>> LimbCategories = new()
    {
        "ArmLeft",
        "ArmRight",
        "LegLeft",
        "LegRight",
    };

    internal static bool IsPhysicalBlast(ExplosionPrototype type)
    {
        return type.TileBreakChance(1000000f) > 0f;
    }

    internal bool ApplyBlastGore(EntityUid uid, MapCoordinates epicenter, BlastContext blast)
    {
        if (!blast.Physical
            || !HasComp<MobStateComponent>(uid)
            || EntityManager.IsQueuedForDeletion(uid))
        {
            return false;
        }

        var powerScale = Math.Clamp(MathF.Sqrt(blast.Power / TearPower), 0.2f, 1.5f);
        var severity = (1f - blast.Fraction) * powerScale;

        var direction = _transformSystem.GetMapCoordinates(uid).Position - epicenter.Position;
        if (direction.LengthSquared() < 0.001f)
            direction = _robustRandom.NextVector2();

        var tearFraction = Math.Clamp(TearFractionBase + TearFractionPerPower * (powerScale - 1f), TearFractionBase, 0.5f);
        var charLevel = Math.Clamp(Math.Min(blast.Power / TearPower, 1f) * 0.8f + (1f - blast.Fraction) * 0.3f, 0f, 1f);

        if (blast.Power >= TearPower && blast.Fraction <= tearFraction && TearApart(uid, direction, charLevel))
            return true;

        var limbs = (int) MathF.Floor(severity * 3f + _robustRandom.NextFloat(0f, 0.8f));
        if (limbs > 0)
            RemoveLimbs(uid, direction, limbs, charLevel);

        if (severity >= IgniteSeverity)
            CharBody(uid, charLevel);

        if (severity >= IgniteSeverity
            && !EntityManager.IsQueuedForDeletion(uid)
            && _flammableQuery.TryGetComponent(uid, out var flammable))
        {
            _flammableSystem.AdjustFireStacks(uid, Math.Clamp(1.5f + severity * 6f, 1.5f, 8f), flammable, ignite: true);
        }

        return false;
    }

    private bool TearApart(EntityUid uid, Vector2 direction, float charLevel)
    {
        var parts = CollectDetachable(uid, TearCategories);
        if (parts.Count == 0)
            return false;

        var coordinates = Transform(uid).Coordinates;

        ThrowHeldItems(uid, direction);

        foreach (var part in parts)
        {
            DetachAndThrow(part, direction, TearDistanceMin, TearDistanceMax, charLevel);
        }

        _audio.PlayPvs(BlastTearSound, coordinates);
        QueueDel(uid);
        return true;
    }

    private void RemoveLimbs(EntityUid uid, Vector2 direction, int count, float charLevel)
    {
        var limbs = CollectDetachable(uid, LimbCategories);
        if (limbs.Count == 0)
            return;

        _robustRandom.Shuffle(limbs);

        var removed = Math.Min(count, limbs.Count);
        var armLost = false;
        for (var i = 0; i < removed; i++)
        {
            if (TryComp<OrganComponent>(limbs[i], out var organ)
                && (organ.Category == ArmLeftCategory || organ.Category == ArmRightCategory))
            {
                armLost = true;
            }
        }

        if (armLost)
            ThrowHeldItems(uid, direction);

        for (var i = 0; i < removed; i++)
        {
            DetachAndThrow(limbs[i], direction, LimbDistanceMin, LimbDistanceMax, charLevel);
        }

        _audio.PlayPvs(BlastTearSound, uid);
    }

    private List<EntityUid> CollectDetachable(EntityUid body, HashSet<ProtoId<OrganCategoryPrototype>> categories)
    {
        var result = new List<EntityUid>();

        foreach (var organ in _body.EnumerateOrgans<DetachableOrganComponent>(body))
        {
            if (organ.Comp1.Category is { } category && categories.Contains(category))
                result.Add(organ.Owner);
        }

        return result;
    }

    private void DetachAndThrow(EntityUid organ, Vector2 direction, float distanceMin, float distanceMax, float charLevel)
    {
        if (_detachableOrgan.Detach(organ) is not { } piece)
            return;

        CharBody(piece, charLevel);
        ThrowAway(piece, direction, distanceMin, distanceMax);
    }

    private void CharBody(EntityUid body, float charLevel)
    {
        if (!_visualBody.TryGatherMarkingsData(body, null, out var profiles, out _, out _))
            return;

        var result = new Dictionary<ProtoId<OrganCategoryPrototype>, OrganProfileData>();

        foreach (var (category, profile) in profiles)
        {
            var level = Math.Clamp(charLevel + _robustRandom.NextFloat(-0.1f, 0.1f), 0f, 1f);
            var data = profile;
            data.SkinColor = CharColor(profile.SkinColor, level);
            result[category] = data;
        }

        _visualBody.ApplyProfiles(body, result);
    }

    private static Color CharColor(Color skin, float level)
    {
        var red = new Color(0.65f, 0.27f, 0.20f);
        var brown = new Color(0.36f, 0.20f, 0.13f);
        var black = new Color(0.08f, 0.065f, 0.055f);

        if (level < 0.35f)
            return Color.InterpolateBetween(skin, red, level / 0.35f);

        if (level < 0.7f)
            return Color.InterpolateBetween(red, brown, (level - 0.35f) / 0.35f);

        return Color.InterpolateBetween(brown, black, (level - 0.7f) / 0.3f);
    }

    private void ThrowHeldItems(EntityUid uid, Vector2 direction)
    {
        var held = new List<EntityUid>(_hands.EnumerateHeld(uid));

        foreach (var item in held)
        {
            if (_hands.TryDrop(uid, item, checkActionBlocker: false, doDropInteraction: false))
                ThrowAway(item, direction, ItemDistanceMin, ItemDistanceMax);
        }
    }

    private void ThrowAway(EntityUid uid, Vector2 direction, float distanceMin, float distanceMax)
    {
        var rotated = Angle.FromDegrees(_robustRandom.NextFloat(-BlastPartSpreadDegrees, BlastPartSpreadDegrees)).RotateVec(direction);
        var vector = rotated.Normalized() * _robustRandom.NextFloat(distanceMin, distanceMax);

        _throwingSystem.TryThrow(uid, vector, BlastPartThrowSpeed, playSound: false, recoil: false);
    }

    internal int BlastSeed(EntityUid visual)
    {
        return GetNetEntity(visual).Id;
    }

    internal bool TrySpawnBlastFire(Entity<MapGridComponent> grid, Vector2i tile, int seed, float fraction, float power)
    {
        if (!BlastBurn.IsBurning(tile, seed, fraction))
            return false;

        var fire = Spawn(BlastFire, _map.GridTileToLocal(grid.Owner, grid.Comp, tile));

        if (TryComp<TimedDespawnComponent>(fire, out var despawn))
        {
            var variation = 0.6f + 0.8f * BlastBurn.Hash(tile, seed + 7);
            despawn.Lifetime = BlastBurn.LingerSeconds(power) * FireLifetimeFactor * variation;
        }

        return true;
    }
}
