// Nestation new city CC BY-NC 3.0
using Content.Shared.Access;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CartridgeLoader.Cartridges;

[RegisterComponent]
public sealed partial class LawsCartridgeComponent : Component
{
    [DataField]
    public ProtoId<AccessLevelPrototype> WriteAccess = "Captain";
}

public static class LawsCartridgeLimits
{
    public const int MaxLaws = 50;
    public const int MaxLawLength = 300;
}

[Serializable, NetSerializable]
public sealed class LawsUiState : BoundUserInterfaceState
{
    public readonly List<string> Laws;
    public readonly bool CanWrite;

    public LawsUiState(List<string> laws, bool canWrite)
    {
        Laws = laws;
        CanWrite = canWrite;
    }
}

[Serializable, NetSerializable]
public enum LawsUiAction : byte
{
    Add,
    Edit,
    Remove,
}

[Serializable, NetSerializable]
public sealed class LawsUiMessageEvent : CartridgeMessageEvent
{
    public readonly LawsUiAction Action;
    public readonly int Index;
    public readonly string Text;

    public LawsUiMessageEvent(LawsUiAction action, int index, string text)
    {
        Action = action;
        Index = index;
        Text = text;
    }
}
