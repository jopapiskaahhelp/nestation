// Nestation Anarhey CC BY-NC 3.0
using Robust.Shared.Serialization;

namespace Content.Shared.Zones;

[Serializable, NetSerializable]
public sealed class ZoneEnteredEvent : EntityEventArgs
{
    public readonly string Name;

    public ZoneEnteredEvent(string name)
    {
        Name = name;
    }
}
