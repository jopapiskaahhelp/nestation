// Nestation CC BY-NC 3.0
using Content.Shared._Nestation.Reactor;

namespace Content.Server._Nestation.Reactor;

public sealed partial class NestationPlantSystem
{
    private void StepSteamGenerator(NestationPlantContext c, float heatIn, float dt)
    {
        var sg = c.Sg;

        if (sg == null)
            return;

        var core = c.Core;
        var turbine = c.Turbine;
        var gen = c.Gen;
        var cond = c.Cond;

        var running = core.Power > 0.02f;
        sg.SetTemp = running
            ? MathF.Min(sg.SteamSetTemp, sg.SetTemp + 0.8f * dt)
            : MathF.Max(60f, sg.SetTemp - 0.4f * dt);

        var demand = Math.Clamp(heatIn + sg.HeatCapacity * (sg.Temp - sg.SetTemp) / NestationTuning.SteamController, 0f, 4_800_000f);

        var capacity = 0f;

        if (turbine != null && !turbine.Tripped && sg.Temp >= 180f && cond != null)
        {
            capacity = turbine.MaxSteamPower;

            if (!core.ProtectionsBypassed)
            {
                capacity *= Math.Clamp((NestationTuning.TripRpm - turbine.Rpm) / 0.05f, 0f, 1f);

                if (gen == null || !gen.BreakerClosed)
                    capacity = MathF.Min(capacity, 0.03f * turbine.MaxSteamPower);
            }
        }

        var turbinePower = MathF.Min(demand, capacity);
        var bypassOk = cond != null && cond.Temp < 120f;
        var bypass = bypassOk ? MathF.Min(demand - turbinePower, sg.BypassCapacity) : 0f;
        var relief = MathF.Max(0f, sg.Temp - sg.ReliefTemp) * sg.ReliefConductance;

        sg.Temp += (heatIn - turbinePower - bypass - relief) / sg.HeatCapacity * dt;
        sg.Temp = Math.Clamp(sg.Temp, NestationTuning.Ambient, 600f);

        sg.SteamFlow = (turbinePower + bypass + relief) / NestationTuning.SteamFlowNominal;
        sg.Level = Math.Clamp(sg.Level + 0.012f * (c.PhiFeed - sg.SteamFlow) * dt, 0f, 1f);
        sg.Pressure = NestationTuning.SaturationPressure(sg.Temp);
        sg.TurbinePower = turbinePower;
        sg.BypassPower = bypass;
        sg.ReliefPower = relief;
    }
}
