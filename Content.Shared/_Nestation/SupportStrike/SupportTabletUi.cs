// Nestation New City CC BY-NC 3.0
using Robust.Shared.Serialization;

namespace Content.Shared._Nestation.SupportStrike;

[Serializable, NetSerializable]
public enum SupportTabletUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class SupportTabletBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly TimeSpan NextUse;
    public readonly bool HasTarget;

    public SupportTabletBoundUserInterfaceState(TimeSpan nextUse, bool hasTarget)
    {
        NextUse = nextUse;
        HasTarget = hasTarget;
    }
}

[Serializable, NetSerializable]
public sealed class SupportTabletLaunchMessage : BoundUserInterfaceMessage;
