// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.Marines;

public sealed partial class MarineEmplacementSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    private static readonly Vector2[] Neighbors =
    {
        new(-1, -1), new(0, -1), new(1, -1),
        new(-1, 0), new(1, 0),
        new(-1, 1), new(0, 1), new(1, 1),
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MarineEmplacementComponent, MapInitEvent>(OnInit);
    }

    private void OnInit(Entity<MarineEmplacementComponent> ent, ref MapInitEvent args)
    {
        var xform = Transform(ent);
        var rotation = xform.LocalRotation;
        var front = new Vector2(0, -1);

        foreach (var offset in Neighbors)
        {
            if (ent.Comp.Bunker && offset == front)
                continue;

            var piece = Spawn(ent.Comp.Piece, xform.Coordinates.Offset(rotation.RotateVec(offset)));
            ent.Comp.Pieces.Add(piece);
        }

        var gunner = Spawn(ent.Comp.Gunner, xform.Coordinates);
        _transform.SetLocalRotation(gunner, rotation);
        ent.Comp.GunnerUid = gunner;

        if (TryComp<MarineComponent>(gunner, out var marine))
            marine.Emplacement = ent;

        if (!ent.Comp.Bunker)
            return;

        var seat = Spawn(ent.Comp.Seat, xform.Coordinates.Offset(rotation.RotateVec(front)));
        ent.Comp.SeatUid = seat;

        if (TryComp<MarineGunSeatComponent>(seat, out var seatComp))
            seatComp.Controller = ent;
    }

    public void GunnerDied(EntityUid controller)
    {
        if (!TryComp<MarineEmplacementComponent>(controller, out var comp) || comp.Destroyed)
            return;

        comp.Destroyed = true;

        foreach (var piece in comp.Pieces)
        {
            if (!Exists(piece) || !HasComp<MarineSandbagComponent>(piece))
                continue;

            if (TryComp<FixturesComponent>(piece, out var fixtures)
                && fixtures.Fixtures.TryGetValue("cover", out var cover))
            {
                _physics.SetHard(piece, cover, true, fixtures);
            }
        }

        if (comp.SeatUid is { } seat && TryComp<MarineGunSeatComponent>(seat, out var seatComp))
        {
            seatComp.Active = false;
            ReleaseAll(seatComp);
        }
    }

    private void ReleaseAll(MarineGunSeatComponent seat)
    {
        foreach (var gun in seat.Holders.Values)
        {
            if (Exists(gun))
                QueueDel(gun);
        }

        seat.Holders.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var seats = EntityQueryEnumerator<MarineGunSeatComponent, TransformComponent>();

        while (seats.MoveNext(out var uid, out var seat, out var xform))
        {
            if (now < seat.NextCheck || !seat.Active)
                continue;

            seat.NextCheck = now + TimeSpan.FromSeconds(0.3);
            var pos = _transform.GetWorldPosition(xform);

            var drop = new List<EntityUid>();
            foreach (var (user, gun) in seat.Holders)
            {
                if (!Exists(user) || _mobState.IsIncapacitated(user)
                    || (_transform.GetWorldPosition(user) - pos).Length() > seat.Radius + 0.4f)
                {
                    drop.Add(user);
                }
            }

            foreach (var user in drop)
            {
                if (seat.Holders.Remove(user, out var gun) && Exists(gun))
                    QueueDel(gun);
            }

            var coords = new MapCoordinates(pos, xform.MapID);
            foreach (var (user, _) in _lookup.GetEntitiesInRange<ActorComponent>(coords, seat.Radius, LookupFlags.Dynamic | LookupFlags.Uncontained))
            {
                if (seat.Holders.ContainsKey(user) || HasComp<MarineComponent>(user)
                    || !HasComp<HandsComponent>(user) || _mobState.IsIncapacitated(user))
                {
                    continue;
                }

                var gun = Spawn(seat.Gun, Transform(user).Coordinates);
                if (!_hands.TryForcePickupAnyHand(user, gun, checkActionBlocker: false))
                {
                    QueueDel(gun);
                    continue;
                }

                seat.Holders[user] = gun;
            }
        }
    }
}
