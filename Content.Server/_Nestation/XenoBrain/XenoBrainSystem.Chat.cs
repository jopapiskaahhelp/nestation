// Nestation new city CC BY-NC 3.0
using Content.Shared.Chat;
using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._Nestation.XenoBrain;

public sealed partial class XenoBrainSystem
{
    [Dependency] private SharedHandsSystem _hands = default!;

    private enum ChatIntent
    {
        None,
        Weak,
        Flee,
        Offensive,
    }

    private sealed class Profile
    {
        public float RangedDamage;
        public float MeleeDamage;
        public float BestHit;
        public int Hits;
        public int RangedHits;
        public bool HasGun;
        public TimeSpan GunCheckAt;
    }

    private readonly Dictionary<EntityUid, Profile> _profiles = new();
    private readonly Dictionary<EntityUid, float> _trust = new();
    private readonly Dictionary<EntityUid, TimeSpan> _chatCooldown = new();
    private float _skirmishBias;

    private static readonly string[] Negations =
    {
        "не", "нет", "ни", "никогда", "нельзя", "no", "not", "dont", "don't", "cant", "can't", "never",
    };

    private static readonly string[] WeakPhrases =
    {
        "в крит", "крит", "кританусь", "упаду в крит", "умираю", "умру", "дохну", "подыхаю", "помираю",
        "мне пизда", "пизда мне", "пиздец мне", "мне пиздец", "мне конец", "мне хана", "мне крышка",
        "я труп", "я покойник", "я мертв", "мало хп", "хп мало", "хп на нуле", "почти труп",
        "истекаю", "кровотечение", "кровь течет", "кровью истекаю", "помогите", "помоги", "спасите",
        "на помощь", "нужен медик", "медик", "доктор", "лечите", "я ранен", "ранен", "тяжело ранен",
        "меня жрут", "меня убивают", "меня грызут", "перезаряжаюсь", "нет патронов", "патроны кончились",
        "кончились патроны", "магазин пуст", "без патронов", "безоружен", "у меня нет оружия",
        "я один", "не могу бежать", "не могу двигаться", "мне хуево", "мне плохо",
        "im dying", "i'm dying", "dying", "going down", "going into crit", "help me", "help", "medic",
        "im hurt", "i'm hurt", "im bleeding", "i'm bleeding", "low hp", "need healing", "out of ammo",
        "reloading", "no ammo", "im dead", "i'm dead", "im done", "i'm done",
    };

    private static readonly string[] OffensivePhrases =
    {
        "пошли на них", "пошли за ними", "в атаку", "атакуем", "атака", "вперед", "вперед на них",
        "идем на них", "идем убивать", "убьем их", "убить их", "бей их", "зачистим", "зачистка", "добьем",
        "прессуем", "давим", "наступаем", "штурм", "штурмуем", "за мной", "выходим на охоту", "охотимся",
        "ищем их", "найдем и убьем", "go go go", "attack", "push", "kill them", "charge", "hunt them",
        "clear them", "move in", "lets go kill", "let's go kill",
    };

    private static readonly string[] FleePhrases =
    {
        "отступаем", "отходим", "отступай", "бежим", "беги", "бегите", "валим", "валим отсюда", "уходим",
        "уходи", "уносим ноги", "съебываем", "сука беги", "сваливаем", "спасайся", "run", "retreat",
        "fall back", "get out", "flee", "back off",
    };

    private void InitializeChat()
    {
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
    }

    private void OnSpoke(EntitySpokeEvent ev)
    {
        var speaker = ev.Source;
        if (!HasComp<ActorComponent>(speaker) || HasComp<XenoBrainComponent>(speaker))
            return;

        var now = _timing.CurTime;
        if (_chatCooldown.TryGetValue(speaker, out var until) && now < until)
            return;

        var intent = Classify(ev.Message);
        if (intent == ChatIntent.None)
            return;

        var trust = GetTrust(speaker);
        if (trust < 0.3f || (intent != ChatIntent.Offensive && trust < 0.6f))
            return;

        _chatCooldown[speaker] = now + TimeSpan.FromSeconds(6);

        var xform = Transform(speaker);
        var pos = _transform.GetWorldPosition(xform);
        var map = xform.MapID;

        Log.Debug($"Xeno hive heard {speaker}: '{ev.Message}' -> {intent}");

        foreach (var x in _xenos)
        {
            if (x.Map != map || (x.Pos - pos).Length() > x.Brain.HearingRange || x.Brain.DuelOpponent != null)
                continue;

            if (intent == ChatIntent.Offensive)
                ReactOffensive(x, pos, now);
            else
                ReactPounce(x, speaker, now);
        }
    }

    private void ReactPounce(XenoInfo x, EntityUid speaker, TimeSpan now)
    {
        var b = x.Brain;
        if (b.ForcedTarget == speaker)
            return;

        b.ForcedTarget = speaker;
        b.ForcedUntil = now + TimeSpan.FromSeconds(12);
        b.LuredBy = speaker;
        b.LuredAt = now;
        b.LureHits = 0;
        b.NoRetreatUntil = now + TimeSpan.FromSeconds(10);
        b.NoLurkUntil = now + TimeSpan.FromSeconds(15);
        b.RecentHits.Clear();
        Leap(x, now);
    }

    private void ReactOffensive(XenoInfo x, Vector2 speakerPos, TimeSpan now)
    {
        var b = x.Brain;
        if (b.TargetDist < 8f || (now - b.LastHitTime).TotalSeconds < 5 || b.Mode is XenoMode.Retreat or XenoMode.Ambush)
            return;

        b.ForcedTarget = null;
        b.ThreatPos = speakerPos;
        b.RetreatSpot = null;
        b.RecentHits.Clear();
        b.NoHuntUntil = now + TimeSpan.FromSeconds(40);
        SetMode(x, XenoMode.Retreat, now);
        b.ModeSince = now;
    }

    private ChatIntent Classify(string text)
    {
        var norm = Normalize(text);
        var weak = Score(norm, WeakPhrases);
        var flee = Score(norm, FleePhrases);
        var off = Score(norm, OffensivePhrases);

        if (weak == 0 && flee == 0 && off == 0)
            return ChatIntent.None;

        if (weak >= flee && weak >= off)
            return ChatIntent.Weak;

        if (flee >= off)
            return ChatIntent.Flee;

        return ChatIntent.Offensive;
    }

    private static string Normalize(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var raw in text.ToLowerInvariant())
        {
            var c = raw == 'ё' ? 'е' : raw;
            sb.Append(char.IsLetterOrDigit(c) || c == '\'' ? c : ' ');
        }

        return " " + string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)) + " ";
    }

    private static int Score(string norm, string[] phrases)
    {
        var score = 0;
        foreach (var phrase in phrases)
        {
            var idx = norm.IndexOf(" " + phrase + " ", StringComparison.Ordinal);
            if (idx < 0 || IsNegated(norm, idx))
                continue;

            score += phrase.Contains(' ') ? 2 : 1;
        }

        return score;
    }

    private static bool IsNegated(string norm, int idx)
    {
        var before = norm.Substring(0, idx).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = before.Length - 1; i >= Math.Max(0, before.Length - 2); i--)
        {
            if (Array.IndexOf(Negations, before[i]) >= 0)
                return true;
        }

        return false;
    }

    private float GetTrust(EntityUid speaker) => _trust.TryGetValue(speaker, out var t) ? t : 1f;

    private void PunishLure(XenoBrainComponent b)
    {
        if (b.LuredBy is { } speaker)
        {
            _trust[speaker] = MathF.Max(0f, GetTrust(speaker) - 0.4f);
            Log.Debug($"Xeno hive lost trust in {speaker}: now {_trust[speaker]}");
        }

        b.LuredBy = null;
        b.LureHits = 0;
    }

    private void RewardLure(XenoBrainComponent b)
    {
        if (b.LuredBy is { } speaker)
            _trust[speaker] = MathF.Min(1.5f, GetTrust(speaker) + 0.25f);

        b.LuredBy = null;
        b.LureHits = 0;
    }

    private Profile GetProfile(EntityUid player)
    {
        if (!_profiles.TryGetValue(player, out var p))
        {
            p = new Profile();
            _profiles[player] = p;
        }

        return p;
    }

    private void LearnHit(EntityUid player, float dmg, bool ranged, Vector2 pos, MapId map, TimeSpan now)
    {
        var p = GetProfile(player);
        p.Hits++;
        if (dmg > p.BestHit)
            p.BestHit = dmg;

        if (ranged)
        {
            p.RangedDamage += dmg;
            p.RangedHits++;
            AddDanger(map, pos, now);
        }
        else
        {
            p.MeleeDamage += dmg;
        }
    }

    private void LearnFromDeath(XenoBrainComponent brain, TimeSpan now)
    {
        if (brain.DuelOpponent != null)
            return;

        if (brain.LastSkirmishAt != TimeSpan.Zero && (now - brain.LastSkirmishAt).TotalSeconds < 10)
            _skirmishBias = MathF.Max(-0.3f, _skirmishBias - 0.05f);
        else
            _skirmishBias = MathF.Min(0.4f, _skirmishBias + 0.1f);

        if (brain.LuredBy != null && (now - brain.LuredAt).TotalSeconds < 15)
            PunishLure(brain);
    }

    private bool IsRangedDanger(EntityUid player, TimeSpan now)
    {
        var p = GetProfile(player);
        if (now >= p.GunCheckAt)
        {
            p.GunCheckAt = now + TimeSpan.FromSeconds(2);
            p.HasGun = HoldsGun(player);
        }

        return p.HasGun || p.RangedHits >= 2;
    }

    private bool HoldsGun(EntityUid player)
    {
        foreach (var item in _hands.EnumerateHeld(player))
        {
            if (HasComp<GunComponent>(item))
                return true;
        }

        return false;
    }

    private EntityUid NextStriker(EntityUid current)
    {
        EntityUid? next = null;
        EntityUid? first = null;
        foreach (var a in _pack)
        {
            if (first == null || a.Uid.Id < first.Value.Id)
                first = a.Uid;

            if (a.Uid.Id > current.Id && (next == null || a.Uid.Id < next.Value.Id))
                next = a.Uid;
        }

        return next ?? first ?? current;
    }

    private void PruneLearning()
    {
        _tmpKeys.Clear();
        foreach (var k in _profiles.Keys)
        {
            if (Deleted(k))
                _tmpKeys.Add(k);
        }

        foreach (var k in _tmpKeys)
        {
            _profiles.Remove(k);
        }

        _tmpKeys.Clear();
        foreach (var k in _chatCooldown.Keys)
        {
            if (Deleted(k))
                _tmpKeys.Add(k);
        }

        foreach (var k in _tmpKeys)
        {
            _chatCooldown.Remove(k);
        }
    }
}
