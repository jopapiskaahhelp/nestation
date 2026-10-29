// Nestation new city CC BY-NC 3.0

namespace Content.Server.AntiFlood;

[RegisterComponent]
public sealed partial class AntiFloodMuteComponent : Component
{
    public TimeSpan LockUntil;
}
