// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.Marines;

public sealed partial class MarineSceneSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private MarineSystem _marines = default!;

    private const int ShotMask = (int) CollisionGroup.BulletImpassable;

    private TimeSpan _nextScan;

    private static readonly string[] SearchOrders =
    {
        "Стоять! Руки за голову!",
        "Документы. Живо.",
        "Не двигаться. Обыск.",
    };

    private static readonly string[] SearchPleas =
    {
        "Я просто иду домой...",
        "У меня ничего нет, клянусь!",
        "Пожалуйста, не трогайте меня...",
    };

    private static readonly string[] AskLines =
    {
        "Имя! Быстро!",
        "Где прячутся южане?",
        "Кто тебе помогал? Говори!",
    };

    private static readonly string[] AnswerLines =
    {
        "Я ничего не знаю, пожалуйста...",
        "Клянусь, я никого не видел!",
        "Я просто живу здесь...",
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MarineSceneComponent, MapInitEvent>(OnInit);
    }

    private void OnInit(Entity<MarineSceneComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Dynamic)
            return;

        var xform = Transform(ent);
        var rotation = xform.LocalRotation;
        var right = rotation.RotateVec(new Vector2(1f, 0f));
        var front = rotation.RotateVec(new Vector2(0f, -1f));

        for (var i = 0; i < ent.Comp.CivilianCount; i++)
        {
            var offset = right * ((i - (ent.Comp.CivilianCount - 1) / 2f) * 1.2f);
            var civilian = Spawn(ent.Comp.Civilian, xform.Coordinates.Offset(offset));
            _transform.SetLocalRotation(civilian, front.ToWorldAngle());

            if (TryComp<MarineComponent>(civilian, out var comp))
            {
                comp.Frozen = true;
                comp.Scene = ent;
            }

            ent.Comp.Civilians.Add(civilian);
        }

        for (var j = 0; j < ent.Comp.SoldierCount; j++)
        {
            var offset = front * 2.6f + right * ((j - (ent.Comp.SoldierCount - 1) / 2f) * 1.6f);
            var proto = j == 0 && ent.Comp.WithOfficer ? ent.Comp.Officer : ent.Comp.Soldier;
            var soldier = Spawn(proto, xform.Coordinates.Offset(offset));
            _transform.SetLocalRotation(soldier, (-front).ToWorldAngle());

            if (TryComp<MarineComponent>(soldier, out var comp))
                comp.Scene = ent;

            ent.Comp.Soldiers.Add(soldier);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var scan = now >= _nextScan;

        if (scan)
            _nextScan = now + TimeSpan.FromSeconds(0.5);

        var query = EntityQueryEnumerator<MarineSceneComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var scene, out var xform))
        {
            if (scene.Done)
                continue;

            if (!scene.Started)
            {
                if (!scan)
                    continue;

                if (AnySoldierHurt(scene, TimeSpan.Zero))
                {
                    Finish(uid, scene, now);
                    continue;
                }

                if (PlayerWatching(xform, scene.TriggerRange))
                    Begin(uid, scene, xform, now);

                continue;
            }

            if (scan && (AnySoldierHurt(scene, scene.StartedAt) || EnemyNear(scene)))
            {
                Finish(uid, scene, now);
                continue;
            }

            if (scene.Step >= scene.Steps.Count)
            {
                Finish(uid, scene, now);
                continue;
            }

            if (scene.Steps[scene.Step]())
                scene.Step++;
        }
    }

    private bool AnySoldierHurt(MarineSceneComponent scene, TimeSpan since)
    {
        foreach (var soldier in scene.Soldiers)
        {
            if (!Exists(soldier) || !_mobState.IsAlive(soldier))
                return true;

            if (TryComp<MarineComponent>(soldier, out var comp) && comp.LastDamaged != TimeSpan.Zero && comp.LastDamaged >= since)
                return true;
        }

        return false;
    }

    private bool EnemyNear(MarineSceneComponent scene)
    {
        foreach (var soldier in scene.Soldiers)
        {
            if (!Exists(soldier))
                continue;

            var coords = _transform.GetMapCoordinates(soldier);
            foreach (var (other, marine) in _lookup.GetEntitiesInRange<MarineComponent>(coords, 9f, LookupFlags.Dynamic | LookupFlags.Uncontained))
            {
                if (marine.Team == "south" && !marine.Passive && _mobState.IsAlive(other))
                    return true;
            }
        }

        return false;
    }

    private bool PlayerWatching(TransformComponent sceneXform, float range)
    {
        var scenePos = _transform.GetWorldPosition(sceneXform);
        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();

        while (players.MoveNext(out var player, out _, out var state, out var xform))
        {
            if (xform.MapID != sceneXform.MapID || state.CurrentState != MobState.Alive || HasComp<MarineComponent>(player))
                continue;

            var pos = _transform.GetWorldPosition(xform);
            var delta = scenePos - pos;
            var dist = delta.Length();

            if (dist > range)
                continue;

            if (dist < 1f || HasSight(sceneXform.MapID, pos, delta / dist, dist, player))
                return true;
        }

        return false;
    }

    private bool HasSight(MapId map, Vector2 from, Vector2 dir, float dist, EntityUid ignore)
    {
        var ray = new CollisionRay(from, dir, ShotMask);

        foreach (var result in _physics.IntersectRay(map, ray, dist, ignore, false))
        {
            if (HasComp<MobStateComponent>(result.HitEntity))
                continue;

            if (result.Distance < dist - 0.6f)
                return false;
        }

        return true;
    }

    public bool StartDynamic(MarineSceneKind kind, List<EntityUid> soldiers, List<EntityUid> civilians)
    {
        if (soldiers.Count == 0 || civilians.Count == 0)
            return false;

        foreach (var soldier in soldiers)
        {
            if (!TryComp<MarineComponent>(soldier, out var soldierComp) || soldierComp.Scene != null)
                return false;
        }

        foreach (var civilian in civilians)
        {
            if (!TryComp<MarineComponent>(civilian, out var civilianComp) || civilianComp.Scene != null)
                return false;
        }

        var now = _timing.CurTime;
        var scene = Spawn("MarineSceneDynamic", Transform(soldiers[0]).Coordinates);

        if (!TryComp<MarineSceneComponent>(scene, out var comp))
        {
            QueueDel(scene);
            return false;
        }

        comp.Dynamic = true;
        comp.Kind = kind;
        comp.Soldiers = new List<EntityUid>(soldiers);
        comp.Civilians = new List<EntityUid>(civilians);
        comp.Started = true;
        comp.StartedAt = now;

        foreach (var soldier in soldiers)
        {
            var soldierComp = Comp<MarineComponent>(soldier);
            soldierComp.Scene = scene;
            soldierComp.PatrolGoal = null;
            soldierComp.ScriptGoal = null;
        }

        foreach (var civilian in civilians)
        {
            var civilianComp = Comp<MarineComponent>(civilian);
            civilianComp.Scene = scene;
            civilianComp.Frozen = true;
            civilianComp.CivMode = MarineCivilianMode.Idle;
            civilianComp.Partner = null;
            civilianComp.ScriptGoal = null;
        }

        comp.Steps = BuildSteps(comp);
        return true;
    }

    private void Begin(EntityUid uid, MarineSceneComponent scene, TransformComponent xform, TimeSpan now)
    {
        scene.Started = true;
        scene.StartedAt = now;
        scene.Step = 0;
        scene.Steps = BuildSteps(scene);
    }

    private void Finish(EntityUid uid, MarineSceneComponent scene, TimeSpan now)
    {
        scene.Done = true;

        if (scene.Dynamic)
            QueueDel(uid);

        var center = _transform.GetWorldPosition(uid);

        foreach (var soldier in scene.Soldiers)
        {
            if (!TryComp<MarineComponent>(soldier, out var comp))
                continue;

            comp.Scene = null;
            comp.ScriptGoal = null;
            comp.Running = true;
        }

        foreach (var civilian in scene.Civilians)
        {
            if (!Exists(civilian) || !TryComp<MarineComponent>(civilian, out var comp))
                continue;

            comp.Scene = null;
            comp.Frozen = false;
            comp.ScriptGoal = null;

            if (!_mobState.IsAlive(civilian))
                continue;

            _standing.Stand(civilian);
            comp.PanicUntil = now + TimeSpan.FromSeconds(10);
            comp.PanicFrom = center;
        }
    }

    private bool Alive(EntityUid uid)
    {
        return Exists(uid) && _mobState.IsAlive(uid);
    }

    private void Say(EntityUid actor, string text)
    {
        if (Alive(actor))
            _chat.TrySendInGameICMessage(actor, text, InGameICChatType.Speak, false);
    }

    private void Face(EntityUid actor, EntityUid target)
    {
        if (!Exists(actor) || !Exists(target))
            return;

        var delta = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(actor);
        if (delta.LengthSquared() > 0.01f)
            _transform.SetWorldRotation(actor, delta.ToWorldAngle());
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

    private Func<bool> MoveNear(EntityUid actor, EntityUid victim, float distance)
    {
        Vector2? goal = null;
        TimeSpan? end = null;

        return () =>
        {
            if (!Alive(actor) || !Exists(victim) || !TryComp<MarineComponent>(actor, out var comp))
                return true;

            var now = _timing.CurTime;
            end ??= now + TimeSpan.FromSeconds(9);

            if (goal == null)
            {
                var victimPos = _transform.GetWorldPosition(victim);
                var actorPos = _transform.GetWorldPosition(actor);
                var away = actorPos - victimPos;
                var dir = away.LengthSquared() > 0.01f ? Vector2.Normalize(away) : new Vector2(0f, -1f);
                goal = victimPos + dir * distance;
            }

            comp.ScriptGoal = goal;

            var arrived = (_transform.GetWorldPosition(actor) - goal.Value).Length() < 0.9f;

            if (!arrived && now < end)
                return false;

            comp.ScriptGoal = null;
            Face(actor, victim);
            return true;
        };
    }

    private Func<bool> Hit(EntityUid attacker, EntityUid victim, bool knockDown)
    {
        return () =>
        {
            if (!Alive(attacker) || !Exists(victim) || !_mobState.IsAlive(victim))
                return true;

            Face(attacker, victim);
            _marines.Frighten(_transform.GetWorldPosition(victim), Transform(victim).MapID, 14f, 0.3f, victim, false);

            var damage = new DamageSpecifier();
            damage.DamageDict["Blunt"] = FixedPoint2.New(6);
            _damageable.TryChangeDamage(victim, damage, true, false, ignoreGlobalModifiers: true);

            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Weapons/punch1.ogg"), victim);
            _popup.PopupEntity(Loc.GetString("marine-scene-hit"), victim, PopupType.MediumCaution);
            _chat.TryEmoteWithChat(victim, "Scream", forceEmote: true);

            if (knockDown)
                _standing.Down(victim, false, false);

            return true;
        };
    }

    private Func<bool> Burst(EntityUid shooter, EntityUid victim, int shots)
    {
        var left = shots;
        TimeSpan? next = null;

        return () =>
        {
            if (!Alive(shooter) || !Exists(victim))
                return true;

            var now = _timing.CurTime;

            if (!_mobState.IsAlive(victim) && !(_mobState.IsCritical(victim) && left > 0))
                return true;

            next ??= now;

            if (now < next)
                return false;

            if (left == shots)
                _marines.Frighten(_transform.GetWorldPosition(victim), Transform(victim).MapID, 18f, 0.5f, victim, false);

            _marines.ForceShoot(shooter, victim);
            left--;
            next = now + TimeSpan.FromSeconds(0.28);

            return left <= 0;
        };
    }

    private List<Func<bool>> BuildSteps(MarineSceneComponent scene)
    {
        var steps = new List<Func<bool>>();
        var soldiers = scene.Soldiers.FindAll(Alive);
        var civilians = scene.Civilians.FindAll(Alive);

        if (soldiers.Count == 0 || civilians.Count == 0)
            return steps;

        var lead = soldiers[0];
        var victim = civilians[0];

        switch (scene.Kind)
        {
            case MarineSceneKind.Search:
                steps.Add(MoveNear(lead, victim, 1.3f));
                steps.Add(Do(() => Say(lead, _random.Pick(SearchOrders))));
                steps.Add(Wait(2f));
                steps.Add(Do(() => Say(victim, _random.Pick(SearchPleas))));
                steps.Add(Wait(2f));
                steps.Add(Do(() => _popup.PopupEntity(Loc.GetString("marine-scene-search"), victim, PopupType.Medium)));
                steps.Add(Wait(3.5f));

                if (_random.Prob(0.4f))
                {
                    steps.Add(Do(() => Say(lead, "Что это?! Ты укрываешь повстанцев!")));
                    steps.Add(Wait(2f));
                    AddPunishment(steps, lead, victim, soldiers);
                }
                else
                {
                    steps.Add(Do(() => Say(lead, "Свободен. Пошёл вон.")));
                    steps.Add(Wait(1.5f));
                }

                break;

            case MarineSceneKind.Interrogation:
                steps.Add(MoveNear(lead, victim, 1.3f));
                steps.Add(Do(() => Say(lead, AskLines[0])));
                steps.Add(Wait(2.2f));
                steps.Add(Do(() => Say(victim, _random.Pick(AnswerLines))));
                steps.Add(Wait(2.5f));
                steps.Add(Do(() => Say(lead, AskLines[1])));
                steps.Add(Wait(2.2f));
                steps.Add(Do(() => Say(victim, _random.Pick(AnswerLines))));
                steps.Add(Wait(2.5f));
                steps.Add(Do(() => Say(lead, "Врёшь.")));
                steps.Add(Wait(1.5f));

                if (_random.Prob(0.2f))
                {
                    steps.Add(Do(() => Say(lead, "Пошёл вон. В следующий раз заговоришь.")));
                    steps.Add(Wait(1.5f));
                }
                else
                {
                    AddPunishment(steps, lead, victim, soldiers);
                }

                break;

            case MarineSceneKind.Beating:
                steps.Add(MoveNear(lead, victim, 1.2f));
                steps.Add(Do(() => Say(lead, "Ты знал и молчал!")));
                steps.Add(Wait(1.6f));
                AddBeating(steps, lead, victim);
                break;

            case MarineSceneKind.Execution:
                steps.Add(Do(() =>
                {
                    foreach (var civilian in civilians)
                    {
                        _standing.Down(civilian, false, false);
                    }
                }));
                steps.Add(Wait(1.5f));
                steps.Add(Do(() => Say(lead, "За пособничество повстанцам: расстрел!")));
                steps.Add(Wait(3f));

                for (var i = 0; i < civilians.Count; i++)
                {
                    var target = civilians[i];
                    var shooter = soldiers[i % soldiers.Count];
                    steps.Add(Do(() => Face(shooter, target)));
                    steps.Add(Wait(1.2f));
                    steps.Add(Burst(shooter, target, 5));
                    steps.Add(Wait(1.6f));
                }

                steps.Add(Do(() => Say(lead, "Убрать тела.")));
                steps.Add(Wait(2f));
                break;
        }

        return steps;
    }

    private void AddBeating(List<Func<bool>> steps, EntityUid attacker, EntityUid victim)
    {
        steps.Add(Hit(attacker, victim, false));
        steps.Add(Wait(1.1f));
        steps.Add(Hit(attacker, victim, true));
        steps.Add(Wait(1.1f));
        steps.Add(Hit(attacker, victim, true));
        steps.Add(Wait(1.4f));
        steps.Add(Do(() => Say(attacker, "Это тебе урок.")));
        steps.Add(Wait(2f));
    }

    private void AddPunishment(List<Func<bool>> steps, EntityUid lead, EntityUid victim, List<EntityUid> soldiers)
    {
        if (_random.Prob(0.35f))
        {
            steps.Add(Do(() => _standing.Down(victim, false, false)));
            steps.Add(Do(() => Say(lead, "Расстрелять.")));
            steps.Add(Wait(2f));

            var shooter = soldiers[soldiers.Count - 1];
            steps.Add(Do(() => Face(shooter, victim)));
            steps.Add(Wait(1f));
            steps.Add(Burst(shooter, victim, 5));
            steps.Add(Wait(1.5f));
            return;
        }

        AddBeating(steps, lead, victim);
    }
}
