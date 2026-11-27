// Nestation New City CC BY-NC 3.0
using Content.Shared.Movement.Systems;

namespace Content.Shared._Nestation.Pain;

public abstract partial class SharedPainSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PainComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<PainComponent, AfterAutoHandleStateEvent>(OnAfterState);
    }

    private void OnRefreshSpeed(EntityUid uid, PainComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        if (MathHelper.CloseTo(component.SpeedModifier, 1f))
            return;

        args.ModifySpeed(component.SpeedModifier, component.SpeedModifier);
    }

    private void OnAfterState(EntityUid uid, PainComponent component, ref AfterAutoHandleStateEvent args)
    {
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    public virtual void OnPainkiller(EntityUid uid, PainComponent component, PainkillerDrug drug)
    {
    }

    public virtual void OnOverdose(EntityUid uid, PainComponent component, PainOverdoseKind kind)
    {
    }

    public virtual void OnCure(EntityUid uid, PainComponent component, PainCureKind kind)
    {
    }
}
