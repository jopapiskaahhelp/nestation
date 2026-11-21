// Nestation CC BY-NC 3.0
using Content.Server.Power.Components;
using Content.Shared._Nestation.Reactor;

namespace Content.Server._Nestation.Reactor;

public sealed partial class NestationPlantSystem
{
    public static float OwnConsumption(NestationPlantContext c)
    {
        return (c.Primary?.PowerDraw ?? 0f) + (c.Feed?.PowerDraw ?? 0f) + (c.Circulation?.PowerDraw ?? 0f);
    }

    private void StepGenerator(NestationPlantContext c, float dt)
    {
        var gen = c.Gen;

        if (gen == null || c.GeneratorUid == null)
            return;

        var gross = c.Turbine?.Gross ?? 0f;
        var own = OwnConsumption(c);
        var covered = MathF.Min(gross, own);
        var deficit = own - covered;
        var charge = 0f;

        if (deficit > 1f)
        {
            gen.Reserve = MathF.Max(0f, gen.Reserve - deficit * dt);
        }
        else if (gen.Reserve < gen.ReserveCapacity && gross > own)
        {
            charge = MathF.Min(gen.ReserveChargePower, gross - own);
            gen.Reserve = MathF.Min(gen.ReserveCapacity, gen.Reserve + charge * dt);
        }

        gen.Availability = deficit <= 1f || gen.Reserve > 0f
            ? 1f
            : Math.Clamp(covered / MathF.Max(own, 1f), 0f, 1f);

        gen.Gross = gross;
        gen.Own = own;
        gen.Net = MathF.Max(0f, gross - own - charge);

        SetSupply(c.GeneratorUid.Value, gen.Net);
    }

    private void SetSupply(EntityUid uid, float watts)
    {
        if (!TryComp(uid, out PowerSupplierComponent? supplier))
            return;

        supplier.MaxSupply = watts;
        supplier.Enabled = watts > 0f;
    }
}
