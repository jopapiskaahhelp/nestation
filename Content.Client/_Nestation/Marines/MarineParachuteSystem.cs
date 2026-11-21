// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Shared._Nestation.Marines;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;
using Robust.Shared.GameStates;
using Robust.Shared.Timing;

namespace Content.Client._Nestation.Marines;

public sealed partial class MarineParachuteSystem : EntitySystem
{
    [Dependency] private AnimationPlayerSystem _player = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const string AnimationKey = "marine-parachute";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MarineParachuteComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnState(Entity<MarineParachuteComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!HasComp<SpriteComponent>(ent))
            return;

        var elapsed = (float) (_timing.CurTime - ent.Comp.Start).TotalSeconds;
        var remaining = ent.Comp.Duration - elapsed;

        if (remaining <= 0.05f)
            return;

        var from = ent.Comp.EndOffset + new Vector2(0f, ent.Comp.Height * (remaining / ent.Comp.Duration));

        var animation = new Animation
        {
            Length = TimeSpan.FromSeconds(remaining),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(from, 0f),
                        new AnimationTrackProperty.KeyFrame(ent.Comp.EndOffset, remaining),
                    },
                },
            },
        };

        var player = EnsureComp<AnimationPlayerComponent>(ent);

        if (_player.HasRunningAnimation(ent, AnimationKey))
            _player.Stop(ent.Owner, player, AnimationKey);

        _player.Play(ent.Owner, animation, AnimationKey);
    }
}
