// Nestation CC BY-NC 3.0
using Content.Shared._Nestation.Reactor;

namespace Content.Server._Nestation.Reactor;

public sealed partial class NestationPlantSystem
{
    private void StepTurbine(NestationPlantContext c, float dt)
    {
        var turbine = c.Turbine;

        if (turbine == null)
            return;

        var steamPower = c.Sg?.TurbinePower ?? 0f;
        var condTemp = c.Cond?.Temp ?? 100f;
        var vacuum = condTemp <= 60f ? 1f : MathF.Max(0.35f, 1f - 0.65f * (condTemp - 60f) / 50f);

        turbine.Efficiency = turbine.NominalEfficiency * vacuum;
        turbine.SteamPower = steamPower;
        turbine.MechanicalPower = turbine.Efficiency * steamPower;

        var gen = c.Gen;
        var synced = gen != null && gen.BreakerClosed && !turbine.Tripped && turbine.Rpm >= 0.95f;

        if (synced)
        {
            turbine.Rpm = 1f;
            turbine.Gross = MathF.Min(turbine.MechanicalPower, gen!.RatedPower * 1.2f);
            return;
        }

        var accel = turbine.MechanicalPower / turbine.RatedPower;
        var drag = 0.03f * turbine.Rpm * turbine.Rpm * turbine.Rpm + 0.002f;
        turbine.Rpm = MathF.Max(0f, turbine.Rpm + (accel - drag) / (2f * turbine.Inertia) * dt);
        turbine.Gross = 0f;
    }
}
