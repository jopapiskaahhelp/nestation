// Nestation New City CC BY-NC 3.0

using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Nestation.LinkedAccounts;

public sealed class LinkedAccountsRequestMessage : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
    }
}

public sealed class LinkedAccountsMessage : NetMessage
{
    public const int MaxAccounts = 16;

    public override MsgGroups MsgGroup => MsgGroups.Command;

    public List<Guid> Accounts = new();

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        var count = Math.Min(buffer.ReadInt32(), MaxAccounts);
        Accounts = new List<Guid>(Math.Max(count, 0));

        for (var i = 0; i < count; i++)
        {
            if (Guid.TryParse(buffer.ReadString(), out var id))
                Accounts.Add(id);
        }
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Accounts.Count);

        foreach (var id in Accounts)
        {
            buffer.Write(id.ToString());
        }
    }
}
