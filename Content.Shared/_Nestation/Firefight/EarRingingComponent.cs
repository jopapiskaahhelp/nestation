// Nestation New City CC BY-NC 3.0
using Robust.Shared.GameStates;

namespace Content.Shared._Nestation.Firefight;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EarRingingComponent : Component
{
    [AutoNetworkedField]
    public TimeSpan Until;

    [AutoNetworkedField]
    public float Strength = 1f;

    [ViewVariables]
    public float Dose;

    [ViewVariables]
    public TimeSpan DoseUpdated;

    [ViewVariables]
    public int BlurApplied;

    [ViewVariables]
    public TimeSpan NextBlurStep;
}
