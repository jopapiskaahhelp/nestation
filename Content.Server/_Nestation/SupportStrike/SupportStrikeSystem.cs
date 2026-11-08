// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Server._Nestation.XenoHive;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Light.Components;
using Content.Server.Light.EntitySystems;
using Content.Shared._Nestation.SupportStrike;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.SupportStrike;

public sealed partial class SupportStrikeSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ConcussionSystem _concussion = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private ExpendableLightSystem _expendable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    private static readonly ProtoId<NpcFactionPrototype> XenoFaction = "Xeno";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SupportFlareComponent, LandEvent>(OnFlareLand);
        SubscribeLocalEvent<SupportTabletComponent, BoundUIOpenedEvent>(OnTabletOpened);

        Subs.BuiEvents<SupportTabletComponent>(SupportTabletUiKey.Key, subs =>
        {
            subs.Event<SupportTabletLaunchMessage>(OnLaunch);
        });
    }

    private void OnFlareLand(Entity<SupportFlareComponent> ent, ref LandEvent args)
    {
        ent.Comp.Landed = true;
        ent.Comp.LandedAt = _timing.CurTime;

        if (TryComp<ExpendableLightComponent>(ent, out var light))
            _expendable.TryActivate(new Entity<ExpendableLightComponent>(ent.Owner, light));

        RefreshTablets();
    }

    private void OnTabletOpened(Entity<SupportTabletComponent> ent, ref BoundUIOpenedEvent args)
    {
        SendState(ent);
    }

    private void OnLaunch(Entity<SupportTabletComponent> ent, ref SupportTabletLaunchMessage args)
    {
        var user = args.Actor;
        var now = _timing.CurTime;

        if (now < ent.Comp.NextUse)
        {
            _popup.PopupEntity(Loc.GetString("support-tablet-popup-reload"), ent, user);
            return;
        }

        if (!TryFindTarget(Transform(user).MapID, out var flare, out var target))
        {
            _popup.PopupEntity(Loc.GetString("support-tablet-popup-no-signal"), ent, user);
            return;
        }

        ent.Comp.NextUse = now + TimeSpan.FromSeconds(ent.Comp.Cooldown);
        QueueDel(flare);
        LaunchRocket(ent.Comp.RocketProto, target, user);

        _popup.PopupEntity(Loc.GetString("support-tablet-popup-launched"), ent, user);
        RefreshTablets();
    }

    private bool TryFindTarget(MapId map, out EntityUid flare, out MapCoordinates coords)
    {
        flare = default;
        coords = default;

        var best = TimeSpan.MinValue;
        var found = false;
        var query = EntityQueryEnumerator<SupportFlareComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (!comp.Landed || xform.MapID != map)
                continue;

            if (!HasComp<MapGridComponent>(xform.ParentUid) && !HasComp<MapComponent>(xform.ParentUid))
                continue;

            if (comp.LandedAt < best)
                continue;

            best = comp.LandedAt;
            flare = uid;
            coords = _transform.GetMapCoordinates(uid, xform);
            found = true;
        }

        return found;
    }

    private void LaunchRocket(EntProtoId proto, MapCoordinates target, EntityUid launcher)
    {
        var rocket = Spawn(proto, target);
        var comp = EnsureComp<SupportRocketComponent>(rocket);
        var map = _maps.GetMapOrInvalid(target.MapId);

        _transform.SetCoordinates(rocket, new EntityCoordinates(map, target.Position + new Vector2(0f, comp.SpawnHeight)));

        comp.Target = target.Position;
        comp.Map = target.MapId;
        comp.Launcher = launcher;
        comp.ImpactAt = _timing.CurTime + TimeSpan.FromSeconds(comp.FlightTime);

        _physics.SetLinearVelocity(rocket, new Vector2(0f, -comp.SpawnHeight / comp.FlightTime));

        if (comp.FlightSound != null)
            _audio.PlayPvs(comp.FlightSound, rocket);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<SupportRocketComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Impacted || now < comp.ImpactAt)
                continue;

            comp.Impacted = true;
            Impact(uid, comp);
        }
    }

    private void Impact(EntityUid uid, SupportRocketComponent comp)
    {
        _transform.SetWorldPosition(uid, comp.Target);
        _physics.SetLinearVelocity(uid, Vector2.Zero);

        Concuss(new MapCoordinates(comp.Target, comp.Map), comp);
        _explosion.TriggerExplosive(uid, user: comp.Launcher);
    }

    private void Concuss(MapCoordinates center, SupportRocketComponent rocket)
    {
        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(center, rocket.ConcussionRange))
        {
            var uid = mob.Owner;

            if (!HasComp<ActorComponent>(uid) ||
                !_mobState.IsAlive(uid) ||
                HasComp<XenoHiveMemberComponent>(uid) ||
                _faction.IsMember(uid, XenoFaction))
            {
                continue;
            }

            var distance = (_transform.GetWorldPosition(uid) - center.Position).Length();
            var falloff = 1f - Math.Clamp(distance / rocket.ConcussionRange, 0f, 1f);

            if (falloff <= 0f)
                continue;

            var strength = MathF.Sqrt(falloff);
            var seconds = rocket.ConcussionMinSeconds + (rocket.ConcussionMaxSeconds - rocket.ConcussionMinSeconds) * falloff;

            _concussion.Apply(uid, strength, seconds);
        }
    }

    private void RefreshTablets()
    {
        var query = EntityQueryEnumerator<SupportTabletComponent, UserInterfaceComponent>();

        while (query.MoveNext(out var uid, out var comp, out _))
        {
            if (_ui.IsUiOpen(uid, SupportTabletUiKey.Key))
                SendState((uid, comp));
        }
    }

    private void SendState(Entity<SupportTabletComponent> ent)
    {
        var hasTarget = TryFindTarget(Transform(ent).MapID, out _, out _);
        _ui.SetUiState(ent.Owner, SupportTabletUiKey.Key, new SupportTabletBoundUserInterfaceState(ent.Comp.NextUse, hasTarget));
    }
}
