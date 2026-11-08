// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Shared._Nestation.Marines;
using Content.Shared.GameTicking;
using Content.Shared.Item;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Content.Shared.StepTrigger.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.Marines;

public sealed partial class MarineSupplySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private StepTriggerSystem _stepTrigger = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    private const int MaxPlayersScale = 15;

    private readonly Dictionary<string, int> _credits = new();
    private TimeSpan _nextUi;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _credits.Clear());
        SubscribeLocalEvent<MarineSupplyLiftComponent, MapInitEvent>(OnLiftInit);
        SubscribeLocalEvent<MarineSupplyConsoleComponent, MarineSupplyBuyMessage>(OnBuy);
        SubscribeLocalEvent<MarineSupplyConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
    }

    public int PlayerScale()
    {
        return Math.Clamp(_players.PlayerCount, 0, MaxPlayersScale);
    }

    public void InitMission(string missionId, int start, int perPlayer)
    {
        if (_credits.ContainsKey(missionId))
            return;

        _credits[missionId] = start + perPlayer * PlayerScale();
    }

    public int GetCredits(string missionId)
    {
        return _credits.GetValueOrDefault(missionId);
    }

    public void AddCredits(string missionId, int amount)
    {
        _credits[missionId] = GetCredits(missionId) + amount;
    }

    private void OnLiftInit(Entity<MarineSupplyLiftComponent> ent, ref MapInitEvent args)
    {
        SetBlocking(ent, false);
        _stepTrigger.SetActive(ent, false);
        _appearance.SetData(ent, MarineLiftVisuals.State, MarineLiftState.Raised);
    }

    private void OnUiOpened(EntityUid uid, MarineSupplyConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, comp);
    }

    private Entity<MarineSupplyLiftComponent>? FindLift(string missionId)
    {
        var query = EntityQueryEnumerator<MarineSupplyLiftComponent>();
        while (query.MoveNext(out var uid, out var lift))
        {
            if (lift.MissionId == missionId)
                return (uid, lift);
        }

        return null;
    }

    private void OnBuy(EntityUid uid, MarineSupplyConsoleComponent comp, MarineSupplyBuyMessage args)
    {
        if (!_prototypes.TryIndex<MarineSupplyPrototype>(args.Id, out var entry))
            return;

        if (FindLift(comp.MissionId) is not { } lift || lift.Comp.State != MarineLiftState.Raised)
            return;

        if (lift.Comp.Queue.Count + entry.Entities.Count > lift.Comp.MaxQueue)
            return;

        var credits = GetCredits(comp.MissionId);
        if (credits < entry.Price)
            return;

        _credits[comp.MissionId] = credits - entry.Price;
        lift.Comp.Queue.AddRange(entry.Entities);

        if (lift.Comp.DeliveryAt == null)
            lift.Comp.DeliveryAt = _timing.CurTime + TimeSpan.FromSeconds(lift.Comp.DeliveryDelay);

        UpdateUi(uid, comp);
    }

    private void UpdateUi(EntityUid uid, MarineSupplyConsoleComponent comp)
    {
        var credits = GetCredits(comp.MissionId);
        var state = MarineLiftState.Raised;
        var seconds = 0;
        var queued = 0;
        var canOrder = false;

        if (FindLift(comp.MissionId) is { } lift)
        {
            state = lift.Comp.State;
            queued = lift.Comp.Queue.Count;
            canOrder = state == MarineLiftState.Raised && queued < lift.Comp.MaxQueue;

            if (state == MarineLiftState.Raised && lift.Comp.DeliveryAt is { } at)
                seconds = Math.Max(0, (int) Math.Ceiling((at - _timing.CurTime).TotalSeconds));
        }

        _ui.SetUiState(uid, MarineSupplyUiKey.Key, new MarineSupplyUiState(credits, state, seconds, queued, canOrder));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var lifts = EntityQueryEnumerator<MarineSupplyLiftComponent>();

        while (lifts.MoveNext(out var uid, out var lift))
        {
            switch (lift.State)
            {
                case MarineLiftState.Raised:
                    if (lift.DeliveryAt is { } at && now >= at)
                        StartLowering((uid, lift), now);
                    break;

                case MarineLiftState.Lowering:
                    if (now >= lift.StateEnd)
                    {
                        lift.State = MarineLiftState.Lowered;
                        lift.StateEnd = now + TimeSpan.FromSeconds(lift.LoweredTime);
                        SetBlocking((uid, lift), false);
                        _stepTrigger.SetActive(uid, true);
                        _appearance.SetData(uid, MarineLiftVisuals.State, lift.State);
                    }

                    break;

                case MarineLiftState.Lowered:
                    if (now >= lift.StateEnd)
                    {
                        lift.State = MarineLiftState.Raising;
                        lift.StateEnd = now + TimeSpan.FromSeconds(lift.RaiseTime);
                        _stepTrigger.SetActive(uid, false);
                        SetBlocking((uid, lift), true);
                        _appearance.SetData(uid, MarineLiftVisuals.State, lift.State);
                    }

                    break;

                case MarineLiftState.Raising:
                    if (now >= lift.StateEnd)
                        FinishRaising((uid, lift));
                    break;
            }
        }

        if (now < _nextUi)
            return;

        _nextUi = now + TimeSpan.FromSeconds(1);

        var consoles = EntityQueryEnumerator<MarineSupplyConsoleComponent>();
        while (consoles.MoveNext(out var consoleUid, out var console))
        {
            UpdateUi(consoleUid, console);
        }
    }

    private void SetBlocking(Entity<MarineSupplyLiftComponent> lift, bool blocking)
    {
        if (!TryComp<FixturesComponent>(lift, out var fixtures)
            || !fixtures.Fixtures.TryGetValue("lift", out var fixture))
        {
            return;
        }

        _physics.SetHard(lift, fixture, blocking, fixtures);
    }

    private void StartLowering(Entity<MarineSupplyLiftComponent> lift, TimeSpan now)
    {
        lift.Comp.State = MarineLiftState.Lowering;
        lift.Comp.StateEnd = now + TimeSpan.FromSeconds(lift.Comp.LowerTime);
        SetBlocking(lift, true);
        _appearance.SetData(lift, MarineLiftVisuals.State, lift.Comp.State);

        var xform = Transform(lift);
        var center = _transform.GetMapCoordinates(lift.Owner, xform).Offset(xform.LocalRotation.RotateVec(new Vector2(0.5f, 0f)));

        foreach (var (item, _) in _lookup.GetEntitiesInRange<ItemComponent>(center, 1.9f, LookupFlags.Dynamic | LookupFlags.Uncontained))
        {
            QueueDel(item);
        }
    }

    private void FinishRaising(Entity<MarineSupplyLiftComponent> lift)
    {
        lift.Comp.State = MarineLiftState.Raised;
        lift.Comp.DeliveryAt = null;
        SetBlocking(lift, false);
        _appearance.SetData(lift, MarineLiftVisuals.State, lift.Comp.State);

        var xform = Transform(lift);
        var rotation = xform.LocalRotation;

        for (var i = 0; i < lift.Comp.Queue.Count; i++)
        {
            var tile = i % 6;
            var local = new Vector2(tile % 2, tile / 2 - 1)
                        + new Vector2(_random.NextFloat(-0.2f, 0.2f), _random.NextFloat(-0.2f, 0.2f));
            Spawn(lift.Comp.Queue[i], xform.Coordinates.Offset(rotation.RotateVec(local)));
        }

        lift.Comp.Queue.Clear();
    }
}
