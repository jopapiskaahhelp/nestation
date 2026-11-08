// Nestation New City CC BY-NC 3.0
using Content.Server._Nestation.Firefight;
using Content.Shared._Nestation.SupportStrike;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Eye.Blinding.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.SupportStrike;

public sealed partial class ConcussionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private BlindableSystem _blindable = default!;
    [Dependency] private HearingProtectionSystem _protection = default!;

    private const int MaxBlur = 3;
    private const float BlurRampSeconds = 1.2f;
    private const float BlurDecaySeconds = 2f;

    public void Apply(EntityUid uid, float strength, float seconds)
    {
        if (_protection.IsProtected(uid))
            return;

        var now = _timing.CurTime;
        var comp = EnsureComp<ConcussionComponent>(uid);
        var until = now + TimeSpan.FromSeconds(seconds);

        comp.Strength = comp.Until > now ? MathF.Max(comp.Strength, strength) : strength;

        if (comp.Until < until)
            comp.Until = until;

        Dirty(uid, comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ConcussionComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextBlurStep)
                continue;

            if (comp.Until > now)
            {
                var target = Math.Max(1, (int) MathF.Round(MaxBlur * comp.Strength));

                if (comp.BlurApplied >= target ||
                    !TryComp<BlindableComponent>(uid, out var blindable) ||
                    blindable.EyeDamage + 1 >= blindable.MaxDamage)
                {
                    continue;
                }

                _blindable.AdjustEyeDamage((uid, blindable), 1);
                comp.BlurApplied++;
                comp.NextBlurStep = now + TimeSpan.FromSeconds(BlurRampSeconds);
            }
            else if (comp.BlurApplied > 0)
            {
                if (TryComp<BlindableComponent>(uid, out var blindable))
                    _blindable.AdjustEyeDamage((uid, blindable), -1);

                comp.BlurApplied--;
                comp.NextBlurStep = now + TimeSpan.FromSeconds(BlurDecaySeconds);
            }
            else
            {
                RemCompDeferred<ConcussionComponent>(uid);
            }
        }
    }
}
