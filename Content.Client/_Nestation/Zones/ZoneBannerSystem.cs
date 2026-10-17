// Nestation Anarhey CC BY-NC 3.0
using Content.Shared.Zones;
using Robust.Client.Graphics;

namespace Content.Client.Zones;

public sealed partial class ZoneBannerSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlay = default!;

    private ZoneBannerOverlay _banner = default!;

    public override void Initialize()
    {
        base.Initialize();

        _banner = new ZoneBannerOverlay();
        _overlay.AddOverlay(_banner);

        SubscribeNetworkEvent<ZoneEnteredEvent>(OnZoneEntered);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlay.RemoveOverlay(_banner);
    }

    private void OnZoneEntered(ZoneEnteredEvent ev)
    {
        _banner.Show(Loc.GetString(ev.Name));
    }
}
