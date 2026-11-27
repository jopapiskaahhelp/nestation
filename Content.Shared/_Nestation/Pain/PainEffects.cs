// Nestation New City CC BY-NC 3.0
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._Nestation.Pain;

public sealed partial class Painkiller : EntityEffectBase<Painkiller>
{
    [DataField(required: true)]
    public PainkillerDrug Drug;

    public override string? EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString($"entity-effect-guidebook-painkiller-{Drug.ToString().ToLowerInvariant()}", ("chance", Probability));
    }
}

public sealed partial class PainOverdose : EntityEffectBase<PainOverdose>
{
    [DataField(required: true)]
    public PainOverdoseKind Kind;

    public override string? EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString($"entity-effect-guidebook-pain-overdose-{Kind.ToString().ToLowerInvariant()}", ("chance", Probability));
    }
}

public sealed partial class PainCure : EntityEffectBase<PainCure>
{
    [DataField(required: true)]
    public PainCureKind Kind;

    public override string? EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString($"entity-effect-guidebook-pain-cure-{Kind.ToString().ToLowerInvariant()}", ("chance", Probability));
    }
}

public sealed partial class PainkillerEntityEffectSystem : EntityEffectSystem<PainComponent, Painkiller>
{
    [Dependency] private SharedPainSystem _pain = default!;

    protected override void Effect(Entity<PainComponent> entity, ref EntityEffectEvent<Painkiller> args)
    {
        _pain.OnPainkiller(entity.Owner, entity.Comp, args.Effect.Drug);
    }
}

public sealed partial class PainOverdoseEntityEffectSystem : EntityEffectSystem<PainComponent, PainOverdose>
{
    [Dependency] private SharedPainSystem _pain = default!;

    protected override void Effect(Entity<PainComponent> entity, ref EntityEffectEvent<PainOverdose> args)
    {
        _pain.OnOverdose(entity.Owner, entity.Comp, args.Effect.Kind);
    }
}

public sealed partial class PainCureEntityEffectSystem : EntityEffectSystem<PainComponent, PainCure>
{
    [Dependency] private SharedPainSystem _pain = default!;

    protected override void Effect(Entity<PainComponent> entity, ref EntityEffectEvent<PainCure> args)
    {
        _pain.OnCure(entity.Owner, entity.Comp, args.Effect.Kind);
    }
}
