// Nestation New City CC BY-NC 3.0

using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._Nestation.Tutorial;

[CVarDefs]
public sealed class TutorialCVars : CVars
{
    public static readonly CVarDef<bool> Seen =
        CVarDef.Create("nestation.tutorial.seen", false, CVar.CLIENTONLY | CVar.ARCHIVE);
}
