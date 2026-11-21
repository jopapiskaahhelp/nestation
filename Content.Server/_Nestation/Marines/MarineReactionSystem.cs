// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.Marines;

public sealed partial class MarineReactionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedIdCardSystem _idCards = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private ChatSystem _chat = default!;

    private sealed class GunWatch
    {
        public TimeSpan Deadline;
        public TimeSpan Compliant;
        public bool Warned;
    }

    private readonly Dictionary<EntityUid, TimeSpan> _hostile = new();
    private readonly Dictionary<EntityUid, (bool South, TimeSpan Until)> _south = new();
    private readonly Dictionary<EntityUid, GunWatch> _watch = new();
    private readonly Dictionary<EntityUid, TimeSpan> _pulling = new();
    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());
        SubscribeLocalEvent<MarineComponent, EntRemovedFromContainerMessage>(OnItemRemoved);
    }

    private void Reset()
    {
        _hostile.Clear();
        _south.Clear();
        _watch.Clear();
        _pulling.Clear();
    }

    public bool IsMarkedHostile(EntityUid player)
    {
        return _hostile.TryGetValue(player, out var until) && until > _timing.CurTime;
    }

    public bool IsPlayerEnemy(EntityUid player)
    {
        var now = _timing.CurTime;

        if (_hostile.TryGetValue(player, out var until) && until > now)
            return true;

        return IsSouth(player, now);
    }

    private bool IsSouth(EntityUid player, TimeSpan now)
    {
        if (_south.TryGetValue(player, out var cached) && cached.Until > now)
            return cached.South;

        var south = TryComp<MarineComponent>(player, out var marine) && marine.Team == "south";

        if (!south && _idCards.TryFindIdCard(player, out var card))
            south = MetaData(card.Owner).EntityPrototype?.ID.StartsWith("Marine") == true;

        _south[player] = (south, now + TimeSpan.FromSeconds(3));
        return south;
    }

    public void MarkHostile(EntityUid player, TimeSpan now, float seconds)
    {
        _hostile[player] = now + TimeSpan.FromSeconds(seconds);

        var pos = _transform.GetWorldPosition(player);
        var coords = _transform.GetMapCoordinates(player);

        foreach (var (other, comp) in _lookup.GetEntitiesInRange<MarineComponent>(coords, 30f, LookupFlags.Dynamic | LookupFlags.Uncontained))
        {
            if (comp.Team != "north" || comp.Passive || HasComp<ActorComponent>(other) || _mobState.IsDead(other))
                continue;

            comp.HeardPoint = pos;
            comp.HeardUntil = now + TimeSpan.FromSeconds(12);
        }
    }

    public void OnAggression(EntityUid origin, EntityUid victim, TimeSpan now)
    {
        if (origin == victim || !HasComp<ActorComponent>(origin) || HasComp<ActorComponent>(victim))
            return;

        if (!TryComp<MarineComponent>(victim, out var victimComp))
            return;

        if (victimComp.Team == "north" || victimComp.Passive)
            MarkHostile(origin, now, 120f);

        var pos = _transform.GetWorldPosition(origin);
        var coords = _transform.GetMapCoordinates(victim);

        foreach (var (other, comp) in _lookup.GetEntitiesInRange<MarineComponent>(coords, 12f, LookupFlags.Dynamic | LookupFlags.Uncontained))
        {
            if (!comp.Passive || comp.Static || HasComp<ActorComponent>(other))
                continue;

            comp.PanicUntil = now + TimeSpan.FromSeconds(8);
            comp.PanicFrom = pos;
        }
    }

    private void OnItemRemoved(Entity<MarineComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        var now = _timing.CurTime;

        if (now < ent.Comp.IgnoreRemovalUntil || _mobState.IsDead(ent) || HasComp<ActorComponent>(ent))
            return;

        var coords = _transform.GetMapCoordinates(ent);
        EntityUid? thief = null;

        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var player, out _, out var state, out var xform))
        {
            if (player == ent.Owner || state.CurrentState != MobState.Alive || xform.MapID != coords.MapId)
                continue;

            if ((_transform.GetWorldPosition(xform) - coords.Position).Length() <= 1.8f)
            {
                thief = player;
                break;
            }
        }

        if (thief is not { } culprit)
            return;

        Say(ent, ent.Comp.Passive ? "Вор! Держите вора!" : "Руки прочь!");
        OnAggression(culprit, ent, now);
        MarkHostile(culprit, now, 120f);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + TimeSpan.FromSeconds(0.4);

        var expired = new List<EntityUid>();
        foreach (var (player, until) in _hostile)
        {
            if (until <= now || !Exists(player))
                expired.Add(player);
        }

        foreach (var player in expired)
            _hostile.Remove(player);

        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var player, out _, out var state, out var xform))
        {
            if (state.CurrentState != MobState.Alive)
                continue;

            CheckPlayer(player, xform, now);
        }
    }

    private void CheckPlayer(EntityUid player, TransformComponent xform, TimeSpan now)
    {
        var pos = _transform.GetWorldPosition(xform);
        var coords = new MapCoordinates(pos, xform.MapID);

        EntityUid? soldier = null;
        var soldierDist = float.MaxValue;
        var civilians = new List<MarineComponent>();

        foreach (var (obs, comp) in _lookup.GetEntitiesInRange<MarineComponent>(coords, 12f, LookupFlags.Dynamic | LookupFlags.Uncontained))
        {
            if (obs == player || HasComp<ActorComponent>(obs) || _mobState.IsDead(obs))
                continue;

            var obsPos = _transform.GetWorldPosition(obs);
            var dist = (obsPos - pos).Length();

            if (!HasSight(xform.MapID, pos, obsPos, player))
                continue;

            if (comp.Team == "north" && !comp.Passive && comp.Scene == null)
            {
                if (dist < soldierDist)
                {
                    soldier = obs;
                    soldierDist = dist;
                }
            }
            else if (comp.Passive && !comp.Static)
            {
                civilians.Add(comp);
            }
        }

        var armed = HoldsGun(player);

        if (!_watch.TryGetValue(player, out var watch))
        {
            watch = new GunWatch();
            _watch[player] = watch;
        }

        if (armed)
        {
            foreach (var civilian in civilians)
            {
                if (now >= civilian.PanicUntil)
                {
                    civilian.PanicUntil = now + TimeSpan.FromSeconds(6);
                    civilian.PanicFrom = pos;
                }
            }

            if (soldier is { } guard)
            {
                if (!watch.Warned && now < watch.Compliant)
                {
                    Say(guard, "Я предупреждал! Огонь!");
                    MarkHostile(player, now, 120f);
                }
                else if (!watch.Warned)
                {
                    Say(guard, "Убери оружие! Живо!");
                    watch.Warned = true;
                    watch.Deadline = now + TimeSpan.FromSeconds(3);
                }
                else if (now >= watch.Deadline)
                {
                    Say(guard, "Нарушитель! Огонь!");
                    watch.Warned = false;
                    MarkHostile(player, now, 120f);
                }
            }
        }
        else if (watch.Warned)
        {
            watch.Warned = false;
            watch.Compliant = now + TimeSpan.FromSeconds(10);
        }

        CheckPulling(player, soldier, now);
    }

    private void CheckPulling(EntityUid player, EntityUid? soldier, TimeSpan now)
    {
        if (!TryComp<PullerComponent>(player, out var puller) || puller.Pulling is not { } pulled
            || !TryComp<MarineComponent>(pulled, out var pulledComp) || HasComp<ActorComponent>(pulled))
        {
            _pulling.Remove(player);
            return;
        }

        if (!_pulling.TryGetValue(player, out var since))
        {
            _pulling[player] = now;
            Say(pulled, pulledComp.Passive ? "Отпустите меня!" : "Руки убрал!");
            return;
        }

        if (now - since < TimeSpan.FromSeconds(3))
            return;

        _pulling.Remove(player);
        Say(soldier ?? pulled, "Нападение! Огонь!");
        OnAggression(player, pulled, now);
        MarkHostile(player, now, 120f);
    }

    private bool HoldsGun(EntityUid player)
    {
        foreach (var held in _hands.EnumerateHeld(player))
        {
            if (HasComp<GunComponent>(held))
                return true;
        }

        return false;
    }

    private bool HasSight(MapId map, Vector2 from, Vector2 to, EntityUid ignore)
    {
        var delta = to - from;
        var dist = delta.Length();

        if (dist < 1f)
            return true;

        var ray = new CollisionRay(from, delta / dist, (int) CollisionGroup.BulletImpassable);

        foreach (var result in _physics.IntersectRay(map, ray, dist, ignore, false))
        {
            if (HasComp<MobStateComponent>(result.HitEntity))
                continue;

            if (result.Distance < dist - 0.6f)
                return false;
        }

        return true;
    }

    private void Say(EntityUid uid, string text)
    {
        if (Exists(uid) && !_mobState.IsDead(uid))
            _chat.TrySendInGameICMessage(uid, text, InGameICChatType.Speak, false);
    }
}
