// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Trigger.Systems;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.Marines;

public sealed partial class MarineTownSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private TriggerSystem _trigger = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private MarineSceneSystem _scene = default!;
    [Dependency] private MarineSystem _marines = default!;
    [Dependency] private MarineCivilianLifeSystem _life = default!;

    private sealed class Activity
    {
        public List<Func<bool>> Steps = new();
        public int Step;
        public List<EntityUid> People = new();
        public Func<bool>? Valid;
        public Action? End;
    }

    private readonly List<Activity> _activities = new();
    private readonly List<EntityUid> _bankQueue = new();
    private readonly HashSet<EntityUid> _usedPoints = new();
    private readonly List<(EntityUid Partisan, TimeSpan Until)> _hunts = new();

    private TimeSpan _nextTick;
    private TimeSpan _nextCivEvent;
    private TimeSpan _nextBar;
    private TimeSpan _nextBankJoin;
    private TimeSpan _nextAssembly;
    private TimeSpan _nextPartisan;
    private TimeSpan? _serveStart;

    private static readonly string[] BarEmotes =
    {
        "пьёт из стакана",
        "что-то тихо рассказывает соседу",
        "морщится и качает головой",
        "вздыхает, глядя в стакан",
        "негромко смеётся",
        "просит налить ещё",
    };

    private static readonly string[] EatEmotes =
    {
        "жадно жуёт хлеб",
        "медленно ест свой паёк",
        "крошит хлеб в ладонь и ест",
    };

    private static readonly string[] Complaints =
    {
        "Опять так мало еды...",
        "Это называется паёк?",
        "Раньше хлеба давали больше.",
        "Партия обещала, а в животе пусто.",
    };

    private static readonly string[] Criticisms =
    {
        "Говорят, склады полны, а нам достаются крошки.",
        "Лучше бы заботились о людях, а не о парадах.",
        "Они только и умеют, что запрещать и требовать.",
    };

    private static readonly string[] Rules =
    {
        "Граждане! Внимание!",
        "Партизанщина и саботаж караются расстрелом!",
        "О подозрительных лицах сообщайте военной полиции.",
        "Паёк выдаётся только лояльным гражданам.",
        "Враг скоро будет разбит. Верьте партии.",
        "Расходитесь по домам.",
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());
    }

    private void Reset()
    {
        _activities.Clear();
        _bankQueue.Clear();
        _usedPoints.Clear();
        _hunts.Clear();
        _serveStart = null;
        _nextBar = TimeSpan.Zero;
        _nextBankJoin = TimeSpan.Zero;
        _nextAssembly = TimeSpan.Zero;
        _nextPartisan = TimeSpan.Zero;
        _nextCivEvent = TimeSpan.Zero;
    }

    private struct PointInfo
    {
        public EntityUid Uid;
        public MarineCivilianPointComponent Comp;
        public Vector2 Pos;
        public MapId Map;
    }

    private List<PointInfo> Points(MarinePointKind kind)
    {
        var list = new List<PointInfo>();
        var query = EntityQueryEnumerator<MarineCivilianPointComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Kind == kind)
                list.Add(new PointInfo { Uid = uid, Comp = comp, Pos = _transform.GetWorldPosition(xform), Map = xform.MapID });
        }

        return list;
    }

    private bool IsIdleCivilian(EntityUid uid, MarineComponent comp, TimeSpan now)
    {
        return comp.Passive && !comp.Static && !comp.Frozen && comp.Scene == null && !comp.Wanted && comp.Fear < 0.7f
               && comp.CivMode == MarineCivilianMode.Idle && now >= comp.PanicUntil
               && !HasComp<ActorComponent>(uid) && !_mobState.IsDead(uid);
    }

    private List<EntityUid> IdleCivilians(Vector2 near, MapId map, float range, int max, TimeSpan now, Func<MarineComponent, float>? need = null, float minNeed = 0f)
    {
        var found = new List<(EntityUid Ent, float Score)>();

        foreach (var (uid, comp) in _lookup.GetEntitiesInRange<MarineComponent>(new MapCoordinates(near, map), range, LookupFlags.Dynamic | LookupFlags.Uncontained))
        {
            if (!IsIdleCivilian(uid, comp, now))
                continue;

            var score = need?.Invoke(comp) ?? 0f;

            if (score < minNeed)
                continue;

            found.Add((uid, score + _random.NextFloat(0f, 0.15f)));
        }

        if (need != null)
            found.Sort((a, b) => b.Score.CompareTo(a.Score));
        else
            _random.Shuffle(found);

        var result = new List<EntityUid>();
        foreach (var (ent, _) in found)
        {
            if (result.Count >= max)
                break;

            result.Add(ent);
        }

        return result;
    }

    private void Claim(EntityUid uid)
    {
        if (!TryComp<MarineComponent>(uid, out var comp))
            return;

        comp.CivMode = MarineCivilianMode.Busy;
        comp.Running = false;
        comp.ScriptGoal = null;
        comp.Arrived = false;
    }

    private void Release(EntityUid uid)
    {
        if (!Exists(uid) || !TryComp<MarineComponent>(uid, out var comp))
            return;

        _buckle.TryUnbuckle(uid, uid, popup: false);

        if (comp.CivMode == MarineCivilianMode.Busy)
            comp.CivMode = MarineCivilianMode.Idle;

        comp.ScriptGoal = null;
        comp.NextActivity = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(20f, 40f));
    }

    private bool Alive(EntityUid uid)
    {
        return Exists(uid) && _mobState.IsAlive(uid);
    }

    private void Say(EntityUid uid, string text)
    {
        if (Alive(uid))
            _chat.TrySendInGameICMessage(uid, text, InGameICChatType.Speak, false);
    }

    private void Emote(EntityUid uid, string text)
    {
        if (Alive(uid))
            _chat.TrySendInGameICMessage(uid, text, InGameICChatType.Emote, false);
    }

    private Func<bool> Wait(float seconds)
    {
        TimeSpan? end = null;

        return () =>
        {
            end ??= _timing.CurTime + TimeSpan.FromSeconds(seconds);
            return _timing.CurTime >= end;
        };
    }

    private Func<bool> Do(Action action)
    {
        return () =>
        {
            action();
            return true;
        };
    }

    private Func<bool> MoveAll(List<(EntityUid Ent, Vector2 Goal)> targets, float timeout)
    {
        TimeSpan? end = null;

        return () =>
        {
            var now = _timing.CurTime;
            end ??= now + TimeSpan.FromSeconds(timeout);
            var allThere = true;

            foreach (var (ent, goal) in targets)
            {
                if (!Alive(ent) || !TryComp<MarineComponent>(ent, out var comp))
                    continue;

                comp.ScriptGoal = goal;

                if ((_transform.GetWorldPosition(ent) - goal).Length() > 1.1f)
                    allThere = false;
            }

            return allThere || now >= end;
        };
    }

    private void StartActivity(Activity activity)
    {
        _activities.Add(activity);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        for (var i = _activities.Count - 1; i >= 0; i--)
        {
            var activity = _activities[i];

            var broken = activity.Valid != null && !activity.Valid();

            foreach (var person in activity.People)
            {
                if (!Alive(person) || !TryComp<MarineComponent>(person, out var comp) || comp.CivMode != MarineCivilianMode.Busy)
                    broken = true;
            }

            if (broken || activity.Step >= activity.Steps.Count)
            {
                foreach (var person in activity.People)
                    Release(person);

                activity.End?.Invoke();
                _activities.RemoveAt(i);
                continue;
            }

            if (activity.Steps[activity.Step]())
                activity.Step++;
        }

        if (now < _nextTick)
            return;

        _nextTick = now + TimeSpan.FromSeconds(1);

        UpdateBank(now);
        UpdatePolice(now);
        ScheduleEvents(now);
    }

    private void ScheduleEvents(TimeSpan now)
    {
        if (now >= _nextBar)
            _nextBar = now + TimeSpan.FromSeconds(StartBar(now) ? _random.NextFloat(50f, 90f) : 10f);

        if (now >= _nextAssembly)
            _nextAssembly = now + TimeSpan.FromSeconds(StartAssembly(now) ? _random.NextFloat(240f, 420f) : 15f);

        if (now >= _nextCivEvent)
        {
            var started = _random.Prob(0.6f) ? StartWallChat(now) : StartSmokeBreak(now);
            _nextCivEvent = now + TimeSpan.FromSeconds(started ? _random.NextFloat(70f, 140f) : 15f);
        }

        if (now >= _nextPartisan)
        {
            _nextPartisan = now + TimeSpan.FromSeconds(_random.NextFloat(300f, 480f));

            if (_random.Prob(0.10f))
                StartPartisan(now);
        }
    }

    private static readonly string[] WallEmotes =
    {
        "что-то тихо говорит",
        "кивает",
        "жестикулирует",
        "качает головой",
        "негромко смеётся",
        "вздыхает и пожимает плечами",
    };

    private EntityUid? RandomIdleCivilian(TimeSpan now)
    {
        var list = new List<EntityUid>();
        var query = EntityQueryEnumerator<MarineComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (IsIdleCivilian(uid, comp, now))
                list.Add(uid);
        }

        return list.Count == 0 ? null : _random.Pick(list);
    }

    private bool WallAhead(MapId map, Vector2 from, Vector2 dir, float len)
    {
        var ray = new CollisionRay(from, dir, (int) CollisionGroup.BulletImpassable);

        foreach (var result in _physics.IntersectRay(map, ray, len, null, false))
        {
            if (!HasComp<MobStateComponent>(result.HitEntity) && result.Distance < len)
                return true;
        }

        return false;
    }

    private bool TryWallSpot(EntityUid civilian, out Vector2 spot, out Vector2 tangent)
    {
        spot = default;
        tangent = default;

        var pos = _transform.GetWorldPosition(civilian);
        var map = Transform(civilian).MapID;
        var dirs = new[] { new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f) };

        for (var i = 0; i < 30; i++)
        {
            var candidate = pos + new Vector2(_random.NextFloat(-14f, 14f), _random.NextFloat(-14f, 14f));
            var dir = _random.Pick(dirs);

            if (!WallAhead(map, candidate, dir, 1.3f) || WallAhead(map, candidate, -dir, 0.9f))
                continue;

            if (!HasSight(map, pos, candidate, civilian))
                continue;

            spot = candidate;
            tangent = new Vector2(-dir.Y, dir.X);
            return true;
        }

        return false;
    }

    private bool StartWallChat(TimeSpan now)
    {
        if (RandomIdleCivilian(now) is not { } first || !TryWallSpot(first, out var spot, out var tangent))
            return false;

        var people = IdleCivilians(spot, Transform(first).MapID, 30f, 2, now);

        if (!people.Contains(first))
            people.Insert(0, first);

        if (people.Count < 2)
            return false;

        people.RemoveRange(2, people.Count - 2);

        var targets = new List<(EntityUid, Vector2)>
        {
            (people[0], spot + tangent * 0.7f),
            (people[1], spot - tangent * 0.7f),
        };

        foreach (var person in people)
            Claim(person);

        var activity = new Activity { People = people };
        activity.Steps.Add(MoveAll(targets, 50f));
        activity.Steps.Add(Do(() =>
        {
            FacePoint(people[0], _transform.GetWorldPosition(people[1]));
            FacePoint(people[1], _transform.GetWorldPosition(people[0]));

            foreach (var person in people)
                _life.GiveDrink(person, false, false);
        }));

        var rounds = _random.Next(6, 11);
        for (var i = 0; i < rounds; i++)
        {
            activity.Steps.Add(Wait(9f));
            activity.Steps.Add(Do(() => Emote(_random.Pick(people), _random.Pick(WallEmotes))));
        }

        activity.End = () =>
        {
            foreach (var person in people)
                _life.Clear(person, true);
        };

        StartActivity(activity);
        return true;
    }

    private bool StartSmokeBreak(TimeSpan now)
    {
        if (RandomIdleCivilian(now) is not { } first || !TryWallSpot(first, out var spot, out var tangent))
            return false;

        var people = IdleCivilians(spot, Transform(first).MapID, 30f, 2, now);

        if (!people.Contains(first))
            people.Insert(0, first);

        if (people.Count > 2)
            people.RemoveRange(2, people.Count - 2);

        var targets = new List<(EntityUid, Vector2)>();
        for (var i = 0; i < people.Count; i++)
        {
            Claim(people[i]);
            targets.Add((people[i], spot + tangent * (i == 0 ? 0.6f : -0.6f)));
        }

        var activity = new Activity { People = people };
        activity.Steps.Add(MoveAll(targets, 50f));
        activity.Steps.Add(Do(() =>
        {
            foreach (var person in people)
                _life.GiveCigarette(person);
        }));

        var rounds = _random.Next(4, 8);
        for (var i = 0; i < rounds; i++)
        {
            activity.Steps.Add(Wait(9f));

            if (people.Count > 1)
                activity.Steps.Add(Do(() => Emote(_random.Pick(people), _random.Pick(WallEmotes))));
        }

        activity.End = () =>
        {
            foreach (var person in people)
                _life.Clear(person, true);
        };

        StartActivity(activity);
        return true;
    }

    private bool StartBar(TimeSpan now)
    {
        var points = Points(MarinePointKind.Bar);
        points.RemoveAll(p => _usedPoints.Contains(p.Uid));

        if (points.Count == 0)
            return false;

        var anchor = _random.Pick(points);
        var civilians = IdleCivilians(anchor.Pos, anchor.Map, 50f, _random.Next(1, 4), now, c => c.Thirst, 0.2f);

        if (civilians.Count == 0)
            return false;

        var targets = new List<(EntityUid, Vector2)>();
        var taken = new List<EntityUid>();

        foreach (var civilian in civilians)
        {
            var civPos = _transform.GetWorldPosition(civilian);
            points.Sort((a, b) => (a.Pos - civPos).LengthSquared().CompareTo((b.Pos - civPos).LengthSquared()));

            if (points.Count == 0)
                break;

            var point = points[0];
            points.RemoveAt(0);
            _usedPoints.Add(point.Uid);
            taken.Add(point.Uid);
            targets.Add((civilian, point.Pos));
            Claim(civilian);
        }

        var activity = new Activity { People = targets.ConvertAll(t => t.Item1) };
        activity.Steps.Add(MoveAll(targets, 50f));
        activity.Steps.Add(Do(() =>
        {
            foreach (var (civilian, _) in targets)
            {
                TrySit(civilian);
                _life.GiveBarItems(civilian);
            }
        }));

        var rounds = _random.Next(4, 8);
        for (var i = 0; i < rounds; i++)
        {
            activity.Steps.Add(Wait(13f));
            activity.Steps.Add(Do(() => Emote(_random.Pick(activity.People), _random.Pick(BarEmotes))));
        }

        activity.End = () =>
        {
            foreach (var point in taken)
                _usedPoints.Remove(point);

            foreach (var (civilian, _) in targets)
            {
                _life.Clear(civilian, false);

                if (Exists(civilian) && TryComp<MarineComponent>(civilian, out var comp))
                    comp.Thirst = 0f;
            }
        };

        StartActivity(activity);
        return true;
    }

    private void TrySit(EntityUid civilian)
    {
        var coords = _transform.GetMapCoordinates(civilian);

        foreach (var (strap, comp) in _lookup.GetEntitiesInRange<StrapComponent>(coords, 2f, LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Uncontained))
        {
            if (!comp.Enabled || comp.BuckledEntities.Count > 0)
                continue;

            if (_buckle.TryBuckle(civilian, civilian, strap, popup: false))
                return;
        }
    }

    private void UpdateBank(TimeSpan now)
    {
        var counters = Points(MarinePointKind.Counter);
        var queue = Points(MarinePointKind.Queue);

        if (counters.Count == 0 || queue.Count == 0)
            return;

        EntityUid? banker = null;
        var bankers = EntityQueryEnumerator<MarineComponent>();
        while (bankers.MoveNext(out var uid, out var comp))
        {
            if (comp.Banker && Alive(uid))
            {
                banker = uid;
                break;
            }
        }

        if (banker is not { } teller)
            return;

        var counter = counters[0];
        queue.Sort((a, b) => a.Comp.Order.CompareTo(b.Comp.Order));

        var slots = queue.ConvertAll(point => point.Pos);

        for (var i = _bankQueue.Count - 1; i >= 0; i--)
        {
            var civ = _bankQueue[i];

            if (!Alive(civ) || !TryComp<MarineComponent>(civ, out var comp) || comp.CivMode != MarineCivilianMode.Busy)
            {
                _bankQueue.RemoveAt(i);

                if (i == 0)
                    _serveStart = null;
            }
        }

        if (now >= _nextBankJoin && _bankQueue.Count < slots.Count)
        {
            _nextBankJoin = now + TimeSpan.FromSeconds(_random.NextFloat(12f, 30f));
            var joiners = IdleCivilians(counter.Pos, counter.Map, 45f, 1, now, c => c.Hunger, 0.4f);

            if (joiners.Count > 0)
            {
                Claim(joiners[0]);
                _bankQueue.Add(joiners[0]);
            }
        }

        for (var i = 0; i < _bankQueue.Count && i < slots.Count; i++)
        {
            if (!TryComp<MarineComponent>(_bankQueue[i], out var comp))
                continue;

            comp.ScriptGoal = slots[i];

            if (i > 0 && _random.Prob(0.015f))
                Say(_bankQueue[i], comp.Hunger > 0.7f ? "Скорее бы, я уже еле стою..." : "Долго же они возятся.");
        }

        if (_bankQueue.Count == 0)
        {
            _serveStart = null;
            return;
        }

        var first = _bankQueue[0];
        if ((_transform.GetWorldPosition(first) - slots[0]).Length() > 1.3f)
            return;

        if (_serveStart == null)
        {
            _serveStart = now;
            return;
        }

        if (now - _serveStart.Value < TimeSpan.FromSeconds(5))
            return;

        _serveStart = null;
        _bankQueue.RemoveAt(0);
        Serve(first, teller, counter, slots[0]);
    }

    private void Serve(EntityUid civilian, EntityUid teller, PointInfo counter, Vector2 queueSpot)
    {
        Emote(teller, "кладёт ломтик хлеба на стойку");

        var food = Spawn("FoodBreadPlainSlice", Transform(counter.Uid).Coordinates);

        var away = queueSpot + Angle.FromDegrees(_random.NextFloat(0f, 360f)).ToWorldVec() * 4f;
        var activity = new Activity { People = new List<EntityUid> { civilian } };

        activity.Steps.Add(Wait(1.5f));
        activity.Steps.Add(Do(() =>
        {
            if (!Exists(food))
                return;

            Emote(civilian, "берёт паёк");
            _hands.TryForcePickupAnyHand(civilian, food, checkActionBlocker: false);
        }));
        activity.Steps.Add(MoveAll(new List<(EntityUid, Vector2)> { (civilian, away) }, 15f));
        activity.Steps.Add(Do(() => Say(civilian, _random.Pick(Complaints))));
        activity.Steps.Add(Wait(3f));
        activity.Steps.Add(Do(() => Emote(civilian, _random.Pick(EatEmotes))));
        activity.Steps.Add(Wait(6f));
        activity.Steps.Add(Do(() =>
        {
            if (TryComp<MarineComponent>(civilian, out var comp))
                comp.IgnoreRemovalUntil = _timing.CurTime + TimeSpan.FromSeconds(2);

            if (Exists(food))
                QueueDel(food);

            if (TryComp<MarineComponent>(civilian, out var eater))
            {
                eater.Hunger = 0f;

                if (_random.Prob(0.15f + 0.45f * eater.Boldness))
                    Say(civilian, _random.Pick(Criticisms));
            }
        }));
        activity.Steps.Add(Wait(3f));

        StartActivity(activity);
    }

    private bool StartAssembly(TimeSpan now)
    {
        var points = Points(MarinePointKind.Assembly);
        if (points.Count == 0)
            return false;

        EntityUid? officer = null;
        var query = EntityQueryEnumerator<MarineComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Lecturer && Alive(uid) && comp.Scene == null && comp.Target == null && !HasComp<ActorComponent>(uid))
            {
                officer = uid;
                break;
            }
        }

        if (officer is not { } speaker)
            return false;

        var point = _random.Pick(points);
        var civilians = IdleCivilians(point.Pos, point.Map, 50f, _random.Next(4, 10), now);

        if (civilians.Count < 3)
            return false;

        var targets = new List<(EntityUid, Vector2)>();
        for (var i = 0; i < civilians.Count; i++)
        {
            Claim(civilians[i]);
            var angle = MathF.PI * 2f * i / civilians.Count;
            var radius = _random.NextFloat(3.2f, 5f);
            targets.Add((civilians[i], point.Pos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius));
        }

        if (!TryComp<MarineComponent>(speaker, out var speakerComp))
            return false;

        speakerComp.Home ??= _transform.GetWorldPosition(speaker);
        var home = speakerComp.Home.Value;
        speakerComp.PatrolGoal = point.Pos;

        var activity = new Activity
        {
            People = targets.ConvertAll(t => t.Item1),
            Valid = () => Alive(speaker),
        };

        activity.Steps.Add(MoveAll(targets, 45f));
        activity.Steps.Add(() =>
        {
            return (_transform.GetWorldPosition(speaker) - point.Pos).Length() < 2f;
        });
        activity.Steps.Add(Do(() =>
        {
            foreach (var (civilian, _) in targets)
                FacePoint(civilian, point.Pos);
        }));

        foreach (var line in Rules)
        {
            var text = line;
            activity.Steps.Add(Do(() => Say(speaker, text)));
            activity.Steps.Add(Wait(5.5f));
        }

        activity.End = () =>
        {
            if (Exists(speaker) && TryComp<MarineComponent>(speaker, out var comp))
                comp.PatrolGoal = home;
        };

        StartActivity(activity);
        return true;
    }

    private void FacePoint(EntityUid uid, Vector2 point)
    {
        if (!Exists(uid))
            return;

        var delta = point - _transform.GetWorldPosition(uid);
        if (delta.LengthSquared() > 0.01f)
            _transform.SetWorldRotation(uid, delta.ToWorldAngle());
    }

    private bool StartPartisan(TimeSpan now)
    {
        var halls = Points(MarinePointKind.Hall);
        if (halls.Count == 0)
            return false;

        var hall = _random.Pick(halls);
        var candidates = IdleCivilians(hall.Pos, hall.Map, 120f, 1, now);

        if (candidates.Count == 0)
            return false;

        var partisan = candidates[0];
        Claim(partisan);

        if (TryComp<MarineComponent>(partisan, out var marine))
            marine.Partisan = true;

        EntityUid? bomb = null;
        var activity = new Activity { People = new List<EntityUid> { partisan } };

        activity.Steps.Add(MoveAll(new List<(EntityUid, Vector2)> { (partisan, hall.Pos + new Vector2(_random.NextFloat(-1.5f, 1.5f), _random.NextFloat(-1.5f, 1.5f))) }, 120f));
        activity.Steps.Add(Do(() => _popup.PopupEntity(Loc.GetString("marine-partisan-plant"), partisan, PopupType.MediumCaution)));
        activity.Steps.Add(Wait(4f));
        activity.Steps.Add(Do(() =>
        {
            bomb = Spawn("ExGrenade", Transform(partisan).Coordinates);

            if (TryComp<MarineComponent>(partisan, out var comp))
            {
                var away = _transform.GetWorldPosition(partisan) - hall.Pos;
                var dir = away.LengthSquared() > 0.01f ? Vector2.Normalize(away) : new Vector2(1f, 0f);
                comp.Running = true;
                comp.ScriptGoal = _transform.GetWorldPosition(partisan) + dir * 14f;
            }
        }));
        activity.Steps.Add(Wait(3.5f));
        activity.Steps.Add(Do(() =>
        {
            if (bomb is { } device && Exists(device))
            {
                _trigger.Trigger(device, partisan);
            }
        }));
        activity.Steps.Add(Wait(1f));
        activity.End = () =>
        {
            if (!Exists(partisan) || !TryComp<MarineComponent>(partisan, out var comp))
                return;

            comp.Partisan = false;
            comp.Running = true;
            comp.PanicUntil = _timing.CurTime + TimeSpan.FromSeconds(10);
            comp.PanicFrom = hall.Pos;

            if (_mobState.IsAlive(partisan))
                StartHunt(partisan, hall.Pos);
        };

        StartActivity(activity);
        return true;
    }

    private void StartHunt(EntityUid partisan, Vector2 hallPos)
    {
        var now = _timing.CurTime;

        if (TryComp<MarineComponent>(partisan, out var target))
            target.Wanted = true;

        _hunts.Add((partisan, now + TimeSpan.FromSeconds(150)));

        var query = EntityQueryEnumerator<MarineComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Team != "north" || comp.Passive || comp.Static || comp.Scene != null || HasComp<ActorComponent>(uid) || _mobState.IsDead(uid))
                continue;

            if ((_transform.GetWorldPosition(xform) - hallPos).Length() > 100f)
                continue;

            comp.Hunt = partisan;
            comp.HeardPoint = hallPos;
            comp.HeardUntil = now + TimeSpan.FromSeconds(20);
        }
    }

    private void UpdatePolice(TimeSpan now)
    {
        for (var i = _hunts.Count - 1; i >= 0; i--)
        {
            var (partisan, until) = _hunts[i];

            if (!Exists(partisan) || _mobState.IsDead(partisan) || now >= until)
            {
                EndHunt(partisan);
                _hunts.RemoveAt(i);
            }
        }

        var query = EntityQueryEnumerator<MarineComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Team != "north" || comp.Passive || comp.Static || comp.Scene != null || HasComp<ActorComponent>(uid) || _mobState.IsDead(uid))
                continue;

            var pos = _transform.GetWorldPosition(xform);

            if (comp.Hunt is { } hunted)
            {
                if (Exists(hunted) && (_transform.GetWorldPosition(hunted) - pos).Length() < 2.6f)
                {
                    CatchPartisan(uid, hunted, pos, xform.MapID);
                    break;
                }

                continue;
            }

            if (!comp.Police)
                continue;

            PatrolStep(uid, comp, pos, xform.MapID, now);
        }
    }

    private void PatrolStep(EntityUid uid, MarineComponent comp, Vector2 pos, MapId map, TimeSpan now)
    {
        if (comp.PatrolPoints == null)
        {
            var points = Points(MarinePointKind.Patrol);
            points.RemoveAll(p => p.Comp.Route != comp.PatrolRoute);
            points.Sort((a, b) => a.Comp.Order.CompareTo(b.Comp.Order));
            comp.PatrolPoints = points.ConvertAll(p => p.Pos);
        }

        if (comp.PatrolPoints.Count == 0)
            return;

        if (comp.Target != null || comp.HeardPoint != null)
        {
            comp.PatrolGoal = null;
            return;
        }

        if (comp.PatrolGoal == null)
        {
            if (now >= comp.PatrolWait)
            {
                comp.PatrolIndex = (comp.PatrolIndex + 1) % comp.PatrolPoints.Count;
                comp.PatrolGoal = comp.PatrolPoints[comp.PatrolIndex];
            }
        }
        else if ((comp.PatrolGoal.Value - pos).Length() < 1.8f)
        {
            comp.PatrolGoal = null;
            comp.PatrolWait = now + TimeSpan.FromSeconds(_random.NextFloat(3f, 8f));
        }

        if (now >= comp.NextEncounter)
            TryEncounter(uid, comp, pos, map, now);
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

    private void TryEncounter(EntityUid uid, MarineComponent comp, Vector2 pos, MapId map, TimeSpan now)
    {
        comp.NextEncounter = now + TimeSpan.FromSeconds(_random.NextFloat(20f, 40f));

        var candidates = new List<EntityUid>();
        foreach (var (civ, civComp) in _lookup.GetEntitiesInRange<MarineComponent>(new MapCoordinates(pos, map), 7f, LookupFlags.Dynamic | LookupFlags.Uncontained))
        {
            if (!civComp.Passive || civComp.Static || civComp.Frozen || civComp.Scene != null || civComp.CivMode == MarineCivilianMode.Busy
                || HasComp<ActorComponent>(civ) || _mobState.IsDead(civ) || now < civComp.PanicUntil)
            {
                continue;
            }

            if (HasSight(map, pos, _transform.GetWorldPosition(civ), uid))
                candidates.Add(civ);
        }

        if (candidates.Count == 0 || !_random.Prob(0.5f))
            return;

        comp.NextEncounter = now + TimeSpan.FromSeconds(_random.NextFloat(70f, 160f));

        var victim = _random.Pick(candidates);
        var soldiers = new List<EntityUid> { uid };

        foreach (var (other, otherComp) in _lookup.GetEntitiesInRange<MarineComponent>(new MapCoordinates(pos, map), 6f, LookupFlags.Dynamic | LookupFlags.Uncontained))
        {
            if (other != uid && soldiers.Count < 2 && otherComp.Police && otherComp.Scene == null && otherComp.Target == null
                && otherComp.Hunt == null && !HasComp<ActorComponent>(other) && !_mobState.IsDead(other))
            {
                soldiers.Add(other);
            }
        }

        var roll = _random.NextFloat();
        var kind = roll < 0.50f ? MarineSceneKind.Search
            : roll < 0.75f ? MarineSceneKind.Interrogation
            : roll < 0.90f ? MarineSceneKind.Beating
            : MarineSceneKind.Execution;

        _scene.StartDynamic(kind, soldiers, new List<EntityUid> { victim });
    }

    private void CatchPartisan(EntityUid police, EntityUid partisan, Vector2 pos, MapId map)
    {
        var soldiers = new List<EntityUid> { police };

        foreach (var (other, otherComp) in _lookup.GetEntitiesInRange<MarineComponent>(new MapCoordinates(pos, map), 6f, LookupFlags.Dynamic | LookupFlags.Uncontained))
        {
            if (other != police && soldiers.Count < 3 && otherComp.Team == "north" && !otherComp.Passive && !otherComp.Static
                && otherComp.Scene == null && !HasComp<ActorComponent>(other) && !_mobState.IsDead(other))
            {
                soldiers.Add(other);
            }
        }

        EndHunt(partisan);
        _hunts.RemoveAll(h => h.Partisan == partisan);

        if (TryComp<MarineComponent>(partisan, out var comp))
            comp.CivMode = MarineCivilianMode.Idle;

        _scene.StartDynamic(MarineSceneKind.Execution, soldiers, new List<EntityUid> { partisan });
    }

    private void EndHunt(EntityUid partisan)
    {
        if (Exists(partisan) && TryComp<MarineComponent>(partisan, out var target))
            target.Wanted = false;

        var query = EntityQueryEnumerator<MarineComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            if (comp.Hunt == partisan)
                comp.Hunt = null;
        }
    }
}
