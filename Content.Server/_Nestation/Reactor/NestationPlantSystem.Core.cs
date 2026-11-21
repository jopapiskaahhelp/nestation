// Nestation CC BY-NC 3.0
using Content.Shared._Nestation.Reactor;
using Content.Shared.Damage.Components;

namespace Content.Server._Nestation.Reactor;

public sealed partial class NestationPlantSystem
{
    public NestationFuelAssemblyComponent? GetFuelFor(EntityUid core)
    {
        return GetFuel(core);
    }

    private NestationFuelAssemblyComponent? GetFuel(EntityUid core)
    {
        var item = _slots.GetItemOrNull(core, FuelSlot);

        if (item != null && TryComp(item.Value, out NestationFuelAssemblyComponent? fuel))
            return fuel;

        return null;
    }

    private float DamageFraction(EntityUid uid, float limit)
    {
        if (limit <= 0f || !TryComp(uid, out DamageableComponent? damage))
            return 0f;

        return Math.Clamp(NestationDamageReader.Total(damage) / limit, 0f, 1f);
    }

    private float StepCore(NestationPlantContext c, float dt)
    {
        var core = c.Core;
        var sg = c.Sg;
        var ts = NestationPhysics.TimeCompression;
        var fuel = GetFuel(c.CoreUid);

        var damage = DamageFraction(c.CoreUid, core.DamageLimit);
        core.Integrity = 1f - damage;
        core.Breach = MathF.Max(core.ForcedBreach, damage < 0.05f ? 0f : damage);
        core.ControlLost = damage >= 0.35f;
        core.RodsJammed = damage >= 0.6f || core.ForcedJam;

        if (core.ControlLost && !core.Scrammed)
        {
            core.Scrammed = true;
            core.AutoMode = false;
            core.RodTarget = 1f;
        }

        var level = core.InventoryNominal > 0f ? core.Inventory / core.InventoryNominal : 0f;
        var wetted = Math.Clamp(level / 0.4f, 0f, 1f);
        var open = !c.PrimaryClosed || core.Breach > 0.01f;
        var satTemp = open ? 120f : core.ProtectionsBypassed ? 3000f : core.BoilingTemp;

        var reactivity = fuel != null ? MathF.Max(0f, fuel.BaseReactivity - fuel.ReactivityLoss * fuel.Burnup) : 0f;
        var feedback = 1f - core.TempCoefficient * (core.FuelTemp - core.ReferenceFuelTemp);

        if (core.CoolantTemp > core.BoilingTemp)
            feedback *= 1f - core.VoidCoefficient * Math.Clamp((core.CoolantTemp - core.BoilingTemp) / 30f, 0f, 1f);

        feedback *= 0.3f + 0.7f * wetted;
        feedback = Math.Clamp(feedback, 0.05f, 2f);

        if (!core.RodsJammed)
        {
            if (core.Scrammed)
            {
                core.RodTarget = 1f;
                core.RodInsertion = NestationTuning.MoveToward(core.RodInsertion, 1f, NestationTuning.ScramRodSpeed * dt);
            }
            else if (core.AutoMode)
            {
                var error = core.Power - core.Setpoint;
                var rate = Math.Clamp(error * NestationTuning.RodGain, -NestationTuning.RodSpeed, NestationTuning.RodSpeed);
                core.RodInsertion = Math.Clamp(core.RodInsertion + rate * dt, 0f, 1f);
                core.RodTarget = core.RodInsertion;
            }
            else
            {
                core.RodInsertion = NestationTuning.MoveToward(core.RodInsertion, core.RodTarget, NestationTuning.RodSpeed * dt);
            }
        }

        var k = fuel != null ? (1f - core.RodInsertion) * reactivity * feedback : 0f;
        core.Target = k;
        var target = Math.Clamp(k, 0f, 4f);

        var tau = target > core.Power
            ? (target > 1.6f ? NestationTuning.PromptTime : NestationTuning.NeutronTime)
            : NestationTuning.ScramTime;

        core.Power += (target - core.Power) * (1f - MathF.Exp(-dt / tau));
        core.Power = MathF.Max(0f, core.Power);

        if (fuel != null)
            fuel.Burnup = MathF.Min(1f, fuel.Burnup + core.Power * dt / fuel.LifeSeconds);

        if (core.Power >= 0.02f)
        {
            core.ShutdownTime = 0f;
            core.OperatingTime = MathF.Min(30_000_000f, core.OperatingTime + core.Power * dt * core.DecayTimeScale);
            core.PrePower += (core.Power - core.PrePower) * (1f - MathF.Exp(-dt / 60f));
        }
        else
        {
            core.ShutdownTime += dt * core.DecayTimeScale;
        }

        var decayTarget = core.Power < 0.02f && fuel != null
            ? NestationPhysics.DecayHeatFraction(core.ShutdownTime, core.OperatingTime) * core.PrePower * core.DecayHeatBoost
            : 0f;

        core.DecayHeat += (decayTarget - core.DecayHeat) * (1f - MathF.Exp(-dt / 3f));
        core.DecayPower = core.NominalPower * core.DecayHeat;

        var thermal = core.NominalPower * (core.Power + core.DecayHeat);
        core.ThermalPower = thermal;

        var natural = c.PrimaryClosed ? 0.12f : 0.04f;
        var flowFactor = natural + (1f - natural) * MathF.Pow(c.PhiPrimary, 0.8f);
        var gfc = core.FuelConductance * flowFactor * (0.04f + 0.96f * wetted);

        if (core.CoolantTemp > core.CrisisTemp && !open)
            gfc *= 0.35f;

        var qFuel = gfc * (core.FuelTemp - core.CoolantTemp);
        var heatToSg = 0f;

        if (sg != null && c.PrimaryClosed && wetted > 0.5f)
        {
            var dryout = Math.Clamp((sg.Level - 0.05f) / 0.25f, 0.2f, 1f);
            var sgFlow = 0.08f + 0.92f * MathF.Pow(c.PhiPrimary, 0.8f);
            heatToSg = sg.PrimaryConductance * sgFlow * dryout * MathF.Max(0f, core.CoolantTemp - sg.Temp);
        }

        var qZr = 0f;
        core.HydrogenRate = 0f;

        var steamPool = core.Inventory + 0.15f * core.CavityWater;

        if (fuel != null && core.ZirconiumOxidized < core.ZirconiumMass && steamPool > 0f)
        {
            var steam = Math.Clamp(steamPool / 40f, 0f, 1f);
            var remaining = core.ZirconiumMass - core.ZirconiumOxidized;
            var oxidized = remaining * NestationPhysics.ZirconiumRate(core.FuelTemp) * ts * dt * steam;
            oxidized = MathF.Min(MathF.Min(oxidized, remaining), steamPool / NestationPhysics.WaterPerZirconium);

            if (oxidized > 0f)
            {
                var water = oxidized * NestationPhysics.WaterPerZirconium;
                var fromInventory = MathF.Min(water, core.Inventory);
                core.Inventory -= fromInventory;
                core.CavityWater = MathF.Max(0f, core.CavityWater - (water - fromInventory) / 0.15f);
                core.ZirconiumOxidized += oxidized;
                core.Hydrogen += oxidized * NestationPhysics.HydrogenPerZirconium;
                core.HydrogenRate = oxidized * NestationPhysics.HydrogenPerZirconium / dt;
                qZr = oxidized * NestationPhysics.ZirconiumReactionHeat / (dt * ts);
            }
        }

        core.Hydrogen = MathF.Max(0f, core.Hydrogen - core.Hydrogen * 0.0005f * dt);

        var coolantCapacity = (MathF.Max(core.Inventory, 0f) * NestationPhysics.WaterHeatCapacity + core.PipingHeatCapacity) / ts;
        var removed = heatToSg;

        core.FuelTemp += (thermal + qZr - qFuel) / core.FuelHeatCapacity * dt;

        if (core.CoolantTemp > satTemp && core.Inventory > 0f)
        {
            var flash = (core.CoolantTemp - satTemp) * core.Inventory * NestationPhysics.WaterHeatCapacity / NestationPhysics.LatentHeat;
            core.Inventory -= MathF.Min(core.Inventory, flash);
            core.CoolantTemp = satTemp;
        }

        var netHeat = qFuel - removed;

        if (core.CoolantTemp >= satTemp - 0.01f && netHeat > 0f && core.Inventory > 0f)
        {
            core.Inventory -= MathF.Min(core.Inventory, netHeat * dt * ts / NestationPhysics.LatentHeat);
            core.CoolantTemp = satTemp;
        }
        else
        {
            core.CoolantTemp += netHeat / coolantCapacity * dt;

            if (core.CoolantTemp > satTemp && core.Inventory > 0f)
            {
                var flash = (core.CoolantTemp - satTemp) * core.Inventory * NestationPhysics.WaterHeatCapacity / NestationPhysics.LatentHeat;
                core.Inventory -= MathF.Min(core.Inventory, flash);
                core.CoolantTemp = satTemp;
            }
        }

        core.FuelTemp = Math.Clamp(core.FuelTemp, NestationTuning.Ambient, 6000f);
        core.CoolantTemp = Math.Clamp(core.CoolantTemp, NestationTuning.Ambient, 3000f);

        if (open)
        {
            var leak = (c.PrimaryClosed ? 0f : 30f) + core.Breach * core.Breach * 60f;
            var floor = core.InventoryNominal * 0.35f * (1f - core.Breach);

            if (core.Inventory > floor)
                core.Inventory = MathF.Max(floor, core.Inventory - leak * dt);
        }
        else if (core.Inventory < core.InventoryNominal)
        {
            core.Inventory = MathF.Min(core.InventoryNominal, core.Inventory + 2f * dt);
        }

        core.Inventory = MathF.Max(0f, core.Inventory);

        var unremoved = thermal + qZr - heatToSg;
        core.DryoutSeconds = unremoved > 1000f && core.Inventory > 0f
            ? NestationPhysics.EvaporationTime(core.Inventory, satTemp - core.CoolantTemp, unremoved) / ts
            : -1f;

        var wantLock = core.Power > 0.001f || core.FuelTemp > 80f;

        if (wantLock != core.FuelLocked)
        {
            core.FuelLocked = wantLock;
            _slots.SetLock(c.CoreUid, FuelSlot, wantLock);
        }

        return heatToSg;
    }
}
