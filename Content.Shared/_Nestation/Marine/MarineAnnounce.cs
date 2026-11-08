// Nestation new city CC BY-NC 3.0
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._NeStation.Marine;

[RegisterComponent]
public sealed partial class MarineAnnounceConsoleComponent : Component
{
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(10);

    [DataField]
    public int MaxLength = 300;

    public TimeSpan NextAnnounceTime;
}

[Serializable, NetSerializable]
public enum MarineAnnounceUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class MarineAnnounceConsoleState : BoundUserInterfaceState
{
    public TimeSpan NextAnnounceTime;
    public int MaxLength;

    public MarineAnnounceConsoleState(TimeSpan nextAnnounceTime, int maxLength)
    {
        NextAnnounceTime = nextAnnounceTime;
        MaxLength = maxLength;
    }
}

[Serializable, NetSerializable]
public sealed class MarineAnnounceSendMessage : BoundUserInterfaceMessage
{
    public string Message;

    public MarineAnnounceSendMessage(string message)
    {
        Message = message;
    }
}
