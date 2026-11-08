// Nestation new city CC BY-NC 3.0
using System.Numerics;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Nestation.XenoTurret;

[RegisterComponent]
public sealed partial class XenoTurretComponent : Component
{
    [DataField]
    public float Range = 4f;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(5);

    [DataField]
    public TimeSpan ScanInterval = TimeSpan.FromSeconds(0.25);

    [DataField]
    public EntProtoId Projectile = "BulletXenoTurretToxin";

    [DataField]
    public float ProjectileSpeed = 30f;

    [DataField]
    public float MuzzleOffset = 0.6f;

    [DataField]
    public SoundSpecifier? ShootSound;

    [DataField]
    public EntProtoId BurstLight = "XenoTurretBurstLight";

    [DataField]
    public SoundSpecifier? BurstSound;

    [DataField]
    public SoundSpecifier? LimbTearSound;

    [DataField]
    public List<SoundSpecifier> DefaultGibSounds = new();

    [DataField]
    public Dictionary<ProtoId<SpeciesPrototype>, List<SoundSpecifier>> GibSounds = new();

    [DataField]
    public float LimbSpeedMin = 8f;

    [DataField]
    public float LimbSpeedMax = 15f;

    [DataField]
    public float LimbSpread = 0.85f;

    [DataField]
    public float LimbSpin = 16f;

    [DataField]
    public float LimbDrag = 3.4f;

    [DataField]
    public float LimbMinSpeed = 0.7f;

    [DataField]
    public float TorsoSpeed = 4f;

    [DataField]
    public float TrailStep = 0.3f;

    [DataField]
    public int SplatCount = 3;

    [DataField]
    public int SprayCount = 16;

    [DataField]
    public int DotCount = 12;

    [DataField]
    public float SprayReach = 4.4f;

    [DataField]
    public float SprayArc = 0.55f;

    [DataField]
    public float DecalLifetime = 300f;

    [ViewVariables]
    public TimeSpan NextFire;

    [ViewVariables]
    public TimeSpan NextScan;
}

[RegisterComponent]
public sealed partial class XenoTurretShotComponent : Component
{
    [ViewVariables]
    public EntityUid? Turret;

    [ViewVariables]
    public Vector2 Direction;
}
