// Nestation new city CC BY-NC 3.0
using System.Linq;
using System.Numerics;
using Content.Shared._NeStation.Marine;
using Content.Shared.Access.Systems;
using Content.Shared.Actions;
using Content.Shared.Doors.Components;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Map;

namespace Content.Server._NeStation.Marine;

public sealed partial class MarineMapSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IComponentFactory _factory = default!;

    private static readonly EntProtoId ActionProto = "ActionMarineMap";

    private const int MaxGridSide = 512;

    private static readonly TimeSpan ActionSyncInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BlipInterval = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan LayoutInterval = TimeSpan.FromSeconds(5);

    private Type? _airtightType;

    private TimeSpan _nextActionSync;
    private TimeSpan _nextBlips;
    private TimeSpan _nextLayout;

    private readonly Dictionary<ICommonSession, EntityUid> _subs = new();

    private readonly Dictionary<EntityUid, MarineMapLayout> _layouts = new();
    private readonly Dictionary<EntityUid, List<MarineMapStroke>> _strokes = new();

    public override void Initialize()
    {
        base.Initialize();

        if (_factory.TryGetRegistration("Airtight", out var airtight))
            _airtightType = airtight.Type;

        SubscribeLocalEvent<MarineMapUserComponent, OpenMarineMapEvent>(OnOpenMap);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _subs.Clear();
            _layouts.Clear();
            _strokes.Clear();
        });

        SubscribeNetworkEvent<MarineMapClosedEvent>(OnClosed);
        SubscribeNetworkEvent<MarineMapStrokeEvent>(OnStroke);
        SubscribeNetworkEvent<MarineMapEraseEvent>(OnErase);
    }

    private bool TryGetMarineCard(EntityUid user, out EntityUid card)
    {
        card = default;
        if (!_idCard.TryFindIdCard(user, out var idCard))
            return false;

        if (!HasComp<MarineIdCardComponent>(idCard.Owner))
            return false;

        card = idCard.Owner;
        return true;
    }

    private bool CanDraw(EntityUid user)
    {
        return TryGetMarineCard(user, out var card) && HasComp<MarineMapDrawerComponent>(card);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;

        if (now >= _nextActionSync)
        {
            _nextActionSync = now + ActionSyncInterval;
            SyncActions();
        }

        if (_subs.Count == 0)
            return;

        if (now >= _nextLayout)
        {
            _nextLayout = now + LayoutInterval;
            RefreshLayouts();
        }

        if (now >= _nextBlips)
        {
            _nextBlips = now + BlipInterval;
            SendBlips();
        }
    }

    private void SyncActions()
    {
        var query = EntityQueryEnumerator<ActorComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            var isMarine = TryGetMarineCard(uid, out _);

            if (isMarine)
            {
                var comp = EnsureComp<MarineMapUserComponent>(uid);
                if (comp.ActionEntity == null || !Exists(comp.ActionEntity))
                    _actions.AddAction(uid, ref comp.ActionEntity, ActionProto);
            }
            else if (TryComp<MarineMapUserComponent>(uid, out var comp))
            {
                _actions.RemoveAction(comp.ActionEntity);
                RemComp<MarineMapUserComponent>(uid);

                if (_players.TryGetSessionByEntity(uid, out var session))
                    CloseFor(session);
            }
        }
    }

    private void CloseFor(ICommonSession session)
    {
        if (_subs.Remove(session) && session.Status == SessionStatus.InGame)
            RaiseNetworkEvent(new MarineMapCloseEvent(), session);
    }

    private void OnOpenMap(Entity<MarineMapUserComponent> ent, ref OpenMarineMapEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        if (!_players.TryGetSessionByEntity(user, out var session))
            return;

        args.Handled = true;

        if (_subs.ContainsKey(session))
        {
            CloseFor(session);
            return;
        }

        if (!TryGetMarineCard(user, out _))
            return;

        var gridUid = Transform(user).GridUid;
        if (gridUid == null || !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            _popup.PopupEntity(Loc.GetString("marine-map-no-grid"), user, user);
            return;
        }

        var layout = BuildLayout(gridUid.Value, grid);
        if (layout == null)
        {
            _popup.PopupEntity(Loc.GetString("marine-map-no-grid"), user, user);
            return;
        }

        _layouts[gridUid.Value] = layout;
        _subs[session] = gridUid.Value;

        RaiseNetworkEvent(new MarineMapOpenEvent(layout, GetStrokes(gridUid.Value), CanDraw(user)), session);
    }

    private void OnClosed(MarineMapClosedEvent msg, EntitySessionEventArgs args)
    {
        _subs.Remove(args.SenderSession);
    }

    private List<MarineMapStroke> GetStrokes(EntityUid grid)
    {
        if (!_strokes.TryGetValue(grid, out var list))
            _strokes[grid] = list = new List<MarineMapStroke>();
        return list;
    }

    private void OnStroke(MarineMapStrokeEvent msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!_subs.TryGetValue(session, out var grid))
            return;

        if (session.AttachedEntity is not { } user || !CanDraw(user))
            return;

        var stroke = msg.Stroke;
        if (stroke.Color >= MarineMapPalette.Colors.Length
            || !float.IsFinite(stroke.Size)
            || stroke.Points.Count == 0
            || stroke.Points.Count > MarineMapPalette.MaxPointsPerStroke)
            return;

        foreach (var p in stroke.Points)
        {
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || MathF.Abs(p.X) > 10000 || MathF.Abs(p.Y) > 10000)
                return;
        }

        stroke.Size = Math.Clamp(stroke.Size, MarineMapPalette.MinPenSize, MarineMapPalette.MaxPenSize);

        var list = GetStrokes(grid);
        list.Add(stroke);
        while (list.Count > MarineMapPalette.MaxStrokes)
            list.RemoveAt(0);

        Broadcast(grid, msg, session);
    }

    private void OnErase(MarineMapEraseEvent msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!_subs.TryGetValue(session, out var grid))
            return;

        if (session.AttachedEntity is not { } user || !CanDraw(user))
            return;

        if (!float.IsFinite(msg.Pos.X) || !float.IsFinite(msg.Pos.Y) || !float.IsFinite(msg.Radius))
            return;

        msg.Radius = Math.Clamp(msg.Radius, MarineMapPalette.MinEraserSize * 0.5f, MarineMapPalette.MaxEraserSize * 0.5f);

        MarineMapEraser.Erase(GetStrokes(grid), msg.Pos, msg.Radius);
        Broadcast(grid, msg, session);
    }

    private void Broadcast(EntityUid grid, EntityEventArgs ev, ICommonSession? except)
    {
        foreach (var (session, g) in _subs)
        {
            if (g != grid || session == except || session.Status != SessionStatus.InGame)
                continue;

            RaiseNetworkEvent(ev, session);
        }
    }

    private void RefreshLayouts()
    {
        var grids = new HashSet<EntityUid>(_subs.Values);
        foreach (var gridUid in grids)
        {
            if (!TryComp<MapGridComponent>(gridUid, out var grid))
                continue;

            var fresh = BuildLayout(gridUid, grid);
            if (fresh == null)
                continue;

            if (_layouts.TryGetValue(gridUid, out var old) && old.SameAs(fresh))
                continue;

            _layouts[gridUid] = fresh;
            Broadcast(gridUid, new MarineMapLayoutEvent(fresh), null);
        }
    }

    private void SendBlips()
    {
        foreach (var (session, grid) in _subs.ToArray())
        {
            if (session.Status != SessionStatus.InGame
                || session.AttachedEntity is not { } user
                || !TryGetMarineCard(user, out _)
                || !Exists(grid))
            {
                CloseFor(session);
            }
        }

        if (_subs.Count == 0)
            return;

        var byGrid = new Dictionary<EntityUid, List<MarineBlip>>();
        foreach (var grid in new HashSet<EntityUid>(_subs.Values))
            byGrid[grid] = new List<MarineBlip>();

        var query = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } gridUid || !byGrid.TryGetValue(gridUid, out var list))
                continue;

            if (!_mobState.IsAlive(uid) || !TryGetMarineCard(uid, out _))
                continue;

            var inv = _transform.GetInvWorldMatrix(gridUid);
            var local = Vector2.Transform(_transform.GetWorldPosition(xform), inv);
            list.Add(new MarineBlip(GetNetEntity(uid), local));
        }

        foreach (var (session, grid) in _subs)
        {
            if (session.AttachedEntity is not { } user)
                continue;

            RaiseNetworkEvent(new MarineMapUpdateEvent(byGrid[grid], CanDraw(user)), session);
        }
    }

    private bool IsWall(EntityUid uid)
    {
        if (HasComp<DoorComponent>(uid))
            return false;

        if (HasComp<OccluderComponent>(uid))
            return true;

        return _airtightType != null && HasComp(uid, _airtightType);
    }

    private MarineMapLayout? BuildLayout(EntityUid gridUid, MapGridComponent grid)
    {
        var aabb = grid.LocalAABB;
        var minX = (int) MathF.Floor(aabb.Left);
        var minY = (int) MathF.Floor(aabb.Bottom);
        var maxX = (int) MathF.Ceiling(aabb.Right);
        var maxY = (int) MathF.Ceiling(aabb.Top);
        var w = maxX - minX;
        var h = maxY - minY;

        if (w <= 0 || h <= 0 || w > MaxGridSide || h > MaxGridSide)
            return null;

        var tiles = new byte[w * h];

        foreach (var tileRef in _map.GetAllTiles(gridUid, grid))
        {
            var idx = tileRef.GridIndices;
            var x = idx.X - minX;
            var y = idx.Y - minY;
            if (x < 0 || y < 0 || x >= w || y >= h)
                continue;

            var type = MarineMapTile.Floor;
            var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, idx);
            while (anchored.MoveNext(out var ent))
            {
                if (IsWall(ent.Value))
                {
                    type = MarineMapTile.Wall;
                    break;
                }
            }

            tiles[y * w + x] = type;
        }

        return new MarineMapLayout
        {
            Origin = new Robust.Shared.Maths.Vector2i(minX, minY),
            Width = w,
            Height = h,
            Tiles = tiles,
        };
    }
}
