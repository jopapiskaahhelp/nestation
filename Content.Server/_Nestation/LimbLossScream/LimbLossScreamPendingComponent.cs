// Nestation New City CC BY-NC 3.0
namespace Content.Server._Nestation.LimbLossScream;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class LimbLossScreamPendingComponent : Component
{
    [DataField, AutoPausedField]
    public TimeSpan ScreamAt;
}
