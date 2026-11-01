// Nestation New City CC BY-NC 3.0
namespace Content.Server._Nestation.Firefight;

[RegisterComponent]
public sealed partial class FirefightComponent : Component
{
    [ViewVariables]
    public float Suppression;

    [ViewVariables]
    public float Flinch;

    [ViewVariables]
    public float StillTime;

    [ViewVariables]
    public TimeSpan LastSuppressed;

    [ViewVariables]
    public TimeSpan LastNotice;
}
