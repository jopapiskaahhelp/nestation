// Nestation Anarhey CC BY-NC 3.0

using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Server.AntiFlood;

[CVarDefs]
public sealed class AntiFloodCVars : CVars
{
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("antiflood.enabled", true, CVar.SERVERONLY);

    public static readonly CVarDef<int> RepeatLimit =
        CVarDef.Create("antiflood.repeat_limit", 3, CVar.SERVERONLY);

    public static readonly CVarDef<float> RepeatWindow =
        CVarDef.Create("antiflood.repeat_window", 30f, CVar.SERVERONLY);

    public static readonly CVarDef<float> MimeMuteSeconds =
        CVarDef.Create("antiflood.mime_mute_seconds", 60f, CVar.SERVERONLY);

    public static readonly CVarDef<string> AllowedChars =
        CVarDef.Create("antiflood.allowed_chars", "", CVar.SERVERONLY);

    public static readonly CVarDef<bool> ExemptAdmins =
        CVarDef.Create("antiflood.exempt_admins", true, CVar.SERVERONLY);
}
