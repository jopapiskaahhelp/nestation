// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Shared._Nestation.Pain;
using Content.Shared.Atmos.Components;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Systems;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Medical;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Rejuvenate;
using Content.Shared.Sound.Components;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Content.Shared.Traits.Assorted;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Nestation.Pain;

public sealed partial class PainSystem : SharedPainSystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private SleepingSystem _sleeping = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private VomitSystem _vomit = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    private readonly record struct Profile(float Gain, float Cap, float Decay, float Floor);

    private const float Step = 0.25f;

    private const float EnterWeak = 5f;
    private const float EnterMedium = 25f;
    private const float EnterStrong = 55f;
    private const float EnterExtreme = 85f;

    private const float LeaveWeak = 3f;
    private const float LeaveMedium = 22f;
    private const float LeaveStrong = 50f;
    private const float LeaveExtreme = 78f;

    private const float ParacetamolCeiling = EnterMedium;
    private const float TramadolCeiling = EnterStrong;

    private const float OtherChannelShare = 0.15f;

    private const float BurnWindow = 2.5f;
    private const float BurnMomentumStep = 0.5f;
    private const float BurnMomentumMax = 4f;
    private const float BurnMinDamage = 0.3f;

    private const float PiercingWound = 3f;
    private const float PiercingCreepFrom = 50f;
    private const float PiercingCreep = 0.7f;
    private const float PiercingCrash = 58f;
    private const float SlashCrash = 30f;

    private const float HealRelief = 1.5f;

    private const int AdrenalineMinHits = 2;
    private const float AdrenalineMinPoints = 25f;
    private const float AdrenalineSingleHit = 60f;
    private const float AdrenalineHitPoints = 3f;
    private const float AdrenalineWindowTime = 10f;
    private const float AdrenalineCooldownTime = 45f;
    private const float AdrenalineSpeed = 1.2f;

    private const float MoanInterval = 120f;
    private const float WeakPulseInterval = 25f;
    private const float FireGraceTime = 4f;

    private const float FentanylSpeed = 0.55f;
    private const float TramadolStamina = 0.55f;
    private const float DrugPresenceTime = 2.5f;

    private const float HypotensionBlood = 0.75f;
    private const float ArrestBlood = 0.6f;
    private const float ArrestChanceScale = 0.5f;
    private const float ArrestChanceMax = 0.4f;

    private const float DepressionFaint = 3f;
    private const float DepressionDamage = 1.5f;
    private const float DepressionClearTime = 15f;
    private const float ArrestDamage = 3f;

    private static readonly Profile[] Profiles = new Profile[7];

    private static readonly float[] OnsetMin = { 15f, 5f, 2f };
    private static readonly float[] OnsetMax = { 20f, 10f, 3f };

    private static readonly EntProtoId Dizzy = "StatusEffectWoozy";

    private readonly List<PainKind> _scratch = new();

    public PainSystem()
    {
        Profiles[(int) PainKind.Blunt] = new Profile(1.8f, 70f, 0.4f, 0f);
        Profiles[(int) PainKind.Slash] = new Profile(2.2f, 80f, 0.07f, 0f);
        Profiles[(int) PainKind.Piercing] = new Profile(1.3f, 100f, 0.03f, 0f);
        Profiles[(int) PainKind.Burn] = new Profile(2f, 100f, 0.15f, 30f);
        Profiles[(int) PainKind.Acid] = new Profile(2f, 100f, 0.15f, 30f);
        Profiles[(int) PainKind.Shock] = new Profile(5f, 82f, 3.5f, 66f);
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PainComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<PainComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<PainComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<PainComponent, MoveInputEvent>(OnMoveInput);
    }

    private static PainKind KindOf(string damageType)
    {
        return damageType switch
        {
            "Blunt" => PainKind.Blunt,
            "Slash" => PainKind.Slash,
            "Piercing" => PainKind.Piercing,
            "Heat" => PainKind.Burn,
            "Caustic" => PainKind.Acid,
            "Shock" => PainKind.Shock,
            _ => PainKind.None
        };
    }

    private static float AdrenalineWeight(PainKind kind)
    {
        return kind switch
        {
            PainKind.Piercing => 1f,
            PainKind.Slash => 0.9f,
            PainKind.Blunt => 0.5f,
            _ => 0f
        };
    }

    private void OnDamageChanged(Entity<PainComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageDelta == null || _mobState.IsDead(ent.Owner))
            return;

        var comp = ent.Comp;
        var adrenaline = 0f;

        foreach (var (type, amount) in args.DamageDelta.DamageDict)
        {
            var kind = KindOf(type.Id);

            if (kind == PainKind.None)
                continue;

            var value = amount.Float();

            if (value > 0f)
            {
                AddPain(comp, kind, value);
                adrenaline += AdrenalineWeight(kind) * value;
            }
            else if (value < 0f)
            {
                RelievePain(comp, kind, -value);
            }
        }

        if (adrenaline >= AdrenalineHitPoints)
        {
            comp.AdrenalineHits++;
            comp.AdrenalinePoints += adrenaline;
            comp.AdrenalineWindow = AdrenalineWindowTime;

            if (!comp.Adrenaline &&
                !comp.Shock &&
                comp.AdrenalineCooldown <= 0f &&
                ((comp.AdrenalineHits >= AdrenalineMinHits && comp.AdrenalinePoints >= AdrenalineMinPoints) ||
                 adrenaline >= AdrenalineSingleHit))
            {
                StartAdrenaline(ent.Owner, comp);
            }
        }
    }

    private void AddPain(PainComponent comp, PainKind kind, float amount)
    {
        var profile = Profiles[(int) kind];
        comp.Channels.TryGetValue(kind, out var current);
        float add;

        if (kind is PainKind.Burn or PainKind.Acid)
        {
            if (amount < BurnMinDamage)
                return;

            if (comp.BurnIdle < BurnWindow)
                comp.BurnMomentum = MathF.Min(BurnMomentumMax, comp.BurnMomentum + BurnMomentumStep);
            else
                comp.BurnMomentum = 1f;

            comp.BurnIdle = 0f;
            add = (1f + amount * 0.6f) * 0.8f * comp.BurnMomentum;
            current = MathF.Max(current, profile.Floor);
        }
        else if (kind == PainKind.Shock)
        {
            add = amount * profile.Gain;
            current = MathF.Max(current, profile.Floor);
        }
        else
        {
            add = amount * profile.Gain;
        }

        comp.Channels[kind] = MathF.Min(profile.Cap, current + add);
    }

    private void RelievePain(PainComponent comp, PainKind kind, float amount)
    {
        if (!comp.Channels.TryGetValue(kind, out var current))
            return;

        current -= amount * Profiles[(int) kind].Gain * HealRelief;

        if (current <= 0f)
            comp.Channels.Remove(kind);
        else
            comp.Channels[kind] = current;
    }

    private void StartAdrenaline(EntityUid uid, PainComponent comp)
    {
        comp.Adrenaline = true;
        comp.AdrenalineTimer = Math.Clamp(10f + comp.AdrenalinePoints * 0.25f, 12f, 30f);
        comp.AdrenalinePoints = 0f;
        comp.AdrenalineHits = 0;
        comp.AdrenalineWindow = 0f;
        Dirty(uid, comp);
    }

    private void EndAdrenaline(EntityUid uid, PainComponent comp)
    {
        comp.Adrenaline = false;
        comp.AdrenalineCooldown = AdrenalineCooldownTime;

        if (comp.Channels.TryGetValue(PainKind.Piercing, out var piercing) && piercing > 8f)
            comp.Channels[PainKind.Piercing] = MathF.Max(piercing, PiercingCrash);

        if (comp.Channels.TryGetValue(PainKind.Slash, out var slash) && slash > 10f)
            comp.Channels[PainKind.Slash] = MathF.Max(slash, SlashCrash);

        Dirty(uid, comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<PainComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            comp.Accumulator += frameTime;

            if (comp.Accumulator < Step)
                continue;

            var dt = comp.Accumulator;
            comp.Accumulator = 0f;
            Tick(uid, comp, dt);
        }
    }

    private void Tick(EntityUid uid, PainComponent comp, float dt)
    {
        if (_mobState.IsDead(uid))
        {
            if (comp.Channels.Count > 0 ||
                comp.Level != PainLevel.None ||
                comp.ForcedSleep ||
                comp.Depression ||
                comp.Arrest ||
                comp.Adrenaline ||
                comp.StaminaApplied ||
                comp.Muffle > 0f ||
                MathF.Abs(comp.SpeedModifier - 1f) > 0.005f)
            {
                ResetState(uid, comp);
            }

            return;
        }

        comp.BurnIdle += dt;

        if (comp.BurnIdle > BurnWindow)
            comp.BurnMomentum = 1f;

        if (comp.AdrenalineWindow > 0f)
        {
            comp.AdrenalineWindow -= dt;

            if (comp.AdrenalineWindow <= 0f)
            {
                comp.AdrenalinePoints = 0f;
                comp.AdrenalineHits = 0;
            }
        }

        if (comp.AdrenalineCooldown > 0f)
            comp.AdrenalineCooldown -= dt;

        if (comp.TramadolTurnCooldown > 0f)
            comp.TramadolTurnCooldown -= dt;

        if (TryComp<FlammableComponent>(uid, out var flammable) && flammable.OnFire)
            comp.FireGrace = FireGraceTime;
        else if (comp.FireGrace > 0f)
            comp.FireGrace -= dt;

        TickDrugs(comp, dt);
        TickChannels(uid, comp, dt);

        if (comp.Adrenaline)
        {
            comp.AdrenalineTimer -= dt;

            if (comp.AdrenalineTimer <= 0f)
                EndAdrenaline(uid, comp);
        }

        comp.SecondAccumulator += dt;
        var secondTick = comp.SecondAccumulator >= 1f;
        var secondDt = comp.SecondAccumulator;

        if (secondTick)
            comp.SecondAccumulator = 0f;

        UpdateLevel(uid, comp, dt);

        if (secondTick)
            TickSecond(uid, comp, secondDt);

        UpdateForcedSleep(uid, comp);
        UpdateSpeed(uid, comp);
        UpdateStamina(uid, comp);
    }

    private void TickChannels(EntityUid uid, PainComponent comp, float dt)
    {
        if (comp.Channels.Count == 0)
            return;

        var wound = false;

        if (comp.Channels.ContainsKey(PainKind.Piercing) &&
            Damage(GetDamage(uid), "Piercing") >= PiercingWound)
        {
            wound = true;
        }

        _scratch.Clear();
        _scratch.AddRange(comp.Channels.Keys);

        foreach (var kind in _scratch)
        {
            var profile = Profiles[(int) kind];
            var value = comp.Channels[kind] - profile.Decay * dt;

            if (kind == PainKind.Piercing && wound && !comp.Adrenaline && value >= PiercingCreepFrom)
                value = MathF.Min(profile.Cap, value + (PiercingCreep + profile.Decay) * dt);

            if (value <= 0f)
                comp.Channels.Remove(kind);
            else
                comp.Channels[kind] = value;
        }
    }

    private static void TickDrugs(PainComponent comp, float dt)
    {
        for (var i = 0; i < comp.DrugPresence.Length; i++)
        {
            if (comp.DrugPresence[i] <= 0f)
                continue;

            comp.DrugPresence[i] -= dt;

            if (comp.DrugOnset[i] > 0f)
                comp.DrugOnset[i] = MathF.Max(0f, comp.DrugOnset[i] - dt);
        }
    }

    private static bool Active(PainComponent comp, PainkillerDrug drug)
    {
        var i = (int) drug;
        return comp.DrugPresence[i] > 0f && comp.DrugOnset[i] <= 0f;
    }

    private static void Compute(PainComponent comp, out float raw, out PainKind dominant)
    {
        var max = 0f;
        var sum = 0f;
        dominant = PainKind.None;

        foreach (var (kind, value) in comp.Channels)
        {
            sum += value;

            if (value > max)
            {
                max = value;
                dominant = kind;
            }
        }

        raw = MathF.Min(100f, max + OtherChannelShare * (sum - max));
    }

    private static float Effective(PainComponent comp, float raw)
    {
        if (comp.Adrenaline || comp.Numb)
            return 0f;

        if (Active(comp, PainkillerDrug.Fentanyl))
            return 0f;

        if (Active(comp, PainkillerDrug.Tramadol) && raw < TramadolCeiling)
            return 0f;

        if (Active(comp, PainkillerDrug.Paracetamol) && raw < ParacetamolCeiling)
            return 0f;

        return raw;
    }

    private static float Enter(PainLevel level)
    {
        return level switch
        {
            PainLevel.Weak => EnterWeak,
            PainLevel.Medium => EnterMedium,
            PainLevel.Strong => EnterStrong,
            PainLevel.Extreme => EnterExtreme,
            _ => 0f
        };
    }

    private static float Leave(PainLevel level)
    {
        return level switch
        {
            PainLevel.Weak => LeaveWeak,
            PainLevel.Medium => LeaveMedium,
            PainLevel.Strong => LeaveStrong,
            PainLevel.Extreme => LeaveExtreme,
            _ => 0f
        };
    }

    private static PainLevel NextLevel(PainLevel current, float effective)
    {
        var level = current;

        while (level < PainLevel.Extreme && effective >= Enter(level + 1))
        {
            level++;
        }

        while (level > PainLevel.None && effective < Leave(level))
        {
            level--;
        }

        return level;
    }

    private void UpdateLevel(EntityUid uid, PainComponent comp, float dt)
    {
        Compute(comp, out var raw, out var dominant);
        comp.RawPain = raw;

        var alive = _mobState.IsAlive(uid);
        var effective = alive ? Effective(comp, raw) : 0f;
        var level = NextLevel(comp.Level, effective);
        var changed = false;

        if (level != comp.Level)
        {
            OnLevelChanged(uid, comp, comp.Level, level);
            comp.Level = level;
            changed = true;
        }

        if (dominant != comp.Dominant && dominant != PainKind.None)
        {
            comp.Dominant = dominant;
            changed = true;
        }

        var intensity = effective / 100f;

        if (MathF.Abs(intensity - comp.Intensity) > 0.02f || (effective <= 0f && comp.Intensity > 0f))
        {
            comp.Intensity = intensity;
            changed = true;
        }

        var shock = level == PainLevel.Extreme && alive && comp.FireGrace <= 0f;

        if (shock != comp.Shock)
        {
            comp.Shock = shock;
            changed = true;
        }

        if (level == PainLevel.Weak)
        {
            comp.WeakPulseTimer -= dt;

            if (comp.WeakPulseTimer <= 0f)
            {
                comp.WeakPulseTimer = WeakPulseInterval;
                comp.WeakPulse++;
                changed = true;
            }
        }

        if (level is PainLevel.Medium or PainLevel.Strong)
        {
            comp.MoanTimer -= dt;

            if (comp.MoanTimer <= 0f)
            {
                comp.MoanTimer = MoanInterval + _random.NextFloat(-10f, 10f);
                _chat.TryEmoteWithChat(uid, "PainMoan", ChatTransmitRange.Normal, ignoreActionBlocker: true, forceEmote: true);
            }
        }

        if (changed)
            Dirty(uid, comp);
    }

    private void OnLevelChanged(EntityUid uid, PainComponent comp, PainLevel from, PainLevel to)
    {
        if (to == PainLevel.Weak && from == PainLevel.None)
        {
            comp.WeakPulse++;
            comp.WeakPulseTimer = WeakPulseInterval;
            _popup.PopupEntity(Loc.GetString("pain-weak-popup"), uid, uid);
        }

        if (to == PainLevel.Medium && from < PainLevel.Medium)
            comp.MoanTimer = MoanInterval + _random.NextFloat(-10f, 10f);
    }

    private void TickSecond(EntityUid uid, PainComponent comp, float dt)
    {
        comp.Numb = _status.TryEffectsWithComp<PainNumbnessStatusEffectComponent>(uid, out _);

        var dizzy = 0f;

        if (comp.Level == PainLevel.Strong)
            dizzy = MathF.Max(dizzy, 6f);

        if (Active(comp, PainkillerDrug.Fentanyl))
            dizzy = MathF.Max(dizzy, 3f);

        TickConditions(uid, comp, dt, ref dizzy);
        TickBreathing(uid, comp, dt);

        if (dizzy > 0f && _mobState.IsAlive(uid))
            _status.TrySetStatusEffectDuration(uid, Dizzy, TimeSpan.FromSeconds(dizzy));
    }

#pragma warning disable CS0618
    private DamageSpecifier GetDamage(EntityUid uid)
    {
        return _damageable.GetAllDamage(uid);
    }
#pragma warning restore CS0618

    private static float Damage(DamageSpecifier damage, string type)
    {
        return damage.DamageDict.TryGetValue(type, out var value) ? value.Float() : 0f;
    }

    private void TickConditions(EntityUid uid, PainComponent comp, float dt, ref float dizzy)
    {
        if (!HasComp<DamageableComponent>(uid))
            return;

        var damage = GetDamage(uid);
        var radiation = Damage(damage, "Radiation");
        var poison = Damage(damage, "Poison");
        var asphyxia = Damage(damage, "Asphyxiation");
        var cold = Damage(damage, "Cold");

        var speed = 1f;
        var stamina = 1f;
        var nausea = 0f;

        if (radiation >= 60f)
        {
            speed *= 0.7f;
            stamina *= 0.6f;
            nausea = 45f;
        }
        else if (radiation >= 30f)
        {
            speed *= 0.85f;
            stamina *= 0.75f;
            nausea = 60f;
        }
        else if (radiation >= 10f)
        {
            nausea = 90f;
        }

        if (poison >= 70f)
        {
            speed *= 0.7f;
            nausea = nausea > 0f ? MathF.Min(nausea, 30f) : 30f;
            dizzy = MathF.Max(dizzy, 5f);
        }
        else if (poison >= 40f)
        {
            speed *= 0.85f;
            nausea = nausea > 0f ? MathF.Min(nausea, 45f) : 45f;
            dizzy = MathF.Max(dizzy, 4f);
        }
        else if (poison >= 15f)
        {
            nausea = nausea > 0f ? MathF.Min(nausea, 70f) : 70f;
        }

        if (asphyxia >= 80f)
        {
            speed *= 0.6f;
            dizzy = MathF.Max(dizzy, 5f);
        }
        else if (asphyxia >= 50f)
        {
            speed *= 0.75f;
            dizzy = MathF.Max(dizzy, 4f);
        }
        else if (asphyxia >= 20f)
        {
            speed *= 0.9f;
        }

        if (cold >= 80f)
        {
            speed *= 0.6f;
            stamina *= 0.6f;
        }
        else if (cold >= 50f)
        {
            speed *= 0.75f;
            stamina *= 0.8f;
        }
        else if (cold >= 20f)
        {
            speed *= 0.9f;
        }

        if (Active(comp, PainkillerDrug.Tramadol))
            stamina *= TramadolStamina;

        comp.ConditionSpeed = speed;
        comp.ConditionStamina = stamina;

        if (nausea > 0f && _mobState.IsAlive(uid))
        {
            comp.NauseaTimer -= dt;

            if (comp.NauseaTimer <= 0f)
            {
                comp.NauseaTimer = nausea * _random.NextFloat(0.8f, 1.2f);
                _vomit.Vomit(uid, -8f, -8f);
            }
        }
        else
        {
            comp.NauseaTimer = MathF.Max(comp.NauseaTimer, 30f);
        }
    }

    private void TickBreathing(EntityUid uid, PainComponent comp, float dt)
    {
        if (!comp.Arrest && !comp.Depression)
            return;

        var amount = 0f;

        if (comp.Arrest)
        {
            amount = ArrestDamage;
        }
        else
        {
            comp.DepressionTime += dt;

            if (comp.DepressionTime >= DepressionFaint)
                amount = DepressionDamage;

            if (comp.DrugPresence[(int) PainkillerDrug.Fentanyl] <= 0f)
            {
                comp.DepressionClear += dt;

                if (comp.DepressionClear >= DepressionClearTime)
                {
                    comp.Depression = false;
                    comp.DepressionTime = 0f;
                    comp.DepressionClear = 0f;
                    amount = 0f;
                }
            }
            else
            {
                comp.DepressionClear = 0f;
            }
        }

        if (amount <= 0f)
            return;

        var damage = new DamageSpecifier();
        damage.DamageDict["Asphyxiation"] = FixedPoint2.New(amount * dt);
        _damageable.TryChangeDamage(uid, damage, ignoreResistances: true, interruptsDoAfters: false);
    }

    private void UpdateForcedSleep(EntityUid uid, PainComponent comp)
    {
        var want = _mobState.IsAlive(uid) &&
                   (comp.Shock || comp.Arrest || (comp.Depression && comp.DepressionTime >= DepressionFaint));

        if (want == comp.ForcedSleep)
            return;

        comp.ForcedSleep = want;

        if (want)
        {
            _status.TrySetStatusEffectDuration(uid, SleepingSystem.StatusEffectForcedSleeping);
            RemComp<SpamEmitSoundComponent>(uid);
        }
        else
        {
            _status.TryRemoveStatusEffect(uid, SleepingSystem.StatusEffectForcedSleeping);
            _sleeping.TryWaking(uid, force: true);
        }
    }

    private float LevelSpeed(PainLevel level)
    {
        return level switch
        {
            PainLevel.Weak => 0.95f,
            PainLevel.Medium => 0.8f,
            PainLevel.Strong => 0.4f,
            PainLevel.Extreme => 0.4f,
            _ => 1f
        };
    }

    private void UpdateSpeed(EntityUid uid, PainComponent comp)
    {
        var speed = LevelSpeed(comp.Level) * comp.ConditionSpeed;

        if (comp.Adrenaline)
            speed *= AdrenalineSpeed;

        var muffle = 0f;

        if (Active(comp, PainkillerDrug.Fentanyl))
        {
            speed *= FentanylSpeed;
            muffle = 0.7f;
        }

        var changed = false;

        if (MathF.Abs(speed - comp.SpeedModifier) > 0.005f)
        {
            comp.SpeedModifier = speed;
            changed = true;
        }

        if (MathF.Abs(muffle - comp.Muffle) > 0.005f)
        {
            comp.Muffle = muffle;
            changed = true;
        }

        if (!changed)
            return;

        Dirty(uid, comp);
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void UpdateStamina(EntityUid uid, PainComponent comp)
    {
        if (!TryComp<StaminaComponent>(uid, out var stamina))
            return;

        if (comp.ConditionStamina < 0.99f)
        {
            if (!comp.StaminaApplied)
            {
                comp.BaseStaminaDecay = stamina.Decay;
                comp.StaminaApplied = true;
            }

            var target = comp.BaseStaminaDecay * comp.ConditionStamina;

            if (MathF.Abs(stamina.Decay - target) > 0.001f)
            {
                stamina.Decay = target;
                Dirty(uid, stamina);
            }
        }
        else if (comp.StaminaApplied)
        {
            comp.StaminaApplied = false;
            stamina.Decay = comp.BaseStaminaDecay;
            Dirty(uid, stamina);
        }
    }

    public override void OnPainkiller(EntityUid uid, PainComponent comp, PainkillerDrug drug)
    {
        var i = (int) drug;

        if (comp.DrugPresence[i] <= 0f)
            comp.DrugOnset[i] = _random.NextFloat(OnsetMin[i], OnsetMax[i]);

        comp.DrugPresence[i] = DrugPresenceTime;

        if (drug == PainkillerDrug.Fentanyl &&
            Active(comp, drug) &&
            !comp.Depression &&
            !comp.Arrest &&
            comp.RawPain < EnterMedium)
        {
            comp.Depression = true;
            comp.DepressionTime = 0f;
            comp.DepressionClear = 0f;
        }
    }

    public override void OnOverdose(EntityUid uid, PainComponent comp, PainOverdoseKind kind)
    {
        if (!_mobState.IsAlive(uid))
            return;

        switch (kind)
        {
            case PainOverdoseKind.TramadolSeizure:
                if (Active(comp, PainkillerDrug.Tramadol))
                    _stun.TryKnockdown(uid, TimeSpan.FromSeconds(2), drop: true);

                break;

            case PainOverdoseKind.FentanylDepression:
                if (Active(comp, PainkillerDrug.Fentanyl) && !comp.Depression && !comp.Arrest)
                {
                    comp.Depression = true;
                    comp.DepressionTime = 0f;
                    comp.DepressionClear = 0f;
                }

                break;

            case PainOverdoseKind.FentanylPressure:
                if (Active(comp, PainkillerDrug.Fentanyl))
                    CheckPressure(uid, comp);

                break;
        }
    }

    private void CheckPressure(EntityUid uid, PainComponent comp)
    {
        var blood = _bloodstream.GetBloodLevel(uid);

        if (blood <= 0f || blood >= HypotensionBlood)
            return;

        _status.TrySetStatusEffectDuration(uid, Dizzy, TimeSpan.FromSeconds(8));

        if (blood >= ArrestBlood || comp.Arrest)
            return;

        var chance = MathF.Min(ArrestChanceMax, (ArrestBlood - blood) * ArrestChanceScale);

        if (_random.Prob(chance))
            comp.Arrest = true;
    }

    public override void OnCure(EntityUid uid, PainComponent comp, PainCureKind kind)
    {
        switch (kind)
        {
            case PainCureKind.RespiratoryAntidote:
                comp.Depression = false;
                comp.DepressionTime = 0f;
                comp.DepressionClear = 0f;
                break;

            case PainCureKind.HeartRestart:
                comp.Arrest = false;
                comp.Depression = false;
                comp.DepressionTime = 0f;
                comp.DepressionClear = 0f;
                break;
        }

        UpdateForcedSleep(uid, comp);
    }

    private void OnMoveInput(Entity<PainComponent> ent, ref MoveInputEvent args)
    {
        var comp = ent.Comp;

        if (comp.TramadolTurnCooldown > 0f || !Active(comp, PainkillerDrug.Tramadol))
            return;

        var old = Direction(args.OldMovement);
        var current = Direction(args.Entity.Comp.HeldMoveButtons);

        if (old == Vector2.Zero || current == Vector2.Zero)
            return;

        if (Vector2.Dot(Vector2.Normalize(old), Vector2.Normalize(current)) > 0.1f)
            return;

        comp.TramadolTurnCooldown = 5f;
        _status.TrySetStatusEffectDuration(ent.Owner, Dizzy, TimeSpan.FromSeconds(2.5));
    }

    private static Vector2 Direction(MoveButtons buttons)
    {
        var x = 0f;
        var y = 0f;

        if ((buttons & MoveButtons.Right) != MoveButtons.None)
            x += 1f;

        if ((buttons & MoveButtons.Left) != MoveButtons.None)
            x -= 1f;

        if ((buttons & MoveButtons.Up) != MoveButtons.None)
            y += 1f;

        if ((buttons & MoveButtons.Down) != MoveButtons.None)
            y -= 1f;

        return new Vector2(x, y);
    }

    private void OnMobStateChanged(Entity<PainComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            ResetState(ent.Owner, ent.Comp);
            return;
        }

        if (args.NewMobState != MobState.Alive)
        {
            ent.Comp.Shock = false;
            UpdateForcedSleep(ent.Owner, ent.Comp);
            Dirty(ent);
        }
    }

    private void OnRejuvenate(Entity<PainComponent> ent, ref RejuvenateEvent args)
    {
        ResetState(ent.Owner, ent.Comp);
    }

    private void ResetState(EntityUid uid, PainComponent comp)
    {
        comp.Channels.Clear();
        comp.Adrenaline = false;
        comp.AdrenalineTimer = 0f;
        comp.AdrenalineCooldown = 0f;
        comp.AdrenalinePoints = 0f;
        comp.AdrenalineHits = 0;
        comp.AdrenalineWindow = 0f;
        comp.BurnMomentum = 1f;
        comp.BurnIdle = 99f;
        comp.FireGrace = 0f;
        comp.Depression = false;
        comp.DepressionTime = 0f;
        comp.DepressionClear = 0f;
        comp.Arrest = false;
        comp.Shock = false;
        comp.Level = PainLevel.None;
        comp.Intensity = 0f;
        comp.RawPain = 0f;
        comp.ConditionSpeed = 1f;
        comp.ConditionStamina = 1f;

        for (var i = 0; i < comp.DrugPresence.Length; i++)
        {
            comp.DrugPresence[i] = 0f;
            comp.DrugOnset[i] = 0f;
        }

        UpdateForcedSleep(uid, comp);
        UpdateSpeed(uid, comp);
        UpdateStamina(uid, comp);
        Dirty(uid, comp);
    }
}
