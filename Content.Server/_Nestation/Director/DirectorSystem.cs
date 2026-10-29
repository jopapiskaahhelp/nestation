// Nestation new city CC BY-NC 3.0
using System.Numerics;
using Content.Server.Decals;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server._Nestation.Director;

public sealed partial class DirectorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private HTNSystem _htn = default!;

    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan GlobalCooldown = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MarkerCooldown = TimeSpan.FromMinutes(40);
    private static readonly string[] StripSlots = { "back", "pocket1", "pocket2" };
    private static readonly Color BloodColor = Color.FromHex("#5c0000");
    private const CollisionGroup BlockMask = CollisionGroup.MobMask | CollisionGroup.Impassable;
    private const int EventCount = 5;
    private const int SecurityCorpseEvent = 1;
    private const string FollowCompound = "DirectorKittenCompound";

    private TimeSpan _nextScan;
    private TimeSpan _nextAllowed;
    private readonly List<(EntityUid Uid, MapId Map, Vector2 Pos)> _players = new();
    private readonly List<(EntityUid Marker, EntityUid Player, int Event)> _triggered = new();
    private readonly List<EntityUid> _expired = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _nextAllowed = TimeSpan.Zero);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        UpdateKittens(now);

        if (now < _nextScan)
            return;

        _nextScan = now + ScanInterval;

        if (now < _nextAllowed)
            return;

        Scan();

        if (_triggered.Count > 0)
            _nextAllowed = now + GlobalCooldown;
    }

    private void Scan()
    {
        _triggered.Clear();
        _players.Clear();

        var now = _timing.CurTime;
        var built = false;
        var markers = EntityQueryEnumerator<DirectorMarkerComponent, TransformComponent>();
        while (markers.MoveNext(out var marker, out var comp, out var xform))
        {
            if (_triggered.Count > 0)
                break;

            if (xform.MapID == MapId.Nullspace || now < comp.NextActivation || comp.Used.Count >= EventCount)
                continue;

            if (!built)
            {
                built = true;
                var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
                while (players.MoveNext(out var uid, out _, out var state, out var pxform))
                {
                    if (!_mobState.IsAlive(uid, state) || pxform.MapID == MapId.Nullspace)
                        continue;

                    _players.Add((uid, pxform.MapID, _transform.GetWorldPosition(pxform)));
                }
            }

            if (comp.Event < 0)
                comp.Event = PickEvent(comp);

            var radius = comp.Event == SecurityCorpseEvent ? comp.SecurityCorpseRadius : comp.Radius;
            var pos = _transform.GetWorldPosition(xform);
            var radiusSquared = radius * radius;

            foreach (var player in _players)
            {
                if (player.Map != xform.MapID || (player.Pos - pos).LengthSquared() > radiusSquared)
                    continue;

                _triggered.Add((marker, player.Uid, comp.Event));
                break;
            }
        }

        foreach (var (marker, player, eventId) in _triggered)
        {
            Run(marker, player, eventId);
        }
    }

    private void Run(EntityUid marker, EntityUid player, int eventId)
    {
        var origin = Transform(marker).Coordinates;

        switch (eventId)
        {
            case 0:
                SoldierAndDrones(origin);
                break;
            case 1:
                SecurityCorpse(origin);
                break;
            case 2:
                CorgiAndSheep(origin);
                break;
            case 3:
                ServiceCorpse(origin);
                break;
            default:
                KittenFollow(origin, player);
                break;
        }

        if (!TryComp<DirectorMarkerComponent>(marker, out var comp))
            return;

        comp.Used.Add(eventId);
        comp.Event = -1;
        comp.NextActivation = _timing.CurTime + MarkerCooldown;

        if (comp.Used.Count >= EventCount)
            QueueDel(marker);
    }

    private int PickEvent(DirectorMarkerComponent comp)
    {
        var pool = new List<int>();
        for (var i = 0; i < EventCount; i++)
        {
            if (!comp.Used.Contains(i))
                pool.Add(i);
        }

        return pool.Count == 0 ? -1 : _random.Pick(pool);
    }

    private void SoldierAndDrones(EntityCoordinates origin)
    {
        var soldier = Spawn("MobSyndicateFootsoldier", origin);

        foreach (var slot in StripSlots)
        {
            if (_inventory.TryGetSlotEntity(soldier, slot, out var item))
                QueueDel(item);
        }

        var knife = Spawn("CombatKnife", origin);
        _hands.TryPickupAnyHand(soldier, knife, checkActionBlocker: false, animate: false);

        var start = _random.NextFloat(0f, MathF.Tau);
        for (var i = 0; i < 2; i++)
        {
            var dir = FindDirection(origin, 2f, start + i * MathF.PI);
            Spawn("MobCobraSpace", origin.Offset(dir * 2f));
        }
    }

    private void SecurityCorpse(EntityCoordinates origin)
    {
        Spawn("MobRandomSecurityCorpse", origin);

        var dir = FindDirection(origin, 1f, _random.NextFloat(0f, MathF.Tau));
        Spawn("WeaponRifleLecter", origin.Offset(dir * 0.5f));

        AddBlood(origin);
        PlayDistantShots(origin);
    }

    private void CorgiAndSheep(EntityCoordinates origin)
    {
        Spawn("MobCorgiNarsi", origin);

        var start = _random.NextFloat(0f, MathF.Tau);
        for (var i = 0; i < 2; i++)
        {
            var dir = FindDirection(origin, 2f, start + i * MathF.PI);
            Spawn("MobSheep", origin.Offset(dir * 2f));
        }
    }

    private void ServiceCorpse(EntityCoordinates origin)
    {
        Spawn("MobRandomServiceCorpse", origin);

        var dir = FindDirection(origin, 1f, _random.NextFloat(0f, MathF.Tau));
        Spawn("WeaponShotgunDoubleBarreled", origin.Offset(dir * 0.5f));

        AddBlood(origin);

        var bearDir = FindDirection(origin, 3f, _random.NextFloat(0f, MathF.Tau));
        Spawn("MobBearSpace", origin.Offset(bearDir * 3f));
    }

    private void KittenFollow(EntityCoordinates origin, EntityUid player)
    {
        var kitten = Spawn("MobCatKitten", origin);

        if (!TryComp<HTNComponent>(kitten, out var htn))
            return;

        var follow = EnsureComp<DirectorKittenFollowComponent>(kitten);
        follow.EndTime = _timing.CurTime + follow.Duration;
        follow.OriginalTask = htn.RootTask.Task;

        htn.RootTask = new HTNCompoundTask { Task = FollowCompound };

        _npc.SetBlackboard(kitten, NPCBlackboard.FollowTarget, new EntityCoordinates(player, Vector2.Zero), htn);
        _npc.SetBlackboard(kitten, "FollowRange", 2f, htn);
        _npc.SetBlackboard(kitten, "FollowCloseRange", 1f, htn);
        _npc.SetBlackboard(kitten, "MinimumIdleTime", 0.2f, htn);
        _npc.SetBlackboard(kitten, "MaximumIdleTime", 0.5f, htn);

        ResetPlan(htn);
    }

    private void UpdateKittens(TimeSpan now)
    {
        _expired.Clear();

        var query = EntityQueryEnumerator<DirectorKittenFollowComponent>();
        while (query.MoveNext(out var uid, out var follow))
        {
            if (now >= follow.EndTime)
                _expired.Add(uid);
        }

        foreach (var uid in _expired)
        {
            if (!TryComp<DirectorKittenFollowComponent>(uid, out var follow))
                continue;

            if (TryComp<HTNComponent>(uid, out var htn))
            {
                htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.FollowTarget);
                htn.Blackboard.Remove<float>("FollowRange");
                htn.Blackboard.Remove<float>("FollowCloseRange");
                htn.Blackboard.Remove<float>("MinimumIdleTime");
                htn.Blackboard.Remove<float>("MaximumIdleTime");
                htn.RootTask = new HTNCompoundTask { Task = follow.OriginalTask };
                ResetPlan(htn);
            }

            RemComp<DirectorKittenFollowComponent>(uid);
        }
    }

    private void ResetPlan(HTNComponent htn)
    {
        if (htn.Plan != null)
        {
            _htn.ShutdownTask(htn.Plan.CurrentOperator, htn.Blackboard, HTNOperatorStatus.Failed);
            _htn.ShutdownPlan(htn);
            htn.Plan = null;
        }

        _htn.Replan(htn);
    }

    private void AddBlood(EntityCoordinates origin)
    {
        var coords = origin.Offset(new Vector2(-0.5f, -0.5f));
        _decals.TryAddDecal("splatter", coords, out _, BloodColor, Angle.Zero, 0, true);
    }

    private void PlayDistantShots(EntityCoordinates origin)
    {
        var shot = new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/ltrifle.ogg");
        var scream = new SoundCollectionSpecifier("MaleScreams");
        var audioParams = AudioParams.Default.WithMaxDistance(40f).WithRolloffFactor(0.8f);

        _audio.PlayPvs(scream, origin, audioParams);

        foreach (var delay in new[] { 0, 150, 300 })
        {
            Timer.Spawn(delay, () =>
            {
                if (Exists(origin.EntityId))
                    _audio.PlayPvs(shot, origin, audioParams);
            });
        }
    }

    private Vector2 FindDirection(EntityCoordinates origin, float distance, float startAngle)
    {
        const int steps = 12;

        for (var i = 0; i < steps; i++)
        {
            var angle = startAngle + i * MathF.Tau / steps;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

            if (!Blocked(origin.Offset(dir * distance)))
                return dir;
        }

        return new Vector2(MathF.Cos(startAngle), MathF.Sin(startAngle));
    }

    private bool Blocked(EntityCoordinates coords)
    {
        var gridUid = _transform.GetGrid(coords);
        if (gridUid == null || !TryComp<MapGridComponent>(gridUid, out var grid))
            return true;

        if (!_maps.TryGetTileRef(gridUid.Value, grid, coords, out var tileRef) || tileRef.Tile.IsEmpty)
            return true;

        return _turf.IsTileBlocked(tileRef, BlockMask);
    }
}
