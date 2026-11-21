// Nestation CC BY-NC 3.0
using Content.Shared._Nestation.Reactor;
using Robust.Shared.GameObjects;

namespace Content.Server._Nestation.Reactor;

public sealed class NestationConsoleSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly NestationGraphSystem _graph = default!;
    [Dependency] private readonly NestationPlantSystem _plant = default!;

    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<NestationConsoleComponent>(NestationConsoleUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<NestationConsoleMessage>(OnMessage);
        });
    }

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;

        if (_accumulator < 1f)
            return;

        _accumulator = 0f;
        var query = EntityQueryEnumerator<NestationConsoleComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            UpdateConsole(uid, comp);
        }
    }

    private void OnOpened(EntityUid uid, NestationConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateConsole(uid, comp);
    }

    private EntityUid? FindCore(EntityUid console)
    {
        foreach (var link in _graph.TraceControl(console))
        {
            if (HasComp<NestationReactorCoreComponent>(link.Device))
                return link.Device;
        }

        return null;
    }

    private void UpdateConsole(EntityUid uid, NestationConsoleComponent comp)
    {
        var core = FindCore(uid);

        if (core == null || TerminatingOrDeleted(core.Value))
        {
            _appearance.SetData(uid, NestationVisuals.State, NestationVisualState.Off);

            if (_ui.IsUiOpen(uid, NestationConsoleUiKey.Key))
                _ui.SetUiState(uid, NestationConsoleUiKey.Key, new NestationConsoleState { Linked = false });

            return;
        }

        var ctx = _plant.BuildContext(core.Value);
        var alarm = (ctx.Core.Warnings & (NestationWarning.MeltImminent | NestationWarning.Scram | NestationWarning.Hydrogen | NestationWarning.HighPressure | NestationWarning.FuelOverheat)) != 0;
        _appearance.SetData(uid, NestationVisuals.State, alarm ? NestationVisualState.Alarm : NestationVisualState.Running);

        if (_ui.IsUiOpen(uid, NestationConsoleUiKey.Key))
            _ui.SetUiState(uid, NestationConsoleUiKey.Key, BuildState(ctx));
    }

    private void OnMessage(EntityUid uid, NestationConsoleComponent comp, NestationConsoleMessage msg)
    {
        var coreUid = FindCore(uid);

        if (coreUid == null)
            return;

        var c = _plant.BuildContext(coreUid.Value);
        var core = c.Core;

        if (core.ControlLost)
            return;

        switch (msg.Command)
        {
            case NestationCommand.Scram:
                core.Scrammed = true;
                core.AutoMode = false;
                core.RodTarget = 1f;
                break;

            case NestationCommand.ResetScram:
                if (core.Scrammed && core.Power < 0.02f && !_plant.ScramConditions(c))
                    core.Scrammed = false;
                break;

            case NestationCommand.ToggleAuto:
                if (core.Scrammed)
                    break;

                core.AutoMode = !core.AutoMode;
                core.RodTarget = core.RodInsertion;
                break;

            case NestationCommand.SetpointDelta:
                core.Setpoint = Math.Clamp(core.Setpoint + msg.Amount, 0f, 1f);
                break;

            case NestationCommand.RodsDelta:
                if (!core.AutoMode && !core.Scrammed)
                    core.RodTarget = Math.Clamp(core.RodTarget + msg.Amount, 0f, 1f);
                break;

            case NestationCommand.PumpDelta:
                var pump = PumpOf(c, msg.Pump);

                if (pump != null)
                {
                    pump.Setpoint = Math.Clamp(pump.Setpoint + msg.Amount, 0f, 1.1f);

                    if (pump.Kind != NestationPumpKind.Primary)
                        pump.Auto = false;
                }
                break;

            case NestationCommand.PumpAuto:
                var auto = PumpOf(c, msg.Pump);

                if (auto != null && auto.Kind != NestationPumpKind.Primary)
                    auto.Auto = true;
                break;

            case NestationCommand.ToggleBreaker:
                if (c.Gen != null)
                    c.Gen.BreakerClosed = !c.Gen.BreakerClosed;
                break;

            case NestationCommand.ResetTurbine:
                if (c.Turbine != null && c.Turbine.Tripped && !_plant.TurbineTripConditions(c))
                    c.Turbine.Tripped = false;
                break;

            case NestationCommand.ToggleBypass:
                core.ProtectionsBypassed = !core.ProtectionsBypassed;
                break;
        }

        UpdateConsole(uid, comp);
    }

    private static NestationPumpComponent? PumpOf(NestationPlantContext c, NestationPumpKind kind)
    {
        return kind switch
        {
            NestationPumpKind.Primary => c.Primary,
            NestationPumpKind.Feed => c.Feed,
            _ => c.Circulation,
        };
    }

    private NestationConsoleState BuildState(NestationPlantContext c)
    {
        var core = c.Core;
        var fuel = _plant.GetFuelFor(c.CoreUid);

        return new NestationConsoleState
        {
            Linked = true,
            Scrammed = core.Scrammed,
            AutoMode = core.AutoMode,
            Bypassed = core.ProtectionsBypassed,
            HasFuel = fuel != null,
            BreakerClosed = c.Gen?.BreakerClosed ?? false,
            TurbineTripped = c.Turbine?.Tripped ?? false,
            HasGenerator = c.Gen != null,
            Setpoint = core.Setpoint,
            Rods = core.RodInsertion,
            Neutron = core.Power,
            ThermalPower = core.ThermalPower,
            FuelTemp = core.FuelTemp,
            CoolantTemp = core.CoolantTemp,
            SteamTemp = c.Sg?.Temp ?? 0f,
            SteamPressure = c.Sg?.Pressure ?? 0f,
            SgLevel = c.Sg?.Level ?? 0f,
            PrimaryFlow = c.PhiPrimary,
            FeedFlow = c.PhiFeed,
            CirculationFlow = c.Circulation != null && c.CoolingClosed ? c.Circulation.Speed : 0f,
            PrimarySetpoint = c.Primary?.Setpoint ?? 0f,
            FeedSetpoint = c.Feed?.Setpoint ?? 0f,
            CirculationSetpoint = c.Circulation?.Setpoint ?? 0f,
            FeedAuto = c.Feed?.Auto ?? false,
            CirculationAuto = c.Circulation?.Auto ?? false,
            Rpm = c.Turbine?.Rpm ?? 0f,
            Gross = c.Gen?.Gross ?? 0f,
            Net = c.Gen?.Net ?? 0f,
            Own = NestationPlantSystem.OwnConsumption(c),
            Efficiency = c.Turbine?.Efficiency ?? 0f,
            CondenserTemp = c.Cond?.Temp ?? 0f,
            HeatToPool = c.CoolingClosed ? (c.Cond?.HeatOut ?? 0f) + NestationPlantSystem.OwnConsumption(c) : 0f,
            PoolTemp = c.PoolTemp,
            PoolLevel = c.PoolLevel,
            PoolSegments = c.Pool.Count,
            ReserveMj = (c.Gen?.Reserve ?? 0f) / 1_000_000f,
            Burnup = fuel?.Burnup ?? 0f,
            Hydrogen = NestationPhysics.HydrogenConcentration(core.Hydrogen, core.HallVolume) * 100f,
            Inventory = core.InventoryNominal > 0f ? core.Inventory / core.InventoryNominal : 0f,
            DryoutSeconds = core.DryoutSeconds,
            DecayKw = core.DecayPower / 1000f,
            Integrity = core.Integrity,
            PotentialTnt = core.PotentialTnt,
            SafeRadius = NestationPhysics.SafeRadius(core.PotentialTnt),
            Countdown = core.Countdown,
            Warnings = core.Warnings,
        };
    }
}
