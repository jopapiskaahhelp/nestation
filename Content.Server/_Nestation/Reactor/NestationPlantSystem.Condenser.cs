// Nestation CC BY-NC 3.0
using Content.Shared._Nestation.Reactor;

namespace Content.Server._Nestation.Reactor;

public sealed partial class NestationPlantSystem
{
    private void StepCondenser(NestationPlantContext c, float dt)
    {
        var cond = c.Cond;

        if (cond == null)
            return;

        var steamPower = c.Sg?.TurbinePower ?? 0f;
        var gross = c.Turbine?.Gross ?? 0f;
        var heatIn = MathF.Max(0f, steamPower - gross) + (c.Sg?.BypassPower ?? 0f);
        cond.HeatIn = heatIn;

        var levelFactor = Math.Clamp(c.PoolLevel / 0.25f, 0f, 1f);
        var flow = c.Circulation != null && c.CoolingClosed ? c.Circulation.Speed * levelFactor : 0f;
        var conductance = cond.Conductance * (0.03f + 0.97f * flow);
        var sink = c.Pool.Count > 0 ? c.PoolTemp : NestationTuning.Ambient;
        var heatOut = conductance * MathF.Max(0f, cond.Temp - sink);

        cond.Temp += (heatIn - heatOut) / cond.HeatCapacity * dt;
        cond.Temp = Math.Clamp(cond.Temp, NestationTuning.Ambient, 400f);
        cond.HeatOut = heatOut;

        if (c.CoolingClosed && c.Pool.Count > 0)
            AddPoolHeat(c.Pool, (heatOut + OwnConsumption(c)) * dt);
    }
}
