// Nestation CC BY-NC 3.0
using Content.Shared._Nestation.Reactor;
using Robust.Shared.GameObjects;

namespace Content.Server._Nestation.Reactor;

public sealed class NestationPlantContext
{
    public EntityUid CoreUid;
    public NestationReactorCoreComponent Core = default!;

    public EntityUid? SteamGeneratorUid;
    public EntityUid? PrimaryPumpUid;
    public EntityUid? TurbineUid;
    public EntityUid? GeneratorUid;
    public EntityUid? CondenserUid;
    public EntityUid? FeedPumpUid;
    public EntityUid? CirculationPumpUid;
    public EntityUid? PoolSeed;

    public NestationSteamGeneratorComponent? Sg;
    public NestationPumpComponent? Primary;
    public NestationPumpComponent? Feed;
    public NestationPumpComponent? Circulation;
    public NestationTurbineComponent? Turbine;
    public NestationGeneratorComponent? Gen;
    public NestationCondenserComponent? Cond;

    public bool PrimaryClosed;
    public bool SecondaryClosed;
    public bool CoolingClosed;
    public List<EntityUid> Pool = new();

    public float PhiPrimary;
    public float PhiFeed;
    public float PoolTemp = NestationTuning.Ambient;
    public float PoolLevel;
}
