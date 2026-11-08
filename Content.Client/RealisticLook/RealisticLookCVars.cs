using Robust.Shared.Configuration;

namespace Content.Client.RealisticLook;

[CVarDefs]
public sealed class RealisticLookCVars
{
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("rl.enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<string> Preset =
        CVarDef.Create("rl.preset", "horror", CVar.CLIENTONLY | CVar.ARCHIVE);
}
