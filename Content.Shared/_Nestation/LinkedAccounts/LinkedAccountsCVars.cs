// Nestation New City CC BY-NC 3.0

using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._Nestation.LinkedAccounts;

[CVarDefs]
public sealed class LinkedAccountsCVars : CVars
{
    public static readonly CVarDef<string> Accounts =
        CVarDef.Create("nestation.tutorial.accounts", "", CVar.CLIENTONLY | CVar.ARCHIVE);
}
