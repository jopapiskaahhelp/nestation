// Nestation CC BY-NC 3.0
using Content.Shared._Nestation.Reactor;

namespace Content.Server._Nestation.Reactor;

public sealed partial class NestationPlantSystem
{
    private void SetVisual(EntityUid? uid, NestationVisualState state)
    {
        if (uid != null)
            _appearance.SetData(uid.Value, NestationVisuals.State, state);
    }

    private void UpdateVisuals(NestationPlantContext c)
    {
        var core = c.Core;
        var coreState = NestationVisualState.Off;

        if (core.Integrity < 0.5f)
            coreState = NestationVisualState.Broken;
        else if ((core.Warnings & (NestationWarning.MeltImminent | NestationWarning.Hydrogen | NestationWarning.Scram)) != 0)
            coreState = NestationVisualState.Alarm;
        else if (core.FuelTemp > 900f)
            coreState = NestationVisualState.Hot;
        else if (core.Power > 0.01f)
            coreState = NestationVisualState.Running;

        SetVisual(c.CoreUid, coreState);

        if (c.Sg != null)
        {
            SetVisual(c.SteamGeneratorUid, c.Sg.Temp > 270f ? NestationVisualState.Hot : c.Sg.Temp > 100f ? NestationVisualState.Running : NestationVisualState.Off);
        }

        if (c.Primary != null)
            SetVisual(c.PrimaryPumpUid, c.Primary.Speed > 0.05f ? NestationVisualState.Running : NestationVisualState.Off);

        if (c.Feed != null)
            SetVisual(c.FeedPumpUid, c.Feed.Speed > 0.05f ? NestationVisualState.Running : NestationVisualState.Off);

        if (c.Circulation != null)
            SetVisual(c.CirculationPumpUid, c.Circulation.Speed > 0.05f ? NestationVisualState.Running : NestationVisualState.Off);

        if (c.Turbine != null)
            SetVisual(c.TurbineUid, c.Turbine.Rpm > 1.08f ? NestationVisualState.Hot : c.Turbine.Rpm > 0.05f ? NestationVisualState.Running : NestationVisualState.Off);

        if (c.Gen != null)
            SetVisual(c.GeneratorUid, c.Gen.Net > 0f ? NestationVisualState.Running : NestationVisualState.Off);

        if (c.Cond != null)
            SetVisual(c.CondenserUid, c.Cond.HeatIn > 1000f ? NestationVisualState.Running : NestationVisualState.Off);
    }
}
