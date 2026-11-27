// Nestation New City CC BY-NC 3.0
using Content.Shared._Nestation.Pain;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._Nestation.Pain;

public sealed partial class PainSystem : SharedPainSystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IPlayerManager _player = default!;

    private PainOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new PainOverlay();

        SubscribeLocalEvent<PainComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PainComponent, LocalPlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<PainComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<PainComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<PainComponent> ent, ref ComponentStartup args)
    {
        if (_player.LocalEntity == ent.Owner)
            AddOverlay();
    }

    private void OnShutdown(Entity<PainComponent> ent, ref ComponentShutdown args)
    {
        if (_player.LocalEntity == ent.Owner)
            _overlayManager.RemoveOverlay(_overlay);
    }

    private void OnPlayerAttached(EntityUid uid, PainComponent component, LocalPlayerAttachedEvent args)
    {
        AddOverlay();
    }

    private void OnPlayerDetached(EntityUid uid, PainComponent component, LocalPlayerDetachedEvent args)
    {
        _overlayManager.RemoveOverlay(_overlay);
    }

    private void AddOverlay()
    {
        if (!_overlayManager.HasOverlay<PainOverlay>())
            _overlayManager.AddOverlay(_overlay);
    }
}
