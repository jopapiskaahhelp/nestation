// Nestation Anarhey CC BY-NC 3.0
using Content.Shared.Doors;
using Content.Shared.Prying.Components;

namespace Content.Shared._Nestation.XenoHive;

public sealed partial class XenoOnlyDoorSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<XenoOnlyDoorComponent, BeforeDoorOpenedEvent>(OnBeforeOpen);
        SubscribeLocalEvent<XenoOnlyDoorComponent, BeforePryEvent>(OnBeforePry);
    }

    private void OnBeforeOpen(EntityUid uid, XenoOnlyDoorComponent comp, BeforeDoorOpenedEvent args)
    {
        if (args.User is not { } user || !HasComp<XenoCasteComponent>(user))
            args.Cancel();
    }

    private void OnBeforePry(Entity<XenoOnlyDoorComponent> ent, ref BeforePryEvent args)
    {
        if (!HasComp<XenoCasteComponent>(args.User))
            args.Cancelled = true;
    }
}
