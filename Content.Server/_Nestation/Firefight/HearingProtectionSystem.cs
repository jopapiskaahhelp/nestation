// Nestation New City CC BY-NC 3.0
using Content.Shared._Nestation.Firefight;
using Content.Shared.Inventory;

namespace Content.Server._Nestation.Firefight;

public sealed partial class HearingProtectionSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;

    private const string EarsSlot = "ears";

    public bool IsProtected(EntityUid uid)
    {
        return _inventory.TryGetSlotEntity(uid, EarsSlot, out var item) &&
               HasComp<HearingProtectionComponent>(item);
    }
}
