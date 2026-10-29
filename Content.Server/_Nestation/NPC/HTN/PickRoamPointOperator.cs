// Nestation new city CC BY-NC 3.0
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Nestation.XenoBrain;
using Content.Shared._Nestation.XenoHive;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Maths;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.NPC.HTN;

public sealed partial class PickRoamPointOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    private SharedTransformSystem _transform = default!;
    private XenoBrainSystem _brainSys = default!;

    [DataField(required: true)]
    public string TargetKey = string.Empty;

    [DataField]
    public float MinRange = 20f;

    [DataField]
    public float MaxRange = 50f;

    [DataField]
    public float CautiousMinRange = 8f;

    [DataField]
    public float CautiousMaxRange = 18f;

    [DataField]
    public List<string> Modes = new();

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
        _brainSys = sysManager.GetEntitySystem<XenoBrainSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var xform = _entManager.GetComponent<TransformComponent>(owner);
        var pos = _transform.GetWorldPosition(xform);

        _entManager.TryGetComponent(owner, out XenoBrainComponent? brain);
        if (brain != null && !XenoOps.ModeOk(Modes, brain))
            return XenoOps.No();

        var cautious = brain != null && _timing.CurTime < brain.CautiousUntil;
        var min = cautious ? CautiousMinRange : MinRange;
        var max = cautious ? CautiousMaxRange : MaxRange;

        var stage = brain != null ? _brainSys.HiveStage(xform.MapID) : 0;
        var avoidHive = brain != null && brain.Caste == XenoCaste.Drone && stage == 1;
        var hivePos = avoidHive ? _brainSys.HivePosition(xform.MapID) : null;
        var map = xform.MapID;

        Vector2? Choose(float minClear, bool filters)
        {
            var baseAngle = _random.NextAngle();
            var total = 0f;
            var fan = new List<(Vector2 Offset, float Weight)>();

            for (var k = 0; k < 16; k++)
            {
                var dir = (baseAngle + Angle.FromDegrees(22.5 * k)).ToVec();
                var clear = _brainSys.ClearDistance(map, pos, dir, max, owner);
                if (clear < minClear)
                    continue;

                var travel = MathF.Min(clear - 0.8f, _random.NextFloat(min, max));
                if (travel < minClear - 0.8f)
                    continue;

                var offset = dir * travel;
                var cand = pos + offset;
                var weight = clear;

                if (brain != null)
                {
                    if (filters && (_brainSys.IsDangerous(map, cand) || XenoOps.NearAvoid(brain, cand, 4f)))
                        continue;

                    if (_brainSys.IsRecentRoam(owner, cand, 4f))
                        continue;

                    if (brain.LastRoamDir != Vector2.Zero)
                    {
                        var dot = Vector2.Dot(dir, brain.LastRoamDir);
                        weight *= dot < -0.5f ? 0.02f : 0.4f + 0.6f * MathF.Max(0f, dot);
                    }

                    if (hivePos is { } hiveAt)
                    {
                        if ((cand - hiveAt).Length() < brain.DroneHiveAvoid)
                            continue;

                        var away = pos - hiveAt;
                        if (away.LengthSquared() > 0.01f)
                            weight *= 0.5f + 1.5f * MathF.Max(0f, Vector2.Dot(dir, Vector2.Normalize(away)));
                    }
                }

                fan.Add((offset, weight));
                total += weight;
            }

            if (fan.Count == 0)
                return null;

            var roll = _random.NextFloat() * total;
            foreach (var (offset, weight) in fan)
            {
                roll -= weight;
                if (roll <= 0f)
                    return offset;
            }

            return fan[^1].Offset;
        }

        Vector2? picked = Choose(6f, true);

        if (picked == null && brain != null && _brainSys.TryBaseRoam(owner, map, pos, out var baseCoords))
        {
            brain.LastRoamDir = Vector2.Zero;
            _brainSys.RememberRoam(owner, pos);
            return XenoOps.Yes(new Dictionary<string, object>
            {
                [TargetKey] = baseCoords,
            });
        }

        picked ??= Choose(3f, true);
        picked ??= Choose(2.5f, false);

        if (picked is not { } chosen)
        {
            _brainSys.ForceUnstick(owner);
            return XenoOps.No();
        }

        if (brain != null)
            brain.LastRoamDir = Vector2.Normalize(chosen);

        _brainSys.RememberRoam(owner, pos + chosen);

        return XenoOps.Yes(new Dictionary<string, object>
        {
            [TargetKey] = xform.Coordinates.Offset(chosen),
        });
    }
}
