// Nestation new city CC BY-NC 3.0
using Content.Server.NPC.HTN;
using Robust.Shared.Prototypes;

namespace Content.Server._Nestation.Director;

[RegisterComponent]
public sealed partial class DirectorKittenFollowComponent : Component
{
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(20);

    [ViewVariables]
    public TimeSpan EndTime;

    [ViewVariables]
    public ProtoId<HTNCompoundPrototype> OriginalTask;
}
