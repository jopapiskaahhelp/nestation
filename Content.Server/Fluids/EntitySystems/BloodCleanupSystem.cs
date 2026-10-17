using Content.Server.Fluids.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Server.Fluids.EntitySystems;

public sealed class BloodCleanupSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private TimeSpan _nextCheck;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BloodCleanupComponent, SolutionChangedEvent>(OnSolutionChanged);
    }

    // В этом форке событие называется SolutionChangedEvent (старое имя SolutionContainerChangedEvent удалено).
    private void OnSolutionChanged(Entity<BloodCleanupComponent> ent, ref SolutionChangedEvent args)
    {
        if (ent.Comp.CleanupTime != null)
            return;

        foreach (var reagent in args.Solution.Comp.Solution.Contents)
        {
            if (!ent.Comp.Reagents.Contains(reagent.Reagent.Prototype))
                continue;

            ent.Comp.CleanupTime = _timing.CurTime + ent.Comp.Delay;
            return;
        }
    }

    public override void Update(float frameTime)
    {
        var curTime = _timing.CurTime;
        if (curTime < _nextCheck)
            return;

        _nextCheck = curTime + CheckInterval;

        var query = EntityQueryEnumerator<BloodCleanupComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.CleanupTime is { } time && curTime >= time)
                QueueDel(uid);
        }
    }
}
