// Nestation Anarhey CC BY-NC 3.0
using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._Nestation.CCVars;

[CVarDefs]
public sealed class NestationCVars : CVars
{
    /// <summary>
    /// Ночью считается момент, когда "дневной фактор" LightCycle (0 = глубокая ночь, 1 = день)
    /// не выше этого порога. 0 = только самая тёмная часть, 0.15 = ещё и начало рассвета/конец заката.
    /// </summary>
    public static readonly CVarDef<float> NightDayFactorThreshold =
        CVarDef.Create("nestation.night.day_factor_threshold", 0.15f, CVar.SERVERONLY);

    /// <summary>Отладка: ночь всегда включена (независимо от LightCycle).</summary>
    public static readonly CVarDef<bool> NightForce =
        CVarDef.Create("nestation.night.force", false, CVar.SERVERONLY);
}
