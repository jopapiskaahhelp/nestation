// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Map;

namespace Content.Shared._Nestation.SupportStrike;

[RegisterComponent]
public sealed partial class SupportRocketComponent : Component
{
    [DataField]
    public float FlightTime = 0.3f;

    [DataField]
    public float SpawnHeight = 20f;

    [DataField]
    public float ConcussionRange = 30f;

    [DataField]
    public float ConcussionMaxSeconds = 25f;

    [DataField]
    public float ConcussionMinSeconds = 3f;

    [DataField]
    public SoundSpecifier? FlightSound;

    public TimeSpan StartedAt;

    public Vector2 StartPos;

    public Vector2 Target;

    public MapId Map;

    public EntityUid? Launcher;

    public bool Impacted;
}
