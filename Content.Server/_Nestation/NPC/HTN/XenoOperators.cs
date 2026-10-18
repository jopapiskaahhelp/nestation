// Nestation Anarhey CC BY-NC 3.0
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Nestation.XenoBrain;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.NPC.HTN;

internal static class XenoOps
{
    public static bool ModeOk(List<string> modes, XenoBrainComponent b)
    {
        return modes.Count == 0 || modes.Contains(b.Mode.ToString());
    }

    public static Vector2 Rotate(Vector2 v, float radians)
    {
        var c = MathF.Cos(radians);
        var s = MathF.Sin(radians);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    public static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared();
        if (len2 < 0.0001f)
            return (p - a).Length();

        var t = Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return (p - (a + ab * t)).Length();
    }

    public static bool NearAvoid(XenoBrainComponent b, Vector2 p, float radius)
    {
        foreach (var (pos, _) in b.AvoidPoints)
        {
            if ((pos - p).Length() < radius)
                return true;
        }

        return false;
    }

    public static Task<(bool Valid, Dictionary<string, object>? Effects)> No()
    {
        return Task.FromResult<(bool Valid, Dictionary<string, object>? Effects)>((false, null));
    }

    public static Task<(bool Valid, Dictionary<string, object>? Effects)> Yes(Dictionary<string, object>? fx = null)
    {
        return Task.FromResult<(bool Valid, Dictionary<string, object>? Effects)>((true, fx));
    }
}

public sealed partial class PickBrainTargetOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    [DataField(required: true)] public string TargetKey = string.Empty;
    [DataField(required: true)] public string TargetCoordinatesKey = string.Empty;
    [DataField] public List<string> Modes = new();

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entMan.TryGetComponent(owner, out XenoBrainComponent? brain) || !XenoOps.ModeOk(Modes, brain))
            return XenoOps.No();

        if (brain.Target is not { } target || !_entMan.EntityExists(target))
            return XenoOps.No();

        return XenoOps.Yes(new Dictionary<string, object>
        {
            [TargetKey] = target,
            [TargetCoordinatesKey] = new EntityCoordinates(target, Vector2.Zero),
        });
    }
}

public sealed partial class PickDetourPointOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private SharedTransformSystem _transform = default!;
    private XenoBrainSystem _brainSys = default!;

    [DataField(required: true)] public string TargetKey = string.Empty;
    [DataField] public int Rank;
    [DataField] public List<string> Modes = new();

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
        _brainSys = sysManager.GetEntitySystem<XenoBrainSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entMan.TryGetComponent(owner, out XenoBrainComponent? brain) || !XenoOps.ModeOk(Modes, brain))
            return XenoOps.No();

        var xform = _entMan.GetComponent<TransformComponent>(owner);
        var pos = _transform.GetWorldPosition(xform);
        var stuck = brain.StuckPoint ?? pos;
        var forward = _brainSys.ForwardDir(owner, brain, pos);

        var cands = new List<Vector2>();

        if (brain.GoalPos is { } goalHint
            && _brainSys.TryGetPassageRoute(xform.MapID, pos, goalHint, _timing.CurTime, out _, out var knownExit))
            cands.Add(knownExit);

        var gaps = new List<Vector2>();
        _brainSys.FindGaps(xform.MapID, pos, forward, 14f, owner, gaps);
        foreach (var g in gaps)
        {
            if ((g - stuck).Length() < 1.5f || XenoOps.NearAvoid(brain, g, 2f))
                continue;

            cands.Add(g);
        }

        var rng = new System.Random(unchecked(owner.Id * 7919 + brain.DetourCount * 31 + 17));
        for (var i = 0; i < 4; i++)
        {
            var side = i % 2 == 0 ? 1f : -1f;
            var ang = side * (0.95f + (float) rng.NextDouble() * 1.25f);
            var via = pos + XenoOps.Rotate(forward, ang) * (5f + (float) rng.NextDouble() * 6f);

            if (XenoOps.NearAvoid(brain, via, 3.5f))
                continue;

            if (brain.GoalPos is { } goal && XenoOps.DistToSegment(stuck, via, goal) < 2.5f)
                continue;

            cands.Add(via);
        }

        if (Rank >= cands.Count)
            return XenoOps.No();

        var pick = cands[Rank];
        brain.DetourPoint = pick;
        return XenoOps.Yes(new Dictionary<string, object>
        {
            [TargetKey] = xform.Coordinates.Offset(pick - pos),
        });
    }
}

public sealed partial class PickCoverSpotOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    private SharedTransformSystem _transform = default!;
    private XenoBrainSystem _brainSys = default!;

    [DataField(required: true)] public string TargetKey = string.Empty;
    [DataField] public int Rank;
    [DataField] public bool Flee;
    [DataField] public List<string> Modes = new();

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
        _brainSys = sysManager.GetEntitySystem<XenoBrainSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entMan.TryGetComponent(owner, out XenoBrainComponent? brain) || !XenoOps.ModeOk(Modes, brain))
            return XenoOps.No();

        var xform = _entMan.GetComponent<TransformComponent>(owner);
        var map = xform.MapID;
        var pos = _transform.GetWorldPosition(xform);
        var threat = brain.ThreatPos;

        var toThreat = threat - pos;
        var away = toThreat.LengthSquared() < 0.01f ? _random.NextAngle().ToVec() : -Vector2.Normalize(toThreat);

        var list = new List<(float Score, Vector2 Pos)>();

        if (!Flee)
        {
            for (var i = 0; i < 16; i++)
            {
                var a = i * (MathF.PI * 2f / 16f);
                var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));

                if (!_brainSys.RayHit(map, threat, dir, 18f, XenoBrainSystem.OpaqueMask, owner,
                        out var d, out _, out _))
                    continue;

                if (d < 3f || d > 16f)
                    continue;

                var cand = threat + dir * (d + 1.6f);
                var delta = cand - pos;
                var dist = delta.Length();

                if (dist < 1.5f || dist > 20f || (cand - threat).Length() < 5f
                    || _brainSys.IsDangerous(map, cand))
                    continue;

                var toCand = delta / dist;

                if (toThreat.LengthSquared() > 0.01f && Vector2.Dot(toCand, Vector2.Normalize(toThreat)) > 0.6f)
                    continue;

                var score = 100f - dist;

                if (!_brainSys.RayHit(map, pos, toCand, dist, XenoBrainSystem.BlockMask, owner,
                        out _, out _, out _))
                    score += 30f;

                list.Add((score, cand));
            }
        }
        else
        {
            for (var i = 0; i < 6; i++)
            {
                var dir = XenoOps.Rotate(away, _random.NextFloat(-0.6f, 0.6f));
                var cand = pos + dir * _random.NextFloat(10f, 16f);
                if (_brainSys.IsDangerous(map, cand))
                    continue;

                list.Add(((cand - threat).Length(), cand));
            }
        }

        list.Sort((l, r) => r.Score.CompareTo(l.Score));

        if (Rank >= list.Count)
            return XenoOps.No();

        var spot = list[Rank].Pos;
        brain.RetreatSpot = spot;
        return XenoOps.Yes(new Dictionary<string, object>
        {
            [TargetKey] = xform.Coordinates.Offset(spot - pos),
        });
    }
}

public sealed partial class PickRallyPointOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    private SharedTransformSystem _transform = default!;

    [DataField(required: true)] public string TargetKey = string.Empty;
    [DataField(required: true)] public string RangeKey = string.Empty;
    [DataField] public float Range = 4.5f;
    [DataField] public List<string> Modes = new();

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entMan.TryGetComponent(owner, out XenoBrainComponent? brain) || !XenoOps.ModeOk(Modes, brain))
            return XenoOps.No();

        if (brain.RallyTarget is not { } ally || !_entMan.EntityExists(ally))
            return XenoOps.No();

        var xform = _entMan.GetComponent<TransformComponent>(owner);
        var dist = (_transform.GetWorldPosition(ally) - _transform.GetWorldPosition(xform)).Length();
        if (dist < Range + 1f)
            return XenoOps.No();

        return XenoOps.Yes(new Dictionary<string, object>
        {
            [TargetKey] = new EntityCoordinates(ally, Vector2.Zero),
            [RangeKey] = Range,
        });
    }
}

public sealed partial class PickFlankPointOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    private SharedTransformSystem _transform = default!;
    private XenoBrainSystem _brainSys = default!;

    [DataField(required: true)] public string TargetKey = string.Empty;
    [DataField] public List<string> Modes = new();

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
        _brainSys = sysManager.GetEntitySystem<XenoBrainSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entMan.TryGetComponent(owner, out XenoBrainComponent? brain) || !XenoOps.ModeOk(Modes, brain))
            return XenoOps.No();

        if (brain.Target is not { } target || !_entMan.EntityExists(target))
            return XenoOps.No();

        var xform = _entMan.GetComponent<TransformComponent>(owner);
        var pos = _transform.GetWorldPosition(xform);
        var tPos = _transform.GetWorldPosition(target);

        var away = pos - tPos;
        away = away.LengthSquared() < 0.01f ? _random.NextAngle().ToVec() : Vector2.Normalize(away);

        for (var i = 0; i < 8; i++)
        {
            var dir = XenoOps.Rotate(away, brain.FlankSide * _random.NextFloat(1.05f, 1.75f));
            var cand = tPos + dir * _random.NextFloat(8f, 11f);

            if (_brainSys.IsDangerous(xform.MapID, cand))
                continue;

            return XenoOps.Yes(new Dictionary<string, object>
            {
                [TargetKey] = xform.Coordinates.Offset(cand - pos),
            });
        }

        return XenoOps.No();
    }
}

public sealed partial class XenoHoldOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    [DataField] public List<string> Modes = new();

    [DataField] public float MaxSeconds;

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entMan.TryGetComponent(owner, out XenoBrainComponent? brain) || !XenoOps.ModeOk(Modes, brain))
            return XenoOps.No();

        return XenoOps.Yes();
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (_entMan.TryGetComponent(owner, out XenoBrainComponent? brain))
            brain.HoldStart = _timing.CurTime;
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entMan.TryGetComponent(owner, out XenoBrainComponent? brain) || !XenoOps.ModeOk(Modes, brain))
            return HTNOperatorStatus.Finished;

        if (MaxSeconds > 0f && (_timing.CurTime - brain.HoldStart).TotalSeconds >= MaxSeconds)
            return HTNOperatorStatus.Finished;

        return HTNOperatorStatus.Continuing;
    }
}

public sealed partial class PickKitePointOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    private SharedTransformSystem _transform = default!;
    private XenoBrainSystem _brainSys = default!;

    [DataField(required: true)] public string TargetKey = string.Empty;
    [DataField] public float MinDist = 8.5f;
    [DataField] public float MaxDist = 14f;
    [DataField] public List<string> Modes = new();

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
        _brainSys = sysManager.GetEntitySystem<XenoBrainSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entMan.TryGetComponent(owner, out XenoBrainComponent? brain) || !XenoOps.ModeOk(Modes, brain))
            return XenoOps.No();

        if (brain.Target is not { } target || !_entMan.EntityExists(target))
            return XenoOps.No();

        var xform = _entMan.GetComponent<TransformComponent>(owner);
        var pos = _transform.GetWorldPosition(xform);
        var tPos = _transform.GetWorldPosition(target);

        var dist = (pos - tPos).Length();

        if (brain.TargetRanged)
        {
            var toMe = pos - tPos;
            var toMeLen = toMe.Length();
            if (toMeLen > 2f && _brainSys.RayHit(xform.MapID, tPos, toMe / toMeLen, toMeLen,
                    XenoBrainSystem.OpaqueMask, owner, out _, out _, out _))
                return XenoOps.No();

            Vector2? coverSpot = null;
            var bestCoverDist = float.MaxValue;
            for (var k = 0; k < 16; k++)
            {
                var ca = k * (MathF.PI * 2f / 16f);
                var cdir = new Vector2(MathF.Cos(ca), MathF.Sin(ca));
                if (!_brainSys.RayHit(xform.MapID, tPos, cdir, 16f, XenoBrainSystem.OpaqueMask, owner,
                        out var hitDist, out _, out _) || hitDist < 4f)
                    continue;

                var spot = tPos + cdir * (hitDist + 1.6f);
                var spotDist = (spot - pos).Length();
                if (spotDist > 20f || spotDist >= bestCoverDist || _brainSys.IsDangerous(xform.MapID, spot))
                    continue;

                bestCoverDist = spotDist;
                coverSpot = spot;
            }

            if (coverSpot is { } cover)
            {
                return XenoOps.Yes(new Dictionary<string, object>
                {
                    [TargetKey] = xform.Coordinates.Offset(cover - pos),
                });
            }
        }

        if (dist >= MinDist && dist <= MaxDist)
            return XenoOps.No();

        var away = pos - tPos;
        away = away.LengthSquared() < 0.01f ? _random.NextAngle().ToVec() : Vector2.Normalize(away);

        for (var i = 0; i < 8; i++)
        {
            var dir = XenoOps.Rotate(away, _random.NextFloat(-0.9f, 0.9f));
            var cand = tPos + dir * _random.NextFloat(MinDist + 0.5f, MaxDist - 0.5f);

            if (_brainSys.IsDangerous(xform.MapID, cand))
                continue;

            return XenoOps.Yes(new Dictionary<string, object>
            {
                [TargetKey] = xform.Coordinates.Offset(cand - pos),
            });
        }

        return XenoOps.No();
    }
}

public sealed partial class XenoModePrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    [DataField] public List<string> Modes = new();

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        return _entMan.TryGetComponent(owner, out XenoBrainComponent? brain) && XenoOps.ModeOk(Modes, brain);
    }
}

public sealed partial class XenoStrikeOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private SharedTransformSystem _transform = default!;
    private NPCSteeringSystem _steering = default!;
    private PathfindingSystem _pathfind = default!;
    private XenoBrainSystem _brainSys = default!;

    [DataField] public List<string> Modes = new();
    [DataField] public float OrbitRadius = 1.1f;
    [DataField] public float LeadSeconds = 0.5f;
    [DataField] public float FanRadius = 6f;
    [DataField] public float FanSwitchDist = 8f;
    [DataField] public float SteerInterval = 0.12f;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
        _steering = sysManager.GetEntitySystem<NPCSteeringSystem>();
        _pathfind = sysManager.GetEntitySystem<PathfindingSystem>();
        _brainSys = sysManager.GetEntitySystem<XenoBrainSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entMan.TryGetComponent(owner, out XenoBrainComponent? brain) || !XenoOps.ModeOk(Modes, brain))
            return XenoOps.No();

        if (brain.Target is not { } target || !_entMan.EntityExists(target))
            return XenoOps.No();

        return XenoOps.Yes();
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entMan.TryGetComponent(owner, out XenoBrainComponent? brain))
            return;

        var melee = _entMan.EnsureComponent<NPCMeleeCombatComponent>(owner);
        if (brain.Target is { } target)
            melee.Target = target;

        brain.NextSteer = TimeSpan.Zero;
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entMan.TryGetComponent(owner, out XenoBrainComponent? brain) || !XenoOps.ModeOk(Modes, brain))
            return HTNOperatorStatus.Finished;

        if (brain.Target is not { } target || !_entMan.EntityExists(target))
            return HTNOperatorStatus.Finished;

        if (_entMan.TryGetComponent(owner, out NPCMeleeCombatComponent? melee))
            melee.Target = target;

        var now = _timing.CurTime;
        if (now < brain.NextSteer)
            return HTNOperatorStatus.Continuing;

        brain.NextSteer = now + TimeSpan.FromSeconds(SteerInterval);

        var xform = _entMan.GetComponent<TransformComponent>(owner);
        var me = _transform.GetWorldPosition(xform);
        var targetPos = _transform.GetWorldPosition(target);

        var vel = Vector2.Zero;
        if (_entMan.TryGetComponent(target, out PhysicsComponent? phys))
            vel = phys.LinearVelocity;

        var goal = _brainSys.StrikeGoal(brain, me, targetPos, vel, OrbitRadius, LeadSeconds, FanRadius, FanSwitchDist);
        var steerTo = goal;
        if (_brainSys.GetBypass(owner, brain, xform.MapID, me, goal, now) is { } bypass)
            steerTo = bypass;

        var steer = _steering.Register(owner, xform.Coordinates.Offset(steerTo - me));
        steer.Range = 0.3f;
        steer.Flags = _pathfind.GetFlags(blackboard);

        return HTNOperatorStatus.Continuing;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        Cleanup(blackboard);
    }

    public override void PlanShutdown(NPCBlackboard blackboard)
    {
        base.PlanShutdown(blackboard);
        Cleanup(blackboard);
    }

    private void Cleanup(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entMan.EntityExists(owner))
            return;

        if (_entMan.HasComponent<NPCMeleeCombatComponent>(owner))
            _entMan.RemoveComponent<NPCMeleeCombatComponent>(owner);

        _steering.Unregister(owner);
    }
}

public sealed partial class XenoTargetDistPrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    [DataField] public float Min;
    [DataField] public float Max = float.MaxValue;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entMan.TryGetComponent(owner, out XenoBrainComponent? brain))
            return false;

        return brain.TargetDist >= Min && brain.TargetDist <= Max;
    }
}
