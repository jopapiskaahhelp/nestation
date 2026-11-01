// Nestation New City CC BY-NC 3.0
using Content.Server._Nestation.Unbalanced;
using Content.Shared.Camera;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Standing;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.Firefight;

public sealed partial class FirefightSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private const float MoveDeadzone = 0.4f;
    private const float MoveDegreesPerSpeed = 1.6f;
    private const float MoveDegreesMax = 8f;

    private const float StillSpeed = 0.35f;
    private const float SteadyAfter = 1f;
    private const float StillTimeCap = 10f;
    private const float SteadyMultiplier = 0.6f;
    private const float ProneMultiplier = 0.65f;

    private const float SuppressionPerBullet = 0.14f;
    private const float SuppressionCooldown = 0.08f;
    private const float SuppressionDecay = 0.3f;
    private const float SuppressionDegrees = 6f;
    private const float SuppressionBreaksSteady = 0.5f;
    private const float SuppressionNoticeAt = 0.5f;
    private const float SuppressionNoticeCooldown = 10f;
    private const float SuppressionKick = 0.07f;

    private const float FlinchMinDamage = 5f;
    private const float FlinchDamageScale = 40f;
    private const float FlinchMin = 0.2f;
    private const float FlinchMax = 0.7f;
    private const float FlinchDecay = 0.8f;
    private const float FlinchDegrees = 7f;
    private const float FlinchKickBase = 0.12f;
    private const float FlinchKickScale = 0.2f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FlyBySoundComponent, StartCollideEvent>(OnFlyBy);
        SubscribeLocalEvent<HandsComponent, DamageDealtEvent>(OnDamageDealt);
    }

    private void OnFlyBy(Entity<FlyBySoundComponent> ent, ref StartCollideEvent args)
    {
        if (args.OurFixtureId != SharedFlyBySoundSystem.FlyByFixture)
            return;

        var target = args.OtherEntity;

        if (!HasComp<HandsComponent>(target) ||
            HasComp<UnbalancedComponent>(target) ||
            !_mobState.IsAlive(target))
        {
            return;
        }

        if (TryComp<ProjectileComponent>(ent, out var projectile) && projectile.Shooter == target)
            return;

        var comp = EnsureState(target);
        var now = _timing.CurTime;

        if (now - comp.LastSuppressed < TimeSpan.FromSeconds(SuppressionCooldown))
            return;

        comp.LastSuppressed = now;
        comp.Suppression = MathF.Min(1f, comp.Suppression + SuppressionPerBullet);

        if (!HasComp<ActorComponent>(target))
            return;

        _recoil.KickCamera(target, _random.NextAngle().ToVec() * SuppressionKick);

        if (comp.Suppression >= SuppressionNoticeAt &&
            now - comp.LastNotice > TimeSpan.FromSeconds(SuppressionNoticeCooldown))
        {
            comp.LastNotice = now;
            _popup.PopupEntity(Loc.GetString("firefight-suppressed"), target, target);
        }
    }

    private void OnDamageDealt(Entity<HandsComponent> ent, ref DamageDealtEvent args)
    {
        if (HasComp<UnbalancedComponent>(ent) || !_mobState.IsAlive(ent))
            return;

        var total = args.Damage.GetTotal().Float();

        if (total < FlinchMinDamage)
            return;

        var comp = EnsureState(ent);
        comp.Flinch = MathF.Min(1f, comp.Flinch + Math.Clamp(total / FlinchDamageScale, FlinchMin, FlinchMax));

        if (HasComp<ActorComponent>(ent))
            _recoil.KickCamera(ent, _random.NextAngle().ToVec() * (FlinchKickBase + comp.Flinch * FlinchKickScale));
    }

    public Angle ModifyCone(EntityUid user, Angle cone)
    {
        if (!HasComp<HandsComponent>(user) || HasComp<UnbalancedComponent>(user))
            return cone;

        var comp = EnsureState(user);
        var speed = TryComp<PhysicsComponent>(user, out var body) ? body.LinearVelocity.Length() : 0f;

        var addDegrees = comp.Suppression * SuppressionDegrees + comp.Flinch * FlinchDegrees;

        if (speed > MoveDeadzone)
            addDegrees += MathF.Min((speed - MoveDeadzone) * MoveDegreesPerSpeed, MoveDegreesMax);

        var multiplier = 1f;

        if (speed <= StillSpeed &&
            comp.StillTime >= SteadyAfter &&
            comp.Suppression < SuppressionBreaksSteady)
        {
            multiplier *= SteadyMultiplier;
        }

        if (_standing.IsDown(user))
            multiplier *= ProneMultiplier;

        return new Angle(Math.Max(0d, cone.Theta * multiplier + Angle.FromDegrees(addDegrees).Theta));
    }

    private FirefightComponent EnsureState(EntityUid uid)
    {
        if (TryComp(uid, out FirefightComponent? comp))
            return comp;

        comp = AddComp<FirefightComponent>(uid);
        comp.StillTime = SteadyAfter;
        return comp;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<FirefightComponent, PhysicsComponent>();

        while (query.MoveNext(out _, out var comp, out var body))
        {
            if (body.LinearVelocity.Length() > StillSpeed)
                comp.StillTime = 0f;
            else
                comp.StillTime = MathF.Min(comp.StillTime + frameTime, StillTimeCap);

            if (comp.Suppression > 0f)
                comp.Suppression = MathF.Max(0f, comp.Suppression - SuppressionDecay * frameTime);

            if (comp.Flinch > 0f)
                comp.Flinch = MathF.Max(0f, comp.Flinch - FlinchDecay * frameTime);
        }
    }
}
