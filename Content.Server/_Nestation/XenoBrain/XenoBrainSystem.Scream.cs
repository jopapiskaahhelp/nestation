// Nestation Anarhey CC BY-NC 3.0
using Content.Shared.Screech;

namespace Content.Server._Nestation.XenoBrain;

public sealed partial class XenoBrainSystem
{
    [Dependency] private ScreechSystem _screech = default!;

    private void TryScream(Ctx c)
    {
        var x = c.X;
        var b = x.Brain;
        var now = c.Now;

        if (!IsTank(b) || now < b.NextScreamAt || b.DuelOpponent != null)
            return;

        if (!TryComp<ScreechActionComponent>(x.Uid, out var screech))
            return;

        if (!TryFindVisiblePlayer(x, b.ScreamTriggerRange, out _))
            return;

        b.NextScreamAt = now + TimeSpan.FromSeconds(b.ScreamCooldownSeconds);
        _screech.Screech(x.Uid, screech.Range, screech.Vfx, screech.ScreechSound, screech.Effects);
    }
}
