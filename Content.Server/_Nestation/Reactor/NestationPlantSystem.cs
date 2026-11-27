// Nestation CC BY-NC 3.0
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared._Nestation.Reactor;
using Content.Shared.Popups;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.Reactor;

public sealed partial class NestationPlantSystem : EntitySystem
{
    [Dependency] private NestationGraphSystem _graph = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const float Step = 0.5f;
    public const string FuelSlot = "fuel";

    private float _accumulator;
    private float _popupTimer;
    private readonly HashSet<EntityUid> _handled = new();

    public override void Initialize()
    {
        base.Initialize();
        InitializeExamine();
    }

    public override void Update(float frameTime)
    {
        ProcessEchoes();
        _accumulator += frameTime;

        if (_accumulator < Step)
            return;

        var dt = MathF.Min(_accumulator, 1.5f);
        _accumulator = 0f;
        _popupTimer += dt;
        _handled.Clear();

        var cores = new List<EntityUid>();
        var query = EntityQueryEnumerator<NestationReactorCoreComponent>();

        while (query.MoveNext(out var uid, out _))
        {
            cores.Add(uid);
        }

        foreach (var core in cores)
        {
            if (TerminatingOrDeleted(core))
                continue;

            StepPlant(core, dt);
        }

        StepIdleDevices(dt);
        StepPools(dt);

        if (_popupTimer >= 5f)
            _popupTimer = 0f;
    }

    public NestationPlantContext BuildContext(EntityUid core)
    {
        var c = new NestationPlantContext
        {
            CoreUid = core,
            Core = Comp<NestationReactorCoreComponent>(core),
        };

        var hot = _graph.Trace(core, NestationPortRole.ReactorHot);
        var cold = _graph.Trace(core, NestationPortRole.ReactorCold);

        c.SteamGeneratorUid = NestationGraphSystem.First(hot, NestationPortRole.SgPrimaryIn);
        c.PrimaryPumpUid = FindPump(cold, NestationPortRole.PumpOut, NestationPumpKind.Primary);

        if (c.SteamGeneratorUid is { } sg && c.PrimaryPumpUid is { } primary)
        {
            var back = _graph.Trace(sg, NestationPortRole.SgPrimaryOut);
            c.PrimaryClosed = back.Contains(new NestationLink(primary, NestationPortRole.PumpIn));

            var steam = _graph.Trace(sg, NestationPortRole.SgSteamOut);
            c.TurbineUid = NestationGraphSystem.First(steam, NestationPortRole.TurbineSteamIn);
        }
        else if (c.SteamGeneratorUid is { } lone)
        {
            var steam = _graph.Trace(lone, NestationPortRole.SgSteamOut);
            c.TurbineUid = NestationGraphSystem.First(steam, NestationPortRole.TurbineSteamIn);
        }

        if (c.TurbineUid is { } turbine)
        {
            var exhaust = _graph.Trace(turbine, NestationPortRole.TurbineExhaust);
            c.CondenserUid = NestationGraphSystem.First(exhaust, NestationPortRole.CondenserExhaustIn);

            var shaft = _graph.Trace(turbine, NestationPortRole.TurbineShaft);
            c.GeneratorUid = NestationGraphSystem.First(shaft, NestationPortRole.GeneratorShaft);
        }

        if (c.CondenserUid is { } cond)
        {
            var condensate = _graph.Trace(cond, NestationPortRole.CondenserCondensateOut);
            c.FeedPumpUid = FindPump(condensate, NestationPortRole.PumpIn, NestationPumpKind.Feed);

            if (c.FeedPumpUid is { } feed && c.SteamGeneratorUid is { } sgFeed)
            {
                var discharge = _graph.Trace(feed, NestationPortRole.PumpOut);
                c.SecondaryClosed = discharge.Contains(new NestationLink(sgFeed, NestationPortRole.SgFeedIn));
            }

            var coolingOut = _graph.Trace(cond, NestationPortRole.CondenserCoolingOut);
            c.PoolSeed = NestationGraphSystem.First(coolingOut, NestationPortRole.PoolPort);

            var coolingIn = _graph.Trace(cond, NestationPortRole.CondenserCoolingIn);
            c.CirculationPumpUid = FindPump(coolingIn, NestationPortRole.PumpOut, NestationPumpKind.Circulation);

            if (c.CirculationPumpUid is { } circ && c.PoolSeed != null)
            {
                var intake = _graph.Trace(circ, NestationPortRole.PumpIn);
                c.CoolingClosed = intake.Exists(l => l.Role == NestationPortRole.PoolPort);
            }
        }

        TryComp(c.SteamGeneratorUid, out c.Sg);
        TryComp(c.PrimaryPumpUid, out c.Primary);
        TryComp(c.FeedPumpUid, out c.Feed);
        TryComp(c.CirculationPumpUid, out c.Circulation);
        TryComp(c.TurbineUid, out c.Turbine);
        TryComp(c.GeneratorUid, out c.Gen);
        TryComp(c.CondenserUid, out c.Cond);

        if (c.PoolSeed is { } seed)
        {
            c.Pool = _graph.GetPoolGroup(seed);
            c.PoolTemp = PoolTemperature(c.Pool);
            c.PoolLevel = PoolLevel(c.Pool);
        }

        RefreshFlows(c);
        return c;
    }

    private static void RefreshFlows(NestationPlantContext c)
    {
        c.PhiPrimary = c.Primary != null && c.PrimaryClosed ? c.Primary.Speed : 0f;
        c.PhiFeed = c.Feed != null && c.SecondaryClosed ? c.Feed.Speed : 0f;
    }

    private EntityUid? FindPump(List<NestationLink> links, NestationPortRole role, NestationPumpKind kind)
    {
        foreach (var link in links)
        {
            if (link.Role != role)
                continue;

            if (TryComp(link.Device, out NestationPumpComponent? pump) && pump.Kind == kind)
                return link.Device;
        }

        return null;
    }

    private void StepPlant(EntityUid coreUid, float dt)
    {
        var c = BuildContext(coreUid);

        if (c.Core.Exploded)
            return;

        _handled.Add(coreUid);
        MarkHandled(c.SteamGeneratorUid);
        MarkHandled(c.PrimaryPumpUid);
        MarkHandled(c.FeedPumpUid);
        MarkHandled(c.CirculationPumpUid);
        MarkHandled(c.TurbineUid);
        MarkHandled(c.GeneratorUid);
        MarkHandled(c.CondenserUid);

        StepPumps(c, dt);
        RefreshFlows(c);

        var heatToSg = StepCore(c, dt);
        StepSteamGenerator(c, heatToSg, dt);
        StepTurbine(c, dt);
        StepGenerator(c, dt);
        StepCondenser(c, dt);
        EvaluateSafety(c, dt);
        EvaluateHazards(c, dt);

        if (!c.Core.Exploded)
            UpdateVisuals(c);
    }

    private void MarkHandled(EntityUid? uid)
    {
        if (uid != null)
            _handled.Add(uid.Value);
    }

    private void StepIdleDevices(float dt)
    {
        var pumps = EntityQueryEnumerator<NestationPumpComponent>();

        while (pumps.MoveNext(out var uid, out var pump))
        {
            if (_handled.Contains(uid) || TerminatingOrDeleted(uid))
                continue;

            StepPump(pump, 0f, 0f, dt);
            _appearance.SetData(uid, NestationVisuals.State, pump.Speed > 0.05f ? NestationVisualState.Running : NestationVisualState.Off);
        }

        var turbines = EntityQueryEnumerator<NestationTurbineComponent>();

        while (turbines.MoveNext(out var uid, out var turbine))
        {
            if (_handled.Contains(uid) || TerminatingOrDeleted(uid))
                continue;

            turbine.Rpm = MathF.Max(0f, turbine.Rpm - 0.05f * dt);
            turbine.Gross = 0f;
            _appearance.SetData(uid, NestationVisuals.State, turbine.Rpm > 0.05f ? NestationVisualState.Running : NestationVisualState.Off);
        }

        var generators = EntityQueryEnumerator<NestationGeneratorComponent>();

        while (generators.MoveNext(out var uid, out var gen))
        {
            if (_handled.Contains(uid) || TerminatingOrDeleted(uid))
                continue;

            gen.Gross = 0f;
            gen.Net = 0f;
            SetSupply(uid, 0f);
            _appearance.SetData(uid, NestationVisuals.State, NestationVisualState.Off);
        }

        var conds = EntityQueryEnumerator<NestationCondenserComponent>();

        while (conds.MoveNext(out var uid, out var cond))
        {
            if (_handled.Contains(uid) || TerminatingOrDeleted(uid))
                continue;

            cond.Temp = MathF.Max(NestationTuning.Ambient, cond.Temp - 0.1f * dt);
            _appearance.SetData(uid, NestationVisuals.State, NestationVisualState.Off);
        }

        var sgs = EntityQueryEnumerator<NestationSteamGeneratorComponent>();

        while (sgs.MoveNext(out var uid, out var sg))
        {
            if (_handled.Contains(uid) || TerminatingOrDeleted(uid))
                continue;

            sg.Temp = MathF.Max(NestationTuning.Ambient, sg.Temp - 0.05f * dt);
            sg.Pressure = NestationTuning.SaturationPressure(sg.Temp);
            _appearance.SetData(uid, NestationVisuals.State, NestationVisualState.Off);
        }
    }
}
