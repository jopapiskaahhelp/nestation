// Nestation New City CC BY-NC 3.0
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Nestation.Marines;

[Serializable, NetSerializable]
public enum MarineLiftState : byte
{
    Raised,
    Lowering,
    Lowered,
    Raising,
}

[Serializable, NetSerializable]
public enum MarineLiftVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum MarineLiftLayers : byte
{
    Base,
}

[Serializable, NetSerializable]
public enum MarineSupplyUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class MarineSupplyBuyMessage : BoundUserInterfaceMessage
{
    public string Id;

    public MarineSupplyBuyMessage(string id)
    {
        Id = id;
    }
}

[Serializable, NetSerializable]
public sealed class MarineSupplyUiState : BoundUserInterfaceState
{
    public int Credits;
    public MarineLiftState Lift;
    public int SecondsLeft;
    public int Queued;
    public bool CanOrder;

    public MarineSupplyUiState(int credits, MarineLiftState lift, int secondsLeft, int queued, bool canOrder)
    {
        Credits = credits;
        Lift = lift;
        SecondsLeft = secondsLeft;
        Queued = queued;
        CanOrder = canOrder;
    }
}

[Prototype]
public sealed partial class MarineSupplyPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField]
    public string Category = "utility";

    [DataField(required: true)]
    public int Price;

    [DataField(required: true)]
    public List<EntProtoId> Entities = new();
}
