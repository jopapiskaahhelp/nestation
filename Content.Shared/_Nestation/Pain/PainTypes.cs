// Nestation New City CC BY-NC 3.0
using Robust.Shared.Serialization;

namespace Content.Shared._Nestation.Pain;

[Serializable, NetSerializable]
public enum PainLevel : byte
{
    None,
    Weak,
    Medium,
    Strong,
    Extreme
}

[Serializable, NetSerializable]
public enum PainKind : byte
{
    None,
    Blunt,
    Slash,
    Piercing,
    Burn,
    Acid,
    Shock
}

[Serializable, NetSerializable]
public enum PainkillerDrug : byte
{
    Paracetamol,
    Tramadol,
    Fentanyl
}

[Serializable, NetSerializable]
public enum PainOverdoseKind : byte
{
    TramadolSeizure,
    FentanylDepression,
    FentanylPressure
}

[Serializable, NetSerializable]
public enum PainCureKind : byte
{
    RespiratoryAntidote,
    HeartRestart
}
