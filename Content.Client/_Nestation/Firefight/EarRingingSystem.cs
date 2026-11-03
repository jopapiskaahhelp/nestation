// Nestation New City CC BY-NC 3.0
using Content.Shared._Nestation.Firefight;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using AudioComponent = Robust.Shared.Audio.Components.AudioComponent;

namespace Content.Client._Nestation.Firefight;

public sealed partial class EarRingingSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private static readonly SoundSpecifier RingSound = new SoundPathSpecifier("/Audio/_Nestation/Firefight/ear_ringing.ogg");

    private const float MaxDip = 14f;
    private const float FadeSeconds = 2.5f;
    private const float RingVolumeMin = -4f;
    private const float RingVolumeMax = 0f;
    private const float VolumeEpsilon = 0.01f;

    private readonly Dictionary<EntityUid, (float Baseline, float Applied)> _tracked = new();
    private readonly List<EntityUid> _stale = new();
    private EntityUid? _ringStream;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var strength = GetStrength();

        if (strength <= 0f)
        {
            if (_ringStream != null || _tracked.Count > 0)
                Clear();

            return;
        }

        UpdateRing(strength);
        UpdateMuffle(strength);
    }

    private float GetStrength()
    {
        if (_player.LocalEntity is not { } local ||
            !TryComp(local, out EarRingingComponent? ring))
        {
            return 0f;
        }

        var remaining = (float) (ring.Until - _timing.CurTime).TotalSeconds;

        if (remaining <= 0f)
            return 0f;

        return ring.Strength * MathF.Min(1f, remaining / FadeSeconds);
    }

    private void UpdateRing(float strength)
    {
        var volume = RingVolumeMin + (RingVolumeMax - RingVolumeMin) * strength;

        if (_ringStream is { } stream && TryComp(stream, out AudioComponent? audio))
        {
            _audio.SetVolume(stream, volume, audio);
            return;
        }

        _ringStream = null;

        if (_audio.PlayGlobal(RingSound, Filter.Local(), false, AudioParams.Default.WithVolume(volume)) is { } played)
            _ringStream = played.Entity;
    }

    private void UpdateMuffle(float strength)
    {
        var dip = MaxDip * strength;

        _stale.Clear();

        foreach (var uid in _tracked.Keys)
        {
            if (!HasComp<AudioComponent>(uid))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            _tracked.Remove(uid);
        }

        var query = AllEntityQuery<AudioComponent>();

        while (query.MoveNext(out var uid, out var audio))
        {
            if (uid == _ringStream)
                continue;

            var baseline = audio.Volume;

            if (_tracked.TryGetValue(uid, out var state) &&
                MathF.Abs(audio.Volume - state.Applied) <= VolumeEpsilon)
            {
                baseline = state.Baseline;
            }

            var applied = baseline - dip;

            if (MathF.Abs(audio.Volume - applied) > VolumeEpsilon)
                _audio.SetVolume(uid, applied, audio);

            _tracked[uid] = (baseline, applied);
        }
    }

    private void Clear()
    {
        foreach (var (uid, (baseline, applied)) in _tracked)
        {
            if (!TryComp(uid, out AudioComponent? audio) ||
                MathF.Abs(audio.Volume - applied) > VolumeEpsilon)
            {
                continue;
            }

            _audio.SetVolume(uid, baseline, audio);
        }

        _tracked.Clear();

        if (_ringStream is { } stream)
            _audio.Stop(stream);

        _ringStream = null;
    }
}
