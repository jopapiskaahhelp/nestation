// Nestation New City CC BY-NC 3.0
using Content.Shared.Bed.Sleep;
using Content.Shared.Body;
using Content.Shared.Chat;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Systems;
using Content.Shared.Speech.Components;
using Content.Shared.Speech.Muting;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.LimbLossScream;

public sealed partial class LimbLossScreamSystem : EntitySystem
{
    private static readonly ProtoId<OrganCategoryPrototype>[] LimbCategories =
    {
        "ArmLeft",
        "ArmRight",
        "HandLeft",
        "HandRight",
        "LegLeft",
        "LegRight",
        "FootLeft",
        "FootRight",
    };

    private static readonly ProtoId<OrganCategoryPrototype> HeadCategory = "Head";
    private static readonly ProtoId<OrganCategoryPrototype> TorsoCategory = "Torso";

    private static readonly ProtoId<EmotePrototype> ScreamEmote = "NestationLimbLossScream";

    private static readonly string MaleScreams = "NestationLimbLossScreamsMale";
    private static readonly string FemaleScreams = "NestationLimbLossScreamsFemale";

    private static readonly TimeSpan ScreamDelay = TimeSpan.FromSeconds(1);

    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private SharedChatSystem _chat = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyComponent, OrganRemovedFromEvent>(OnOrganRemoved);
    }

    private void OnOrganRemoved(Entity<BodyComponent> ent, ref OrganRemovedFromEvent args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (!TryComp<OrganComponent>(args.Organ, out var organ) || organ.Category is not { } category)
            return;

        if (category == HeadCategory || category == TorsoCategory)
        {
            RemComp<LimbLossScreamPendingComponent>(ent);
            return;
        }

        if (!IsLimb(category))
            return;

        if (HasComp<LimbLossScreamPendingComponent>(ent))
            return;

        if (!HasComp<HumanoidProfileComponent>(ent) || !HasComp<VocalComponent>(ent))
            return;

        if (_mobState.IsDead(ent))
            return;

        if (!HasHeadAndTorso(ent))
            return;

        var pending = EnsureComp<LimbLossScreamPendingComponent>(ent);
        pending.ScreamAt = _timing.CurTime + ScreamDelay;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<LimbLossScreamPendingComponent>();

        while (query.MoveNext(out var uid, out var pending))
        {
            if (curTime < pending.ScreamAt)
                continue;

            RemComp<LimbLossScreamPendingComponent>(uid);

            if (!CanScream(uid))
                continue;

            Scream(uid);
        }
    }

    private bool CanScream(EntityUid uid)
    {
        if (!HasComp<VocalComponent>(uid) || !HasComp<HumanoidProfileComponent>(uid))
            return false;

        if (_mobState.IsDead(uid))
            return false;

        if (HasComp<SleepingComponent>(uid))
            return false;

        if (_statusEffects.HasEffectComp<MutedStatusEffectComponent>(uid))
            return false;

        return HasHeadAndTorso(uid);
    }

    private void Scream(EntityUid uid)
    {
        if (!_chat.TryEmoteWithChat(uid, ScreamEmote, ignoreActionBlocker: true, forceEmote: true))
            return;

        var sex = TryComp<HumanoidProfileComponent>(uid, out var humanoid) ? humanoid.Sex : Sex.Unsexed;

        var female = sex switch
        {
            Sex.Female => true,
            Sex.Male => false,
            _ => _random.Prob(0.5f),
        };

        var sound = new SoundCollectionSpecifier(
            female ? FemaleScreams : MaleScreams,
            AudioParams.Default.WithVolume(6f).WithVariation(0.1f));

        _audio.PlayPvs(sound, uid);
    }

    private bool HasHeadAndTorso(EntityUid body)
    {
        var hasHead = false;
        var hasTorso = false;

        foreach (var organ in _body.EnumerateOrgans<OrganComponent>(body))
        {
            var category = organ.Comp1.Category;

            if (category == HeadCategory)
                hasHead = true;
            else if (category == TorsoCategory)
                hasTorso = true;
        }

        return hasHead && hasTorso;
    }

    private static bool IsLimb(ProtoId<OrganCategoryPrototype> category)
    {
        foreach (var limb in LimbCategories)
        {
            if (limb == category)
                return true;
        }

        return false;
    }
}
