// Nestation New City CC BY-NC 3.0
using Content.Shared.Hands.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._Nestation.Firefight;

public sealed partial class ReloadLockSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedGunSystem _gun = default!;

    private static readonly TimeSpan TacticalLock = TimeSpan.FromSeconds(0.6);
    private static readonly TimeSpan EmergencyLock = TimeSpan.FromSeconds(1.6);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<GunComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<ReloadLockComponent, ShotAttemptedEvent>(OnShotAttempted);
    }

    private void OnShotAttempted(Entity<ReloadLockComponent> ent, ref ShotAttemptedEvent args)
    {
        if (ent.Comp.LockedUntil > _timing.CurTime)
            args.Cancel();
    }

    private void OnRemoved(Entity<GunComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (!_net.IsServer ||
            args.Container.ID != SharedGunSystem.MagazineSlot ||
            TerminatingOrDeleted(ent))
        {
            return;
        }

        var comp = EnsureComp<ReloadLockComponent>(ent);
        comp.LastMagazineEmpty = _gun.GetAmmoCount(args.Entity) <= 0;
    }

    private void OnInserted(Entity<GunComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (!_net.IsServer ||
            args.Container.ID != SharedGunSystem.MagazineSlot ||
            TerminatingOrDeleted(ent))
        {
            return;
        }

        if (!HasComp<HandsComponent>(Transform(ent).ParentUid))
            return;

        var comp = EnsureComp<ReloadLockComponent>(ent);

        bool tactical;

        if (HasComp<ChamberMagazineAmmoProviderComponent>(ent))
            tactical = _gun.GetChamberEntity(ent) != null;
        else
            tactical = !comp.LastMagazineEmpty;

        comp.LockedUntil = _timing.CurTime + (tactical ? TacticalLock : EmergencyLock);
        comp.LastMagazineEmpty = false;
        Dirty(ent, comp);
    }
}
