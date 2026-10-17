// Nestation Anarhey CC BY-NC 3.0
using Content.Shared.Zones;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Zones;

public sealed partial class ZoneAreaSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedTransformSystem _xform = default!;

    private const float UpdateInterval = 0.5f;

    private float _accumulator;

    private readonly Dictionary<NetUserId, string?> _current = new();

    public override void Initialize()
    {
        base.Initialize();

        _player.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _player.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus == SessionStatus.Disconnected)
            _current.Remove(e.Session.UserId);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { } uid)
            {
                _current.Remove(session.UserId);
                continue;
            }

            var zone = FindZone(uid);
            _current.TryGetValue(session.UserId, out var previous);

            if (zone == previous)
                continue;

            _current[session.UserId] = zone;

            if (zone != null)
                RaiseNetworkEvent(new ZoneEnteredEvent(zone), session);
        }
    }

    private string? FindZone(EntityUid uid)
    {
        var coords = _xform.GetMapCoordinates(uid);

        var query = EntityQueryEnumerator<ZoneAreaComponent, TransformComponent>();
        while (query.MoveNext(out _, out var zone, out var xform))
        {
            if (xform.MapID != coords.MapId)
                continue;

            var offset = coords.Position - _xform.GetWorldPosition(xform);
            var half = zone.Size / 2f;

            if (MathF.Abs(offset.X) <= half.X && MathF.Abs(offset.Y) <= half.Y)
                return zone.Name;
        }

        return null;
    }
}
