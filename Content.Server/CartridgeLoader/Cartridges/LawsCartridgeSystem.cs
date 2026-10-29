// Nestation new city CC BY-NC 3.0
using Content.Shared.Access.Components;
using Content.Shared.Administration.Logs;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.PDA;

namespace Content.Server.CartridgeLoader.Cartridges;

public sealed class LawsCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;

    private readonly List<string> _laws = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LawsCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<LawsCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _laws.Clear());
    }

    private void OnUiReady(Entity<LawsCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        UpdateUi(ent, args.Loader);
    }

    private void OnUiMessage(EntityUid uid, LawsCartridgeComponent comp, CartridgeMessageEvent args)
    {
        if (args is not LawsUiMessageEvent msg)
            return;

        var loaderUid = GetEntity(args.LoaderUid);

        if (!CanWrite(comp, loaderUid))
        {
            UpdateUi((uid, comp), loaderUid);
            return;
        }

        var text = msg.Text.Trim();
        if (text.Length > LawsCartridgeLimits.MaxLawLength)
            text = text[..LawsCartridgeLimits.MaxLawLength];

        var changed = false;

        switch (msg.Action)
        {
            case LawsUiAction.Add when text.Length > 0 && _laws.Count < LawsCartridgeLimits.MaxLaws:
                _laws.Add(text);
                changed = true;
                break;
            case LawsUiAction.Edit when text.Length > 0 && msg.Index >= 0 && msg.Index < _laws.Count:
                _laws[msg.Index] = text;
                changed = true;
                break;
            case LawsUiAction.Remove when msg.Index >= 0 && msg.Index < _laws.Count:
                _laws.RemoveAt(msg.Index);
                changed = true;
                break;
        }

        if (!changed)
        {
            UpdateUi((uid, comp), loaderUid);
            return;
        }

        _adminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"Законы изменены через {ToPrettyString(loaderUid):pda}: {msg.Action} №{msg.Index + 1} \"{text}\"");

        UpdateAllUis();
    }

    private bool CanWrite(LawsCartridgeComponent comp, EntityUid loaderUid)
    {
        if (!TryComp<PdaComponent>(loaderUid, out var pda) || pda.ContainedId is not { } idUid)
            return false;

        return TryComp<AccessComponent>(idUid, out var access) && access.Tags.Contains(comp.WriteAccess);
    }

    private void UpdateUi(Entity<LawsCartridgeComponent> ent, EntityUid loaderUid)
    {
        var state = new LawsUiState(new List<string>(_laws), CanWrite(ent.Comp, loaderUid));
        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
    }

    private void UpdateAllUis()
    {
        var query = AllEntityQuery<LawsCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var uid, out var comp, out var cartridge))
        {
            if (cartridge.LoaderUid is not { } loaderUid)
                continue;

            if (!TryComp<CartridgeLoaderComponent>(loaderUid, out var loader) || loader.ActiveProgram != uid)
                continue;

            UpdateUi((uid, comp), loaderUid);
        }
    }
}
