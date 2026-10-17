using Content.Server.Stack;
using Content.Shared.Power.EntitySystems;

namespace Content.Server.ResourceFabricator;

public sealed partial class ResourceFabricatorSystem : EntitySystem
{
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private StackSystem _stack = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ResourceFabricatorComponent>();
        while (query.MoveNext(out var uid, out var fabricator))
        {
            if (!_power.IsPowered(uid))
                continue;

            fabricator.ProgressSeconds += frameTime;

            if (fabricator.ProgressSeconds < fabricator.IntervalSeconds)
                continue;

            fabricator.ProgressSeconds -= fabricator.IntervalSeconds;
            _stack.SpawnMultipleAtPosition(fabricator.Resource, fabricator.Amount, Transform(uid).Coordinates);
        }
    }
}
