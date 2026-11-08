// Nestation New City CC BY-NC 3.0
using Content.Shared._Nestation.SupportStrike;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using AudioComponent = Robust.Shared.Audio.Components.AudioComponent;

namespace Content.Client._Nestation.SupportStrike;

public sealed partial class ConcussionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private static readonly SoundSpecifier RingSound = new SoundPathSpecifier("/Audio/_Nestation/Firefight/ear_ringing.ogg");

    private const float FadeSeconds = 2.5f;
    private const float RingVolumeMin = -4f;
    private const float RingVolumeMax = 0f;

    private EntityUid? _ringStream;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var strength = GetStrength();

        if (strength <= 0f)
        {
            if (_ringStream is { } stream)
                _audio.Stop(stream);

            _ringStream = null;
            return;
        }

        var volume = RingVolumeMin + (RingVolumeMax - RingVolumeMin) * strength;

        if (_ringStream is { } current && TryComp(current, out AudioComponent? audio))
        {
            _audio.SetVolume(current, volume, audio);
            return;
        }

        _ringStream = null;

        if (_audio.PlayGlobal(RingSound, Filter.Local(), false, AudioParams.Default.WithVolume(volume)) is { } played)
            _ringStream = played.Entity;
    }

    private float GetStrength()
    {
        if (_player.LocalEntity is not { } local ||
            !TryComp(local, out ConcussionComponent? concussion))
        {
            return 0f;
        }

        var remaining = (float) (concussion.Until - _timing.CurTime).TotalSeconds;

        if (remaining <= 0f)
            return 0f;

        return concussion.Strength * MathF.Min(1f, remaining / FadeSeconds);
    }
}
