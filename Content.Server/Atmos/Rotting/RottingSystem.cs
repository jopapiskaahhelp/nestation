using Content.Shared.Atmos;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Temperature.Components;

namespace Content.Server.Atmos.Rotting;

/// <summary>
/// Nestation: механика гниения отключена.
/// Серверный Update (накопление гниения, урон, превращение в RotInto, выброс аммиака/миазм)
/// и выброс газа при гибе удалены. RottingComponent больше никому не добавляется.
/// </summary>
public sealed partial class RottingSystem : SharedRottingSystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TemperatureComponent, IsRottingEvent>(OnTempIsRotting);
    }

    private void OnTempIsRotting(EntityUid uid, TemperatureComponent component, ref IsRottingEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = component.Temperature < Atmospherics.T0C + 0.85f;
    }
}
