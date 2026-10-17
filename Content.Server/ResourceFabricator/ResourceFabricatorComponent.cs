using Robust.Shared.Prototypes;

namespace Content.Server.ResourceFabricator;

/// <summary>
/// Установка, которая раз в <see cref="IntervalSeconds"/> секунд выдаёт <see cref="Amount"/> единиц ресурса.
/// Таймер идёт только пока установка запитана; без питания он стоит на месте.
/// </summary>
[RegisterComponent]
public sealed partial class ResourceFabricatorComponent : Component
{
    /// <summary>
    /// Прототип выдаваемого ресурса (стак, например SheetSteel1).
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Resource;

    /// <summary>
    /// Сколько единиц ресурса выдаётся за один цикл.
    /// </summary>
    [DataField]
    public int Amount = 1;

    /// <summary>
    /// Длительность цикла в секундах.
    /// </summary>
    [DataField]
    public float IntervalSeconds = 60f;

    /// <summary>
    /// Накопленное время текущего цикла в секундах.
    /// </summary>
    [DataField]
    public float ProgressSeconds;
}
