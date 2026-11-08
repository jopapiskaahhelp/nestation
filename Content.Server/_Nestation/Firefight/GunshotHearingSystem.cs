// Nestation New City CC BY-NC 3.0
using Content.Server._Nestation.Unbalanced;
using Content.Shared._Nestation.Firefight;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.Firefight;

public sealed partial class GunshotHearingSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private BlindableSystem _blindable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private HearingProtectionSystem _protection = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedRoofSystem _roof = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const float CartridgeDose = 0.2f;
    private const float ShellDoseMultiplier = 5f;
    private const float ListenerDoseMultiplier = 0.5f;
    private const float ListenRange = 6f;
    private const float Threshold = 1f;
    private const float DoseAfterRinging = 0.6f;
    private const float DoseDecay = 0.25f;
    private const float RingSeconds = 5f;
    private const float ShooterStrength = 1f;
    private const float ListenerStrength = 0.6f;
    private const int MaxBlur = 3;
    private const float BlurRampSeconds = 1.2f;
    private const float BlurDecaySeconds = 2f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HandsComponent, SelfBeforeGunShotEvent>(OnBeforeShot);
    }

    private void OnBeforeShot(Entity<HandsComponent> ent, ref SelfBeforeGunShotEvent args)
    {
        var dose = 0f;
        var spreadName = _factory.GetComponentName<ProjectileSpreadComponent>();

        foreach (var (_, shootable) in args.Ammo)
        {
            if (shootable is not CartridgeAmmoComponent cartridge)
                continue;

            var shell = _proto.TryIndex<EntityPrototype>(cartridge.Prototype, out var proto) &&
                        proto.Components.ContainsKey(spreadName);

            dose += shell ? CartridgeDose * ShellDoseMultiplier : CartridgeDose;
        }

        if (dose <= 0f || !IsRoofed(args.Shooter))
            return;

        var shooterPos = _transform.GetWorldPosition(args.Shooter);

        if (HasComp<ActorComponent>(args.Shooter) &&
            !HasComp<UnbalancedComponent>(args.Shooter) &&
            _mobState.IsAlive(args.Shooter))
            AddDose(args.Shooter, dose, ShooterStrength);

        foreach (var (uid, _) in _lookup.GetEntitiesInRange<MobStateComponent>(_transform.GetMapCoordinates(args.Shooter, Transform(args.Shooter)), ListenRange))
        {
            if (uid == args.Shooter ||
                !HasComp<ActorComponent>(uid) ||
                HasComp<UnbalancedComponent>(uid) ||
                !_mobState.IsAlive(uid) ||
                !IsRoofed(uid))
            {
                continue;
            }

            var distance = (_transform.GetWorldPosition(uid) - shooterPos).Length();
            var falloff = 1f - Math.Clamp(distance / ListenRange, 0f, 1f);

            if (falloff <= 0f)
                continue;

            AddDose(uid, dose * ListenerDoseMultiplier * falloff, ListenerStrength);
        }
    }

    private void AddDose(EntityUid uid, float amount, float strength)
    {
        if (_protection.IsProtected(uid))
            return;

        var comp = EnsureComp<EarRingingComponent>(uid);
        var now = _timing.CurTime;
        var elapsed = (float) (now - comp.DoseUpdated).TotalSeconds;

        comp.Dose = MathF.Max(0f, comp.Dose - DoseDecay * elapsed) + amount;
        comp.DoseUpdated = now;

        if (comp.Dose < Threshold)
            return;

        comp.Dose = DoseAfterRinging;

        var active = comp.Until > now;
        comp.Strength = active ? MathF.Max(comp.Strength, strength) : strength;

        var target = now + TimeSpan.FromSeconds(RingSeconds);

        if (comp.Until < target)
            comp.Until = target;

        Dirty(uid, comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<EarRingingComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextBlurStep)
                continue;

            var ringing = comp.Until > now;

            if (ringing)
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
        }
    }

    private bool IsRoofed(EntityUid uid)
    {
        var xform = Transform(uid);

        if (xform.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid) ||
            !TryComp<RoofComponent>(gridUid, out var roof))
        {
            return false;
        }

        var tile = _maps.WorldToTile(gridUid, grid, _transform.GetWorldPosition(xform));
        return _roof.IsRooved((gridUid, grid, roof), tile);
    }
}
