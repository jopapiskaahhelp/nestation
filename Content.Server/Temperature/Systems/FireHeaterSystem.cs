// Nestation Anarhey CC BY-NC 3.0
using Content.Shared.Placeable;
using Content.Shared.Temperature.Components;

namespace Content.Server.Temperature.Systems;

public sealed partial class FireHeaterSystem : EntitySystem
{
    [Dependency] private TemperatureSystem _temperature = default!;

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<FireHeaterComponent, ItemPlacerComponent>();
        while (query.MoveNext(out _, out var heater, out var placer))
        {
            if (placer.PlacedEntities.Count == 0)
                continue;

            var energy = heater.Power * frameTime / placer.PlacedEntities.Count;

            foreach (var ent in placer.PlacedEntities)
            {
                _temperature.ChangeHeat(ent, energy);
            }
        }
    }
}
