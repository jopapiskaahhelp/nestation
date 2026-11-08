// Nestation new city CC BY-NC 3.0
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Shared._NeStation.Marine;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._NeStation.Marine;

public sealed partial class MarineAnnounceConsoleSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    private const int AnnounceFontSize = 20;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MarineAnnounceConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<MarineAnnounceConsoleComponent, MarineAnnounceSendMessage>(OnSend);
    }

    private void OnOpened(Entity<MarineAnnounceConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void UpdateUi(Entity<MarineAnnounceConsoleComponent> ent)
    {
        _ui.SetUiState(ent.Owner, MarineAnnounceUiKey.Key,
            new MarineAnnounceConsoleState(ent.Comp.NextAnnounceTime, ent.Comp.MaxLength));
    }

    private void OnSend(Entity<MarineAnnounceConsoleComponent> ent, ref MarineAnnounceSendMessage msg)
    {
        var now = _timing.CurTime;

        if (now < ent.Comp.NextAnnounceTime)
        {
            _popup.PopupEntity(Loc.GetString("marine-announce-cooldown-popup"), ent.Owner, msg.Actor);
            UpdateUi(ent);
            return;
        }

        var text = SharedChatSystem.SanitizeAnnouncement(msg.Message, ent.Comp.MaxLength);
        if (string.IsNullOrWhiteSpace(text))
            return;

        ent.Comp.NextAnnounceTime = now + ent.Comp.Cooldown;

        var header = Loc.GetString("marine-announce-header");
        var wrapped = $"[font size=14][bold]{FormattedMessage.EscapeText(header)}[/bold][/font]\n"
                      + $"[font size={AnnounceFontSize}][bold]{FormattedMessage.EscapeText(text)}[/bold][/font]";

        _chat.ChatMessageToAll(ChatChannel.Radio, text, wrapped, default, false, true, Color.Red);
        _audio.PlayGlobal(SharedChatSystem.DefaultAnnouncementSound, Filter.Broadcast(), true);

        _adminLog.Add(LogType.Chat, LogImpact.Low,
            $"{ToPrettyString(msg.Actor):user} sent a marine announcement: {text}");

        UpdateUi(ent);
    }
}
