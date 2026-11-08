// Nestation New City CC BY-NC 3.0
using Robust.Shared.GameStates;

namespace Content.Shared._Nestation.SupportStrike;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ConcussionComponent : Component
{
    [AutoNetworkedField]
    public TimeSpan Until;

    [AutoNetworkedField]
    public float Strength = 1f;

    [ViewVariables]
    public int BlurApplied;

    [ViewVariables]
    public TimeSpan NextBlurStep;
}
