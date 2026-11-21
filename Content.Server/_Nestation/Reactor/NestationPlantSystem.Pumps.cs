// Nestation CC BY-NC 3.0
using Content.Shared._Nestation.Reactor;

namespace Content.Server._Nestation.Reactor;

public sealed partial class NestationPlantSystem
{
    private void StepPumps(NestationPlantContext c, float dt)
    {
        var availability = c.Gen?.Availability ?? 1f;

        if (c.Primary != null)
            StepPump(c.Primary, c.Primary.Setpoint, availability, dt);

        if (c.Feed != null)
        {
            var target = c.Feed.Setpoint;

            if (c.Feed.Auto && c.Sg != null)
                target = Math.Clamp(c.Sg.SteamFlow + 2f * (0.5f - c.Sg.Level), 0f, 1.1f);

            StepPump(c.Feed, target, availability, dt);
        }

        if (c.Circulation != null)
        {
            var target = c.Circulation.Setpoint;

            if (c.Circulation.Auto)
                target = Math.Clamp((c.Cond?.HeatIn ?? 0f) / 2_100_000f, 0.25f, 1.1f);

            StepPump(c.Circulation, target, availability, dt);
        }
    }

    private static void StepPump(NestationPumpComponent pump, float target, float availability, float dt)
    {
        var goal = Math.Clamp(target, 0f, 1.1f) * Math.Clamp(availability, 0f, 1f);
        var tau = goal > pump.Speed ? pump.SpinUpTime : pump.CoastDownTime;
        pump.Speed += (goal - pump.Speed) * (1f - MathF.Exp(-dt / tau));
        pump.Speed = Math.Clamp(pump.Speed, 0f, 1.1f);
        pump.PowerDraw = pump.NominalPower * pump.Speed * pump.Speed * pump.Speed;
    }
}
