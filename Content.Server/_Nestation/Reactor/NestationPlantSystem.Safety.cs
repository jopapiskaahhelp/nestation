// Nestation CC BY-NC 3.0
using Content.Shared._Nestation.Reactor;
using Content.Shared.Popups;
using Robust.Shared.Localization;

namespace Content.Server._Nestation.Reactor;

public sealed partial class NestationPlantSystem
{
    public bool ScramConditions(NestationPlantContext c)
    {
        var core = c.Core;
        var running = core.Power > 0.05f;

        if (core.FuelTemp > NestationTuning.ScramFuelTemp ||
            core.Power > NestationTuning.ScramPower ||
            core.CoolantTemp > NestationTuning.ScramCoolantTemp)
        {
            return true;
        }

        if (running && c.PhiPrimary < NestationTuning.ScramFlow)
            return true;

        if (c.Sg != null && (c.Sg.Pressure > NestationTuning.ScramPressure || (running && c.Sg.Level < NestationTuning.ScramSgLevel)))
            return true;

        if (c.Cond != null && c.Cond.Temp > NestationTuning.ScramCondenserTemp)
            return true;

        if (running && core.InventoryNominal > 0f && core.Inventory / core.InventoryNominal < 0.5f)
            return true;

        return running && c.Turbine != null && c.Turbine.Tripped && core.Power > 0.3f;
    }

    public bool TurbineTripConditions(NestationPlantContext c)
    {
        if (c.Turbine == null)
            return false;

        return c.Turbine.Rpm > NestationTuning.TripRpm ||
               (c.Cond != null && c.Cond.Temp > NestationTuning.ScramCondenserTemp) ||
               (c.Sg != null && c.Sg.Level > NestationTuning.TripSgLevel);
    }

    private void EvaluateSafety(NestationPlantContext c, float dt)
    {
        var core = c.Core;
        var running = core.Power > 0.05f;
        var w = NestationWarning.None;

        if (core.FuelTemp > 750f)
            w |= NestationWarning.FuelOverheat;

        if (core.Power > 1.08f)
            w |= NestationWarning.HighPower;

        if (running && c.PhiPrimary < 0.5f)
            w |= NestationWarning.LowFlow;

        if (c.Sg != null)
        {
            if (c.Sg.Pressure > 55f)
                w |= NestationWarning.HighPressure;

            if (running && c.Sg.Level < 0.25f)
                w |= NestationWarning.LowSgLevel;

            if (c.Sg.Level > 0.85f)
                w |= NestationWarning.HighSgLevel;

            if (c.Sg.ReliefPower > 0f)
                w |= NestationWarning.ReliefOpen;
        }

        if (core.CoolantTemp > 320f)
            w |= NestationWarning.HighCoolantTemp;

        if ((c.Cond != null && c.Cond.Temp > 100f) || (running && !c.CoolingClosed))
            w |= NestationWarning.HeatSinkLoss;

        if (c.Turbine != null)
        {
            if (c.Turbine.Rpm > 1.08f)
                w |= NestationWarning.TurbineOverspeed;

            if (c.Turbine.Tripped)
                w |= NestationWarning.TurbineTrip;
        }

        var concentration = NestationPhysics.HydrogenConcentration(core.Hydrogen, core.HallVolume);

        if (concentration > 0.01f)
            w |= NestationWarning.Hydrogen;

        if (core.InventoryNominal > 0f && core.Inventory / core.InventoryNominal < 0.4f)
            w |= NestationWarning.CoreUncovered;

        if (core.HydrogenRate > 0f)
            w |= NestationWarning.ZirconiumReaction;

        if (core.Breach > 0.01f)
            w |= NestationWarning.VesselDamaged;

        if (core.ControlLost)
            w |= NestationWarning.ControlLost;

        if (core.RodsJammed)
            w |= NestationWarning.RodsJammed;

        if (c.Pool.Count > 0)
        {
            if (c.PoolTemp > 70f)
                w |= NestationWarning.PoolHot;

            if (c.PoolLevel < 0.35f)
                w |= NestationWarning.PoolLow;
        }

        if (!c.PrimaryClosed)
            w |= NestationWarning.PrimaryOpen;

        if (!c.SecondaryClosed)
            w |= NestationWarning.SecondaryOpen;

        if (!c.CoolingClosed)
            w |= NestationWarning.CoolingOpen;

        if (GetFuel(c.CoreUid) == null)
            w |= NestationWarning.NoFuel;

        if (c.Gen != null && c.Gen.Reserve < 5_000_000f && c.Gen.Gross < c.Gen.Own)
            w |= NestationWarning.PowerLoss;

        if (core.ProtectionsBypassed)
            w |= NestationWarning.ProtectionsBypassed;

        if (core.Scrammed)
            w |= NestationWarning.Scram;

        core.Warnings = w;

        if (!core.ProtectionsBypassed && !core.ControlLost && !core.Scrammed && ScramConditions(c))
        {
            core.Scrammed = true;
            core.AutoMode = false;
            _popup.PopupEntity(Loc.GetString("nestation-popup-scram"), c.CoreUid, PopupType.LargeCaution);
        }

        if (c.Turbine != null && !c.Turbine.Tripped && !core.ProtectionsBypassed && !core.ControlLost && TurbineTripConditions(c))
        {
            c.Turbine.Tripped = true;
            _popup.PopupEntity(Loc.GetString("nestation-popup-turbine-trip"), c.TurbineUid!.Value, PopupType.LargeCaution);
        }

        if (_popupTimer >= 5f && (w & (NestationWarning.FuelOverheat | NestationWarning.HighPressure | NestationWarning.Hydrogen | NestationWarning.TurbineOverspeed)) != 0)
            _popup.PopupEntity(Loc.GetString("nestation-popup-alarm"), c.CoreUid, PopupType.MediumCaution);
    }
}
