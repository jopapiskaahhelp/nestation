// Nestation new city CC BY-NC 3.0
namespace Content.Shared._Nestation.XenoHive;

public enum XenoCaste : byte
{
    Burrower,
    Drone,
    Runner,
    Spitter,
    Ravager,
    Praetorian,
    Queen,
}

[RegisterComponent]
public sealed partial class XenoCasteComponent : Component
{
    [DataField]
    public XenoCaste Caste = XenoCaste.Burrower;

    public int Class => Caste switch
    {
        XenoCaste.Burrower or XenoCaste.Drone => 1,
        XenoCaste.Runner or XenoCaste.Spitter => 2,
        XenoCaste.Ravager or XenoCaste.Praetorian => 3,
        _ => 0,
    };
}
