// Nestation Anarhey CC BY-NC 3.0
namespace Content.Shared.Temperature.Components;

[RegisterComponent]
public sealed partial class FireHeaterComponent : Component
{
    [DataField]
    public float Power = 6000f;
}
