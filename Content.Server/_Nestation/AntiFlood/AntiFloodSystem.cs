// Nestation new city CC BY-NC 3.0

using System.Text;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Shared.Abilities.Mime;
using Content.Shared.Database;
using Content.Shared.Popups;
using Content.Shared.Speech;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.AntiFlood;

public sealed partial class AntiFloodSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private static readonly HashSet<char> BaseAllowedChars = new()
    {
        '.', ',', '!', '?', '*', '-', '—', '–', '…', ':', ';',
        '\'', '"', '’', '«', '»', '(', ')', '~',
        '%', '#', '$', '+', '=',
    };

    private enum FloodChannel
    {
        Ic,
        Ooc,
    }

    private sealed class FloodState
    {
        public string Last = string.Empty;
        public int Count;
        public TimeSpan FirstTime;
    }

    private readonly Dictionary<(NetUserId, FloodChannel), FloodState> _states = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AntiFloodMuteComponent, SpeakAttemptEvent>(OnSpeakAttempt);
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    public bool CheckIc(EntityUid source, ICommonSession? session, string message)
    {
        if (session == null || !_cfg.GetCVar(AntiFloodCVars.Enabled))
            return false;

        if (IsExempt(session))
            return false;

        if (HasComp<AntiFloodMuteComponent>(source))
            return false;

        if (TryComp<MimePowersComponent>(source, out var mime) && mime.Enabled)
            return false;

        var text = StripRadioPrefix(message);

        if (ContainsForbiddenChars(text))
        {
            MuteLikeMime(source, message, "antiflood-ic-symbols", "symbols");
            return true;
        }

        if (RegisterRepeat(session, FloodChannel.Ic, text))
        {
            MuteLikeMime(source, message, "antiflood-ic-repeat", "repeat");
            return true;
        }

        return false;
    }

    public bool CheckOoc(ICommonSession session, string message)
    {
        if (!_cfg.GetCVar(AntiFloodCVars.Enabled))
            return false;

        if (IsExempt(session))
            return false;

        if (!RegisterRepeat(session, FloodChannel.Ooc, message))
            return false;

        Kick(session, message);
        return true;
    }

    private void MuteLikeMime(EntityUid uid, string message, string popupLoc, string reason)
    {
        EnsureComp<MimePowersComponent>(uid);

        var until = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(AntiFloodCVars.MimeMuteSeconds));

        var lockComp = EnsureComp<AntiFloodMuteComponent>(uid);
        lockComp.LockUntil = until;

        _popup.PopupEntity(Loc.GetString(popupLoc), uid, uid, PopupType.LargeCaution);

        _adminLog.Add(LogType.Chat, LogImpact.Low,
            $"{ToPrettyString(uid):user} получил обет молчания от антифлуда ({reason}): {message}");
    }

    private void Kick(ICommonSession session, string message)
    {
        _adminLog.Add(LogType.Chat, LogImpact.Medium,
            $"{session.Name} кикнут антифлудом за OOC-флуд: {message}");

        _chat.SendAdminAlert(Loc.GetString("antiflood-admin-alert", ("player", session.Name)));

        _states.Remove((session.UserId, FloodChannel.Ooc));
        _states.Remove((session.UserId, FloodChannel.Ic));

        session.Channel.Disconnect(Loc.GetString("antiflood-kick-reason"));
    }

    private void OnSpeakAttempt(EntityUid uid, AntiFloodMuteComponent comp, SpeakAttemptEvent args)
    {
        var left = comp.LockUntil - _timing.CurTime;
        if (left <= TimeSpan.Zero)
            return;

        args.Cancel();
        _popup.PopupEntity(
            Loc.GetString("antiflood-vow-locked", ("seconds", (int) Math.Ceiling(left.TotalSeconds))),
            uid,
            uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<AntiFloodMuteComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.LockUntil)
                continue;

            RemCompDeferred<AntiFloodMuteComponent>(uid);
            _popup.PopupEntity(Loc.GetString("antiflood-vow-available"), uid, uid);
        }
    }

    private bool IsExempt(ICommonSession session)
    {
        return _cfg.GetCVar(AntiFloodCVars.ExemptAdmins) && _admin.IsAdmin(session);
    }

    private static string StripRadioPrefix(string message)
    {
        var text = message.TrimStart();

        if (text.Length >= 1 && text[0] == ';')
            return text[1..];

        if (text.Length >= 2 && (text[0] == ':' || text[0] == '.') && char.IsLetter(text[1]))
            return text[2..];

        return text;
    }

    private bool ContainsForbiddenChars(string message)
    {
        var extra = _cfg.GetCVar(AntiFloodCVars.AllowedChars);

        foreach (var c in message)
        {
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                continue;

            if (BaseAllowedChars.Contains(c))
                continue;

            if (extra.Contains(c))
                continue;

            return true;
        }

        return false;
    }

    private bool RegisterRepeat(ICommonSession session, FloodChannel channel, string message)
    {
        var normalized = Normalize(message);
        var now = _timing.RealTime;
        var window = TimeSpan.FromSeconds(_cfg.GetCVar(AntiFloodCVars.RepeatWindow));
        var limit = Math.Max(2, _cfg.GetCVar(AntiFloodCVars.RepeatLimit));
        var key = (session.UserId, channel);

        if (_states.TryGetValue(key, out var state)
            && state.Last == normalized
            && now - state.FirstTime <= window)
        {
            state.Count++;
        }
        else
        {
            state = new FloodState { Last = normalized, Count = 1, FirstTime = now };
            _states[key] = state;
        }

        if (state.Count < limit)
            return false;

        _states.Remove(key);
        return true;
    }

    private static string Normalize(string message)
    {
        var sb = new StringBuilder(message.Length);

        foreach (var c in message)
        {
            if (!char.IsLetterOrDigit(c))
                continue;

            var lower = char.ToLowerInvariant(c);
            if (sb.Length > 0 && sb[^1] == lower)
                continue;

            sb.Append(lower);
        }

        return sb.ToString();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus != SessionStatus.Disconnected)
            return;

        _states.Remove((e.Session.UserId, FloodChannel.Ic));
        _states.Remove((e.Session.UserId, FloodChannel.Ooc));
    }
}
