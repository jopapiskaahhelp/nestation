// Nestation New City CC BY-NC 3.0
using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.Nutrition.EntitySystems;
using Content.Shared.Buckle;
using Content.Shared.Chat;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Smoking;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.Marines;

public sealed partial class MarineCivilianLifeSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private OpenableSystem _openable = default!;
    [Dependency] private SmokingSystem _smoking = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private ChatSystem _chat = default!;

    private sealed class DrinkState
    {
        public EntityUid Item;
        public bool Beer;
        public bool Refill;
        public TimeSpan Next;
    }

    private sealed class SmokeState
    {
        public EntityUid Item;
        public TimeSpan Next;
    }

    private readonly Dictionary<EntityUid, DrinkState> _drinks = new();
    private readonly Dictionary<EntityUid, SmokeState> _smokers = new();
    private TimeSpan _nextTick;

    private static readonly string[] BeerSips =
    {
        "делает глоток пива",
        "отпивает пива из бутылки",
        "неторопливо прихлёбывает пиво",
    };

    private static readonly string[] CoffeeSips =
    {
        "отпивает кофе",
        "дует на кофе и делает глоток",
        "осторожно делает глоток кофе",
    };

    private static readonly string[] SmokeActions =
    {
        "затягивается сигаретой",
        "выпускает струйку дыма",
        "стряхивает пепел",
        "покашливает от дыма",
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _drinks.Clear();
            _smokers.Clear();
        });
    }

    private void Protect(EntityUid civ)
    {
        if (TryComp<MarineComponent>(civ, out var comp))
            comp.IgnoreRemovalUntil = _timing.CurTime + TimeSpan.FromSeconds(2);
    }

    public void GiveBarItems(EntityUid civ)
    {
        var roll = _random.NextFloat();

        if (roll < 0.40f)
            GiveDrink(civ, true, true);
        else if (roll < 0.65f)
            GiveDrink(civ, false, true);

        if (_random.Prob(0.30f))
            GiveCigarette(civ);
    }

    public void GiveDrink(EntityUid civ, bool beer, bool refill)
    {
        if (!Exists(civ) || _drinks.ContainsKey(civ))
            return;

        var item = Spawn(beer ? "DrinkBeerBottleFull" : "DrinkCoffee", Transform(civ).Coordinates);

        if (beer)
            _openable.SetOpen(item, true);

        if (!_hands.TryForcePickupAnyHand(civ, item, checkActionBlocker: false))
        {
            QueueDel(item);
            return;
        }

        _drinks[civ] = new DrinkState
        {
            Item = item,
            Beer = beer,
            Refill = refill,
            Next = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(2f, 5f)),
        };
    }

    public void GiveCigarette(EntityUid civ)
    {
        if (!Exists(civ) || _smokers.ContainsKey(civ))
            return;

        var cigarette = Spawn("Cigarette", Transform(civ).Coordinates);

        if (!_inventory.TryEquip(civ, cigarette, "mask", silent: true, force: true))
        {
            QueueDel(cigarette);
            return;
        }

        _smoking.SetSmokableState(cigarette, SmokableState.Lit);
        _smokers[civ] = new SmokeState { Item = cigarette, Next = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(5f, 9f)) };
    }

    public void Clear(EntityUid civ, bool deleteItems)
    {
        Protect(civ);

        if (_drinks.Remove(civ, out var drink) && Exists(drink.Item))
        {
            if (deleteItems)
                QueueDel(drink.Item);
            else
                _hands.TryDrop(civ, drink.Item, checkActionBlocker: false);
        }

        if (_smokers.Remove(civ, out var smoke) && Exists(smoke.Item))
            QueueDel(smoke.Item);
    }

    public void PanicDrop(EntityUid civ)
    {
        Protect(civ);
        _buckle.TryUnbuckle(civ, civ, popup: false);

        foreach (var held in _hands.EnumerateHeld(civ).ToList())
        {
            _hands.TryDrop(civ, held, checkActionBlocker: false);
        }

        _drinks.Remove(civ);
        _smokers.Remove(civ);
    }

    private void Emote(EntityUid uid, string text)
    {
        if (Exists(uid) && !_mobState.IsDead(uid))
            _chat.TrySendInGameICMessage(uid, text, InGameICChatType.Emote, false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        if (now < _nextTick)
            return;

        _nextTick = now + TimeSpan.FromSeconds(1);

        foreach (var civ in _drinks.Keys.ToList())
        {
            var drink = _drinks[civ];

            if (!Exists(civ) || _mobState.IsDead(civ) || !Exists(drink.Item) || Transform(drink.Item).ParentUid != civ)
            {
                _drinks.Remove(civ);
                continue;
            }

            if (now >= drink.Next)
                Sip(civ, drink, now);
        }

        foreach (var civ in _smokers.Keys.ToList())
        {
            var smoke = _smokers[civ];

            if (!Exists(civ) || _mobState.IsDead(civ) || !Exists(smoke.Item))
            {
                _smokers.Remove(civ);
                continue;
            }

            if (now < smoke.Next)
                continue;

            smoke.Next = now + TimeSpan.FromSeconds(_random.NextFloat(8f, 14f));
            Emote(civ, _random.Pick(SmokeActions));
        }
    }

    private void Sip(EntityUid civ, DrinkState drink, TimeSpan now)
    {
        if (!_solutions.TryGetSolution(drink.Item, "drink", out var solution, out var contents) || contents.Volume <= FixedPoint2.Zero)
        {
            Finish(civ, drink);
            return;
        }

        _solutions.SplitSolution(solution.Value, FixedPoint2.New(5));
        Emote(civ, _random.Pick(drink.Beer ? BeerSips : CoffeeSips));
        drink.Next = now + TimeSpan.FromSeconds(_random.NextFloat(4.5f, 5.5f));
    }

    private void Finish(EntityUid civ, DrinkState drink)
    {
        Emote(civ, drink.Beer ? "допивает пиво до дна" : "допивает кофе до дна");
        Protect(civ);
        _hands.TryDrop(civ, drink.Item, checkActionBlocker: false);
        _drinks.Remove(civ);

        if (drink.Refill && _random.Prob(0.5f))
            GiveDrink(civ, drink.Beer, true);
    }
}
