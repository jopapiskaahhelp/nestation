// Nestation New City CC BY-NC 3.0
using System.Linq;
using System.Numerics;
using Content.Shared._Nestation.Marines;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Mobs.Systems;
using Content.Server.Chat.Managers;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.Marines;

public sealed partial class MarineMissionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedDoorSystem _door = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MarineSupplySystem _supply = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private IChatManager _chatManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MarineSpawnZoneComponent, SignalReceivedEvent>(OnZoneSignal);
        SubscribeLocalEvent<MarineGateComponent, MapInitEvent>(OnGateInit);
        SubscribeLocalEvent<MarineMissionConsoleComponent, MarineMissionStartMessage>(OnStart);
        SubscribeLocalEvent<MarineMissionConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
    }

    private void OnZoneSignal(Entity<MarineSpawnZoneComponent> ent, ref SignalReceivedEvent args)
    {
        if (args.Port != "Trigger")
            return;

        TriggerZone(ent);
    }

    public int TriggerZone(Entity<MarineSpawnZoneComponent> zone)
    {
        var xform = Transform(zone);
        var spawned = 0;

        for (var i = 0; i < zone.Comp.Count; i++)
        {
            var offset = new Vector2(_random.NextFloat(-zone.Comp.Spread, zone.Comp.Spread),
                _random.NextFloat(-zone.Comp.Spread, zone.Comp.Spread));
            var marine = Spawn(zone.Comp.Marine, xform.Coordinates.Offset(offset));

            if (TryComp<MarineComponent>(marine, out var marineComp))
                marineComp.MissionId = zone.Comp.MissionId;

            spawned++;
        }

        return spawned;
    }

    private void OnGateInit(Entity<MarineGateComponent> ent, ref MapInitEvent args)
    {
        var xform = Transform(ent);
        var rotation = xform.LocalRotation;

        for (var i = 1; i < ent.Comp.Length; i++)
        {
            var coords = xform.Coordinates.Offset(rotation.RotateVec(new Vector2(i, 0)));

            if (SegmentAt(ent.Comp, coords) != null)
                continue;

            var segment = Spawn(ent.Comp.SegmentProto, coords);
            _transform.SetLocalRotation(segment, rotation);
        }
    }

    private EntityUid? SegmentAt(MarineGateComponent gate, EntityCoordinates coords)
    {
        var map = _transform.ToMapCoordinates(coords);

        foreach (var (door, _) in _lookup.GetEntitiesInRange<DoorComponent>(map, 0.3f, LookupFlags.Static))
        {
            if (MetaData(door).EntityPrototype?.ID == gate.SegmentProto.Id)
                return door;
        }

        return null;
    }

    private void OnStart(EntityUid uid, MarineMissionConsoleComponent comp, MarineMissionStartMessage args)
    {
        Entity<MarineMissionConsoleComponent> ent = (uid, comp);

        if (ent.Comp.Started)
            return;

        ent.Comp.Started = true;
        ent.Comp.NextWave = _timing.CurTime;
        _supply.InitMission(ent.Comp.MissionId, ent.Comp.StartCredits, ent.Comp.StartCreditsPerPlayer);

        var gates = EntityQueryEnumerator<MarineGateComponent>();
        while (gates.MoveNext(out var gateUid, out var gate))
        {
            if (gate.MissionId != ent.Comp.MissionId)
                continue;

            OpenDoor(gateUid);

            var gateXform = Transform(gateUid);
            var rotation = gateXform.LocalRotation;

            for (var i = 1; i < gate.Length; i++)
            {
                var coords = gateXform.Coordinates.Offset(rotation.RotateVec(new Vector2(i, 0)));

                if (SegmentAt(gate, coords) is { } segment)
                    OpenDoor(segment);
            }
        }

        UpdateUi(ent);
    }

    private void OnUiOpened(EntityUid uid, MarineMissionConsoleComponent comp, BoundUIOpenedEvent args)
    {
        Entity<MarineMissionConsoleComponent> ent = (uid, comp);

        UpdateUi(ent);
    }

    private void OpenDoor(EntityUid uid)
    {
        if (TryComp<DoorComponent>(uid, out var door))
            _door.TryOpen(uid, door, null, quiet: false);
    }

    private void UpdateUi(Entity<MarineMissionConsoleComponent> ent)
    {
        _ui.SetUiState(ent.Owner, MarineMissionUiKey.Key,
            new MarineMissionUiState(ent.Comp.Started, ent.Comp.Spawned, ent.Comp.Total));
    }

    public bool TryBeginRaise(Entity<MarineFlagpoleComponent> flag, EntityUid marine)
    {
        if (flag.Comp.Raised || flag.Comp.Raiser != null)
            return false;

        flag.Comp.Raiser = marine;
        flag.Comp.RaiseEnd = _timing.CurTime + TimeSpan.FromSeconds(flag.Comp.RaiseTime);
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        var consoles = EntityQueryEnumerator<MarineMissionConsoleComponent>();
        while (consoles.MoveNext(out var uid, out var console))
        {
            if (!console.Started || console.Spawned >= console.Total || now < console.NextWave)
                continue;

            console.NextWave = now + TimeSpan.FromSeconds(console.WaveInterval);

            var zones = new List<Entity<MarineSpawnZoneComponent>>();
            var zoneQuery = EntityQueryEnumerator<MarineSpawnZoneComponent>();
            while (zoneQuery.MoveNext(out var zoneUid, out var zone))
            {
                if (zone.MissionId == console.MissionId && zone.Group == console.ZoneGroup)
                    zones.Add((zoneUid, zone));
            }

            _random.Shuffle(zones);

            var budget = Math.Min(console.BatchSize, console.Total - console.Spawned);
            foreach (var zone in zones)
            {
                if (budget <= 0)
                    break;

                var count = TriggerZone(zone);
                console.Spawned += count;
                budget -= count;
            }

            UpdateUi((uid, console));
        }

        var flags = EntityQueryEnumerator<MarineFlagpoleComponent, TransformComponent>();
        while (flags.MoveNext(out var flagUid, out var flag, out var flagXform))
        {
            if (flag.Raised || flag.Raiser is not { } raiser)
                continue;

            if (!Exists(raiser) || _mobState.IsIncapacitated(raiser))
            {
                flag.Raiser = null;
                continue;
            }

            var distance = (_transform.GetWorldPosition(raiser) - _transform.GetWorldPosition(flagXform)).Length();
            if (distance > 3.5f)
            {
                flag.Raiser = null;
                continue;
            }

            if (now < flag.RaiseEnd)
                continue;

            CompleteRaise((flagUid, flag), flagXform, now);
        }
    }

    private void CompleteRaise(Entity<MarineFlagpoleComponent> flag, TransformComponent xform, TimeSpan now)
    {
        flag.Comp.Raised = true;
        flag.Comp.Raiser = null;
        flag.Comp.RaisedAt = now;
        _appearance.SetData(flag, MarineFlagVisuals.Raised, true);

        var players = _supply.PlayerScale();
        var raw = (flag.Comp.Reward + flag.Comp.RewardPerPlayer * players) * (1f + flag.Comp.OrderBonus * flag.Comp.Order);
        var reward = (int) (MathF.Round(raw / 10f) * 10f);
        _supply.AddCredits(flag.Comp.MissionId, reward);
        _chatManager.DispatchServerAnnouncement(Loc.GetString("marine-flag-reward",
            ("credits", reward),
            ("balance", _supply.GetCredits(flag.Comp.MissionId))));

        var flagPos = _transform.GetWorldPosition(xform);
        var count = flag.Comp.ReinforcementCount;
        var baseAngle = _random.NextFloat(0f, MathF.Tau);

        for (var i = 0; i < count; i++)
        {
            var offset = Vector2.Zero;
            var found = false;

            for (var attempt = 0; attempt < 8 && !found; attempt++)
            {
                var angle = baseAngle + MathF.Tau * i / count + _random.NextFloat(-0.4f, 0.4f);
                var radius = _random.NextFloat(2.5f, 4.5f);
                offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;

                var box = Box2.CenteredAround(flagPos + offset, new Vector2(0.7f, 0.7f));
                found = !_physics.GetCollidingEntities(xform.MapID, in box).Any();
            }

            DropMarine("MobMarine", xform.Coordinates.Offset(offset), flag.Comp.MissionId, now);
        }
    }

    private void DropMarine(string proto, EntityCoordinates coords, string missionId, TimeSpan now)
    {
        var marine = Spawn(proto, coords);

        if (TryComp<MarineComponent>(marine, out var marineComp))
            marineComp.MissionId = missionId;

        var chute = EnsureComp<MarineParachuteComponent>(marine);
        chute.Start = now;
        Dirty(marine, chute);

        var canopy = Spawn("MarineParachuteCanopy", coords);
        if (TryComp<MarineParachuteComponent>(canopy, out var canopyChute))
        {
            canopyChute.Start = now;
            Dirty(canopy, canopyChute);
        }
    }
}
