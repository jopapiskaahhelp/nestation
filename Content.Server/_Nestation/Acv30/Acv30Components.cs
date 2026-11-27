// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Nestation.Acv30;

public enum Acv30ZoneKind : byte
{
    Spawn,
    Route,
    Fire,
    Detonation,
}

[RegisterComponent]
public sealed partial class Acv30ZoneComponent : Component
{
    [DataField(required: true)]
    public Acv30ZoneKind Kind;

    [DataField]
    public string Group = "default";

    [DataField]
    public int Index;

    [DataField]
    public Vector2 Size = new(20f, 20f);

    [DataField]
    public bool AutoSpawn = true;

    [DataField]
    public float SpawnDelay;

    [DataField]
    public EntProtoId Vehicle = "Acv30Vehicle";

    [ViewVariables]
    public TimeSpan? NextSpawn;

    [ViewVariables]
    public EntityUid? Spawned;
}

[RegisterComponent]
public sealed partial class Acv30VehicleComponent : Component
{
    [DataField]
    public string Group = "default";

    [DataField]
    public float Speed = 1f;

    [DataField]
    public float TurnRate = 1.4f;

    [DataField]
    public float PivotAngle = 0.7f;

    [DataField]
    public float ReachDistance = 1.5f;

    [DataField]
    public Vector2 Hull = new(3f, 4f);

    [DataField]
    public float PushSpeed = 6f;

    [DataField]
    public float FrontPushSpeed = 7f;

    [DataField]
    public float FrontReach = 1.2f;

    [DataField]
    public float FrontDepth = 0.8f;

    [DataField]
    public float FrontMargin = 0.8f;

    [DataField]
    public float CrushMargin = 0.3f;

    [DataField]
    public bool CrushStructures = true;

    [DataField]
    public float FireRate = 8f;

    [DataField]
    public EntProtoId Projectile = "BulletLightRifle";

    [DataField]
    public float ProjectileSpeed = 25f;

    [DataField]
    public float MuzzleOffset = 2.1f;

    [DataField]
    public float SpreadDegrees = 1.5f;

    [DataField]
    public SoundSpecifier? ShootSound;

    [DataField]
    public SoundSpecifier? CrushSound;

    [DataField]
    public SoundSpecifier? DetonationSound;

    [DataField]
    public EntProtoId Wreck = "Acv30Wreck";

    [DataField]
    public string ExplosionType = "Default";

    [DataField]
    public float ExplosionIntensity = 40f;

    [DataField]
    public float ExplosionSlope = 6f;

    [DataField]
    public float ExplosionMaxTileIntensity = 8f;

    [ViewVariables]
    public List<EntityUid>? Route;

    [ViewVariables]
    public int Waypoint;

    [ViewVariables]
    public TimeSpan NextShot;

    [ViewVariables]
    public TimeSpan NextCrushSound;
}

[DataDefinition]
public sealed partial class Acv30EffectSlot
{
    [DataField(required: true)]
    public EntProtoId Proto;

    [DataField]
    public Vector2 Offset;
}

[RegisterComponent]
public sealed partial class Acv30EffectComponent : Component;

[RegisterComponent]
public sealed partial class Acv30WreckComponent : Component
{
    [DataField]
    public float FlickerInterval = 0.09f;

    [DataField]
    public float EnergyMin = 2.2f;

    [DataField]
    public float EnergyMax = 4.8f;

    [DataField]
    public Vector2 Hull = new(3f, 4f);

    [DataField]
    public float IgniteMargin = 0.6f;

    [DataField]
    public float IgniteInterval = 1f;

    [DataField]
    public float IgniteStacks = 0.6f;

    [DataField]
    public List<Acv30EffectSlot> Effects = new();

    [DataField]
    public EntProtoId? FireAmbience = "Acv30FireAmbience";

    [DataField]
    public SoundSpecifier? ScreamSound;

    [DataField]
    public float ScreamRange = 5.5f;

    [DataField]
    public float ScreamRolloff = 2f;

    [DataField]
    public float ScreamVolume;

    [ViewVariables]
    public bool Started;

    [ViewVariables]
    public List<EntityUid> Spawned = new();

    [ViewVariables]
    public TimeSpan NextFlicker;

    [ViewVariables]
    public TimeSpan NextIgnite;
}
