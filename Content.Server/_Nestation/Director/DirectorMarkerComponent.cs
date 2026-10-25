// Nestation Anarhey CC BY-NC 3.0
namespace Content.Server._Nestation.Director;

[RegisterComponent]
public sealed partial class DirectorMarkerComponent : Component
{
    [DataField]
    public float Radius = 18f;

    [DataField]
    public float SecurityCorpseRadius = 25f;

    [ViewVariables]
    public int Event = -1;

    [ViewVariables]
    public HashSet<int> Used = new();

    [ViewVariables]
    public TimeSpan NextActivation;
}
