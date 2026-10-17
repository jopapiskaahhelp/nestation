using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Server.Fluids.Components;

/// <summary>
/// Автоматически удаляет лужу через заданное время после появления в ней крови.
/// Вешается на прототип Puddle.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class BloodCleanupComponent : Component
{
    /// <summary>Через сколько после появления крови лужа удаляется.</summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.FromMinutes(5);

    /// <summary>Реагенты, которые считаются кровью.</summary>
    [DataField]
    public HashSet<ProtoId<ReagentPrototype>> Reagents = new() { "Blood" };

    /// <summary>Когда лужа будет удалена. null — крови в луже ещё не было.</summary>
    [DataField, AutoPausedField]
    public TimeSpan? CleanupTime;
}
