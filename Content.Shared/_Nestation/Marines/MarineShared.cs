// Nestation New City CC BY-NC 3.0
using Robust.Shared.Serialization;

namespace Content.Shared._Nestation.Marines;

[Serializable, NetSerializable]
public enum MarineFlagVisuals : byte
{
    Raised,
}

[Serializable, NetSerializable]
public enum MarineFlagLayers : byte
{
    Flag,
}

[Serializable, NetSerializable]
public enum MarineMissionUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class MarineMissionStartMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class MarineMissionUiState : BoundUserInterfaceState
{
    public bool Started;
    public int Spawned;
    public int Total;

    public MarineMissionUiState(bool started, int spawned, int total)
    {
        Started = started;
        Spawned = spawned;
        Total = total;
    }
}
