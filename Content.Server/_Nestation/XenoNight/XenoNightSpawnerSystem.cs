// Nestation Anarhey CC BY-NC 3.0
using Content.Shared._Nestation.CCVars;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.XenoNight;

public sealed partial class XenoNightSpawnerSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private TurfSystem _turf = default!;

    public override void Update(float frameTime)
    {
        var curTime = _timing.CurTime;

        var query = EntityQueryEnumerator<XenoNightSpawnerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            // Карта на паузе / не инициализирована (например, в маппинге) - не спавним.
            if (Paused(uid))
                continue;

            // Ночь определяется по LightCycle той карты, на которой стоит зона.
            if (!IsNight(xform.MapUid))
            {
                comp.WasNight = false;
                continue;
            }

            // Ночь только что началась: первый спавн через один интервал.
            if (!comp.WasNight)
            {
                comp.WasNight = true;
                comp.NextSpawn = curTime + comp.SpawnInterval;
                continue;
            }

            if (curTime < comp.NextSpawn)
                continue;

            comp.NextSpawn = curTime + comp.SpawnInterval;

            comp.Alive.RemoveWhere(e => TerminatingOrDeleted(e) || _mobState.IsDead(e));
            if (comp.Alive.Count >= comp.MaxAlive || comp.Spawns.Count == 0)
                continue;

            if (xform.MapID == MapId.Nullspace)
                continue;

            var proto = PickWeighted(comp.Spawns);
            var coords = PickSpawnCoordinates(comp, xform);
            comp.Alive.Add(Spawn(proto, coords));
        }
    }

    /// <summary>
    /// Ночь на данной карте? Берём тот же расчёт, что и визуальный цикл (SharedLightCycleSystem),
    /// поэтому спавн совпадает с темнотой на экране.
    /// Если на карте нет включённого LightCycleComponent - ночи нет, зона не спавнит.
    /// </summary>
    public bool IsNight(EntityUid? mapUid)
    {
        if (_cfg.GetCVar(NestationCVars.NightForce))
            return true;

        if (mapUid == null || !TryComp<LightCycleComponent>(mapUid, out var cycle) || !cycle.Enabled)
            return false;

        // ВАЖНО: формула времени должна совпадать с клиентским LightCycleSystem.
        // Предполагается CurTime + Offset (см. примечание в ответе).
        var time = (float) (_timing.CurTime + cycle.Offset).TotalSeconds;
        var dayFactor = SharedLightCycleSystem.CalculateDayFactor(cycle, time);

        return dayFactor <= _cfg.GetCVar(NestationCVars.NightDayFactorThreshold);
    }

    /// <summary>Взвешенный случайный выбор прототипа (вес 0 или меньше = никогда).</summary>
    private EntProtoId PickWeighted(Dictionary<EntProtoId, float> weights)
    {
        var total = 0f;
        foreach (var weight in weights.Values)
        {
            total += MathF.Max(0f, weight);
        }

        var roll = _random.NextFloat() * total;
        EntProtoId last = default;

        foreach (var (id, weight) in weights)
        {
            var w = MathF.Max(0f, weight);
            if (w <= 0f)
                continue;

            last = id;
            roll -= w;
            if (roll <= 0f)
                return id;
        }

        return last;
    }

    private EntityCoordinates PickSpawnCoordinates(XenoNightSpawnerComponent comp, TransformComponent xform)
    {
        var origin = xform.Coordinates;

        for (var i = 0; i < 10; i++)
        {
            var offset = _random.NextAngle().ToVec() * comp.Radius * MathF.Sqrt(_random.NextFloat());
            var coords = origin.Offset(offset);

            var tile = _turf.GetTileRef(coords);
            if (tile == null || tile.Value.Tile.IsEmpty)
                continue;

            if (_turf.IsTileBlocked(tile.Value, CollisionGroup.MobMask))
                continue;

            return coords;
        }

        return origin;
    }
}
