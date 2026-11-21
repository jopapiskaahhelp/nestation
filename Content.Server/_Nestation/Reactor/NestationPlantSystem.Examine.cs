// Nestation CC BY-NC 3.0
using Content.Shared.Examine;
using Content.Shared._Nestation.Reactor;
using Robust.Shared.Localization;

namespace Content.Server._Nestation.Reactor;

public sealed partial class NestationPlantSystem
{
    private static string F0(float v) => v.ToString("0");
    private static string F1(float v) => v.ToString("0.0");

    private void InitializeExamine()
    {
        SubscribeLocalEvent<NestationReactorCoreComponent, ExaminedEvent>(OnCoreExamined);
        SubscribeLocalEvent<NestationPumpComponent, ExaminedEvent>(OnPumpExamined);
        SubscribeLocalEvent<NestationSteamGeneratorComponent, ExaminedEvent>(OnSgExamined);
        SubscribeLocalEvent<NestationTurbineComponent, ExaminedEvent>(OnTurbineExamined);
        SubscribeLocalEvent<NestationGeneratorComponent, ExaminedEvent>(OnGeneratorExamined);
        SubscribeLocalEvent<NestationCondenserComponent, ExaminedEvent>(OnCondenserExamined);
        SubscribeLocalEvent<NestationPoolComponent, ExaminedEvent>(OnPoolExamined);
        SubscribeLocalEvent<NestationFuelAssemblyComponent, ExaminedEvent>(OnFuelExamined);
    }

    private void OnCoreExamined(Entity<NestationReactorCoreComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var core = ent.Comp;
        args.PushMarkup(Loc.GetString("nestation-examine-core",
            ("power", F0(core.Power * 100f)),
            ("mw", F1(core.ThermalPower / 1_000_000f)),
            ("fuel", F0(core.FuelTemp)),
            ("coolant", F0(core.CoolantTemp)),
            ("rods", F0(core.RodInsertion * 100f))));

        if (core.Scrammed)
            args.PushMarkup(Loc.GetString("nestation-examine-scram"));
    }

    private void OnPumpExamined(Entity<NestationPumpComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("nestation-examine-pump",
            ("speed", F0(ent.Comp.Speed * 100f)),
            ("kw", F1(ent.Comp.PowerDraw / 1000f))));
    }

    private void OnSgExamined(Entity<NestationSteamGeneratorComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("nestation-examine-sg",
            ("temp", F0(ent.Comp.Temp)),
            ("pressure", F1(ent.Comp.Pressure)),
            ("level", F0(ent.Comp.Level * 100f))));
    }

    private void OnTurbineExamined(Entity<NestationTurbineComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("nestation-examine-turbine",
            ("rpm", F0(ent.Comp.Rpm * 3000f)),
            ("steam", F1(ent.Comp.SteamPower / 1_000_000f))));

        if (ent.Comp.Tripped)
            args.PushMarkup(Loc.GetString("nestation-examine-tripped"));
    }

    private void OnGeneratorExamined(Entity<NestationGeneratorComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("nestation-examine-generator",
            ("gross", F0(ent.Comp.Gross / 1000f)),
            ("own", F0(ent.Comp.Own / 1000f)),
            ("net", F0(ent.Comp.Net / 1000f)),
            ("breaker", Loc.GetString(ent.Comp.BreakerClosed ? "nestation-breaker-closed" : "nestation-breaker-open"))));
    }

    private void OnCondenserExamined(Entity<NestationCondenserComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("nestation-examine-condenser",
            ("temp", F0(ent.Comp.Temp)),
            ("heat", F0(ent.Comp.HeatIn / 1000f))));
    }

    private void OnPoolExamined(Entity<NestationPoolComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("nestation-examine-pool",
            ("temp", F0(ent.Comp.Temperature)),
            ("level", F0(ent.Comp.Level * 100f))));
    }

    private void OnFuelExamined(Entity<NestationFuelAssemblyComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("nestation-examine-fuel", ("burnup", F0(ent.Comp.Burnup * 100f))));
    }
}
