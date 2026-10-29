// Nestation New City CC BY-NC 3.0

using Content.Shared._Nestation.LinkedAccounts;
using JetBrains.Annotations;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;
using Robust.Shared.Network;

namespace Content.Client._Nestation.LinkedAccounts;

[UsedImplicitly]
public sealed partial class LinkedAccountsUIController : UIController
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    public override void Initialize()
    {
        base.Initialize();

        _net.RegisterNetMessage<LinkedAccountsRequestMessage>(OnRequest);
        _net.RegisterNetMessage<LinkedAccountsMessage>(OnSync);
    }

    private void OnRequest(LinkedAccountsRequestMessage message)
    {
        var accounts = new List<Guid>();

        foreach (var part in _cfg.GetCVar(LinkedAccountsCVars.Accounts).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Guid.TryParse(part, out var id))
                accounts.Add(id);
        }

        _net.ClientSendMessage(new LinkedAccountsMessage { Accounts = accounts });
    }

    private void OnSync(LinkedAccountsMessage message)
    {
        _cfg.SetCVar(LinkedAccountsCVars.Accounts, string.Join(',', message.Accounts));
        _cfg.SaveToFile();
    }
}
