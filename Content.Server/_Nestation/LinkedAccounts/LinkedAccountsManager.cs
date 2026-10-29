// Nestation New City CC BY-NC 3.0

using System.Linq;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Shared._Nestation.LinkedAccounts;
using Robust.Shared.Configuration;
using Robust.Shared.Network;

namespace Content.Server._Nestation.LinkedAccounts;

public sealed partial class LinkedAccountsManager
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ILocalizationManager _loc = default!;
    [Dependency] private IChatManager _chat = default!;

    public void Initialize()
    {
        _net.RegisterNetMessage<LinkedAccountsRequestMessage>();
        _net.RegisterNetMessage<LinkedAccountsMessage>(OnLinkedAccounts);
        _net.Connected += OnConnected;
    }

    private void OnConnected(object? sender, NetChannelArgs e)
    {
        _net.ServerSendMessage(new LinkedAccountsRequestMessage(), e.Channel);
    }

    private async void OnLinkedAccounts(LinkedAccountsMessage message)
    {
        var channel = message.MsgChannel;
        var self = channel.UserId;
        var accounts = message.Accounts.Distinct().Where(id => id != self.UserId).ToList();

        foreach (var account in accounts)
        {
            var bans = await _db.GetBansAsync(null, new NetUserId(account), null, null, includeUnbanned: false);

            if (bans.Count == 0)
                continue;

            var alert = _loc.GetString(
                "linked-accounts-admin-alert",
                ("player", (object) channel.UserName),
                ("account", (object) account.ToString()),
                ("ban", (object) (bans[0].Id?.ToString() ?? "?")));

            _chat.SendAdminAlert(alert);

            channel.Disconnect(bans[0].FormatBanMessage(_cfg, _loc));
            return;
        }

        accounts.Add(self.UserId);

        _net.ServerSendMessage(
            new LinkedAccountsMessage { Accounts = accounts.TakeLast(LinkedAccountsMessage.MaxAccounts).ToList() },
            channel);
    }
}
