// Nestation Anarhey CC BY-NC 3.0
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Nestation.XenoBrain;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.NPC.HTN;

/// <summary>
/// Случайная точка для патруля. Обходит места, где ксеносов ранили, и не ведёт в старые "застревания".
/// В осторожном режиме (после засады) прыжки короче.
/// </summary>
public sealed partial class PickRoamPointOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
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

    /// <summary>В каких режимах мозга оператор активен. Пусто = всегда.</summary>
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

        for (var i = 0; i < 6; i++)
        {
            var offset = _random.NextAngle().ToVec() * _random.NextFloat(min, max);
            var cand = pos + offset;

            if (_brainSys.IsDangerous(xform.MapID, cand))
                continue;

            if (brain != null && XenoOps.NearAvoid(brain, cand, 4f))
                continue;

            return XenoOps.Yes(new Dictionary<string, object>
            {
                [TargetKey] = xform.Coordinates.Offset(offset),
            });
        }

        return XenoOps.No();
    }
}
