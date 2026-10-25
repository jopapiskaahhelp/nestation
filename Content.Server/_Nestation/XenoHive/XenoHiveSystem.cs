// Nestation Anarhey CC BY-NC 3.0
using System.Numerics;
using Content.Shared._Nestation.XenoHive;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.XenoHive;

public enum XenoTileState : byte
{
    Free,
    Busy,
    Built,
    Blocked,
    Invalid,
}

public enum XenoPlaceResult : byte
{
    Placed,
    Busy,
    Skip,
}

public sealed partial class XenoHiveSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private const CollisionGroup StructureMask =
        CollisionGroup.Impassable | CollisionGroup.MidImpassable | CollisionGroup.HighImpassable | CollisionGroup.LowImpassable;

    private float _accum;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<XenoClutchComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<XenoHiveStructureComponent, DamageModifyEvent>(OnStructureDamage);
    }

    private void OnMapInit(Entity<XenoClutchComponent> ent, ref MapInitEvent args)
    {
        Begin(ent.Owner, ent.Comp);
    }

    private void OnStructureDamage(EntityUid uid, XenoHiveStructureComponent comp, DamageModifyEvent args)
    {
        if (!comp.AnyOrigin && (args.Origin is not { } origin || !HasComp<XenoCasteComponent>(origin)))
            return;

        var spec = new DamageSpecifier();
        foreach (var type in _prototypes.EnumeratePrototypes<DamageTypePrototype>())
        {
            spec.DamageDict[type.ID] = FixedPoint2.New(comp.XenoDamage);
        }

        args.Damage = spec;
    }

    private bool QueenAlive(XenoClutchComponent clutch)
    {
        return clutch.Queen is { } queen && !TerminatingOrDeleted(queen) && !_mobState.IsDead(queen);
    }

    private void Begin(EntityUid uid, XenoClutchComponent clutch)
    {
        if (clutch.Started)
            return;

        clutch.Started = true;
        clutch.Start = _timing.CurTime;
        clutch.LastSpawnAt = clutch.Start;
        clutch.NextRescan = clutch.Start + TimeSpan.FromSeconds(15);
        clutch.Queen = Spawn(clutch.QueenProto, Transform(uid).Coordinates);
    }

    public override void Update(float frameTime)
    {
        _accum += frameTime;
        if (_accum < 1f)
            return;

        _accum = 0f;
        var now = _timing.CurTime;

        var clutches = EntityQueryEnumerator<XenoClutchComponent, TransformComponent>();
        while (clutches.MoveNext(out var uid, out var clutch, out var xform))
        {
            if (Paused(uid))
                continue;

            Begin(uid, clutch);

            if (!QueenAlive(clutch))
            {
                DestroyEggs(uid);
                continue;
            }

            Schedule(uid, clutch, xform, now);

            if (now >= clutch.NextRescan)
            {
                clutch.NextRescan = now + TimeSpan.FromSeconds(15);
                Rescan(clutch);
            }

            if (now >= clutch.NextFix)
            {
                clutch.NextFix = now + TimeSpan.FromSeconds(30);
                FixEnclosed(clutch);
            }
        }

        var eggs = EntityQueryEnumerator<XenoEggComponent, TransformComponent>();
        while (eggs.MoveNext(out var uid, out var egg, out var xform))
        {
            if (Paused(uid) || now < egg.HatchAt)
                continue;

            Hatch(uid, egg, xform);
        }
    }

    private void DestroyEggs(EntityUid clutchUid)
    {
        var eggs = EntityQueryEnumerator<XenoEggComponent>();
        while (eggs.MoveNext(out var uid, out var egg))
        {
            if (egg.Clutch == clutchUid)
                QueueDel(uid);
        }
    }

    private bool TryGetEntry(XenoClutchComponent clutch, out XenoHiveEntry entry)
    {
        entry = default!;

        if (clutch.Cursor < clutch.Opening.Count)
        {
            entry = clutch.Opening[clutch.Cursor];
            return true;
        }

        if (clutch.Cycle.Count == 0)
            return false;

        entry = clutch.Cycle[(clutch.Cursor - clutch.Opening.Count) % clutch.Cycle.Count];
        return true;
    }

    private void Schedule(EntityUid uid, XenoClutchComponent clutch, TransformComponent xform, TimeSpan now)
    {
        for (var guard = 0; guard < 8; guard++)
        {
            if (!TryGetEntry(clutch, out var entry))
                return;

            if (!clutch.HasNext)
            {
                var maturation = entry.Maturation;
                if (entry.MaturationMax > entry.Maturation)
                    maturation += (entry.MaturationMax - entry.Maturation) * _random.NextFloat();

                clutch.NextSpawnAt = clutch.LastSpawnAt + entry.Delay;
                clutch.NextLayAt = clutch.NextSpawnAt - maturation;
                if (clutch.NextLayAt < clutch.Start)
                    clutch.NextLayAt = clutch.Start;

                clutch.HasNext = true;
            }

            if (now < clutch.NextLayAt)
                return;

            LayEgg(uid, clutch, xform, entry);
            clutch.LastSpawnAt = clutch.NextSpawnAt;
            clutch.Cursor++;
            clutch.HasNext = false;
        }
    }

    private void LayEgg(EntityUid uid, XenoClutchComponent clutch, TransformComponent xform, XenoHiveEntry entry)
    {
        EntityCoordinates coords;
        var plan = GetPlan((uid, clutch));
        if (plan != null)
        {
            Vector2i offset;
            do
            {
                offset = new Vector2i(_random.Next(-1, 2), _random.Next(-1, 2));
            }
            while (offset == Vector2i.Zero);

            coords = EntryCoords(plan, offset);
        }
        else
        {
            coords = xform.Coordinates.Offset(_random.NextAngle().ToVec() * 1.2f);
        }

        var egg = Spawn(clutch.EggProto, coords);
        var comp = EnsureComp<XenoEggComponent>(egg);
        comp.Hatch = entry.Proto;
        comp.HatchAt = clutch.NextSpawnAt;
        comp.Class = entry.Class;
        comp.Clutch = uid;
    }

    private void Hatch(EntityUid uid, XenoEggComponent egg, TransformComponent xform)
    {
        Spawn(egg.Hatch, xform.Coordinates);

        if (egg.Clutch is { } clutchUid && TryComp<XenoClutchComponent>(clutchUid, out var clutch))
            clutch.Stage = Math.Max(clutch.Stage, egg.Class);

        QueueDel(uid);
    }

    public bool TrySnapFree(MapId map, Vector2 pos, out EntityCoordinates coords, out Vector2 snapped)
    {
        coords = default;
        snapped = default;

        if (!_maps.TryFindGridAt(map, pos, out var gridUid, out var grid))
            return false;

        var center = _maps.WorldToTile(gridUid, grid, pos);
        var bestDist = float.MaxValue;
        var found = false;

        for (var dx = -3; dx <= 3; dx++)
        {
            for (var dy = -3; dy <= 3; dy++)
            {
                var tile = new Vector2i(center.X + dx, center.Y + dy);
                if (!_maps.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
                    continue;

                if (_turf.IsTileBlocked(tileRef, StructureMask))
                    continue;

                var local = _maps.GridTileToLocal(gridUid, grid, tile);
                var world = _transform.ToMapCoordinates(local).Position;
                var d = (world - pos).LengthSquared();
                if (d >= bestDist)
                    continue;

                bestDist = d;
                coords = local;
                snapped = world;
                found = true;
            }
        }

        return found;
    }

    public bool TryGetHive(MapId map, out Entity<XenoClutchComponent> hive)
    {
        var query = EntityQueryEnumerator<XenoClutchComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var clutch, out var xform))
        {
            if (xform.MapID != map)
                continue;

            hive = (uid, clutch);
            return true;
        }

        hive = default;
        return false;
    }

    public int GetStage(MapId map)
    {
        return TryGetHive(map, out var hive) ? hive.Comp.Stage : 0;
    }

    public Vector2? HivePos(MapId map)
    {
        return TryGetHive(map, out var hive) ? _transform.GetWorldPosition(hive.Owner) : null;
    }

    public XenoMazePlan? GetPlan(Entity<XenoClutchComponent> hive)
    {
        if (hive.Comp.Plan is { } existing)
            return existing;

        var xform = Transform(hive.Owner);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return null;

        var origin = _maps.WorldToTile(grid, gridComp, _transform.GetWorldPosition(xform));

        bool Blocked(Vector2i offset)
        {
            var tile = new Vector2i(origin.X + offset.X, origin.Y + offset.Y);
            if (!_maps.TryGetTileRef(grid, gridComp, tile, out var tileRef) || tileRef.Tile.IsEmpty)
                return true;

            return _turf.IsTileBlocked(tileRef, StructureMask);
        }

        var plan = XenoMazePlan.Generate(_random, Blocked);
        plan.Grid = grid;
        plan.Origin = origin;
        plan.Owner = hive.Comp;
        hive.Comp.Plan = plan;
        return plan;
    }

    private Vector2i Abs(XenoMazePlan plan, Vector2i offset)
    {
        return new Vector2i(plan.Origin.X + offset.X, plan.Origin.Y + offset.Y);
    }

    public EntityCoordinates EntryCoords(XenoMazePlan plan, Vector2i offset)
    {
        var gridComp = Comp<MapGridComponent>(plan.Grid);
        return _maps.GridTileToLocal(plan.Grid, gridComp, Abs(plan, offset));
    }

    public Vector2 EntryWorld(XenoMazePlan plan, Vector2i offset)
    {
        return _transform.ToMapCoordinates(EntryCoords(plan, offset)).Position;
    }

    private bool HasBuilt(XenoMazePlan plan, XenoClutchComponent clutch, MapGridComponent gridComp, Vector2i tile)
    {
        foreach (var anchored in _maps.GetAnchoredEntities(plan.Grid, gridComp, tile))
        {
            var id = MetaData(anchored).EntityPrototype?.ID;
            if (id != null && (id == clutch.WallProto.Id || id == clutch.ReinforcedProto.Id || id == clutch.DoorProto.Id))
                return true;
        }

        return false;
    }

    private XenoTileState Inspect(XenoMazePlan plan, XenoClutchComponent clutch, XenoPlanEntry entry)
    {
        if (!TryComp<MapGridComponent>(plan.Grid, out var gridComp))
            return XenoTileState.Invalid;

        var tile = Abs(plan, entry.Offset);
        if (!_maps.TryGetTileRef(plan.Grid, gridComp, tile, out var tileRef) || tileRef.Tile.IsEmpty)
            return XenoTileState.Invalid;

        if (HasBuilt(plan, clutch, gridComp, tile))
            return XenoTileState.Built;

        var hasMob = false;
        foreach (var ent in _lookup.GetEntitiesInTile(tileRef, LookupFlags.Dynamic))
        {
            if (HasComp<MobStateComponent>(ent))
            {
                hasMob = true;
                break;
            }
        }

        if (_turf.IsTileBlocked(tileRef, StructureMask))
            return hasMob ? XenoTileState.Busy : XenoTileState.Blocked;

        return hasMob ? XenoTileState.Busy : XenoTileState.Free;
    }

    private void Rescan(XenoClutchComponent clutch)
    {
        if (clutch.Plan is not { } plan || !TryComp<MapGridComponent>(plan.Grid, out var gridComp))
            return;

        foreach (var entry in plan.Entries)
        {
            if (entry.State == XenoEntryState.Built && !HasBuilt(plan, clutch, gridComp, Abs(plan, entry.Offset)))
                entry.State = XenoEntryState.Pending;
        }
    }

    private enum TileClass : byte
    {
        Clear,
        Wall,
        Door,
        Foreign,
        Void,
    }

    private TileClass Classify(XenoMazePlan plan, XenoClutchComponent clutch, MapGridComponent gridComp,
        Vector2i tile, out EntityUid wall)
    {
        wall = default;
        if (!_maps.TryGetTileRef(plan.Grid, gridComp, tile, out var tileRef) || tileRef.Tile.IsEmpty)
            return TileClass.Void;

        foreach (var anchored in _maps.GetAnchoredEntities(plan.Grid, gridComp, tile))
        {
            var id = MetaData(anchored).EntityPrototype?.ID;
            if (id == null)
                continue;

            if (id == clutch.WallProto.Id || id == clutch.ReinforcedProto.Id)
            {
                wall = anchored;
                return TileClass.Wall;
            }

            if (id == clutch.DoorProto.Id)
                return TileClass.Door;
        }

        return _turf.IsTileBlocked(tileRef, StructureMask) ? TileClass.Foreign : TileClass.Clear;
    }

    private bool BuildLabels(XenoMazePlan plan, XenoClutchComponent clutch, MapGridComponent gridComp,
        out TileClass[,] cls, out EntityUid[,] walls, out int[,] label, out HashSet<int> good)
    {
        var r = plan.Radius;
        var size = 2 * r + 1;
        cls = new TileClass[size, size];
        walls = new EntityUid[size, size];
        label = new int[size, size];
        good = new HashSet<int>();

        for (var x = 0; x < size; x++)
        {
            for (var y = 0; y < size; y++)
            {
                cls[x, y] = Classify(plan, clutch, gridComp, Abs(plan, new Vector2i(x - r, y - r)), out walls[x, y]);
                label[x, y] = cls[x, y] is TileClass.Clear or TileClass.Door ? 0 : -1;
            }
        }

        var count = 0;
        var stack = new Stack<(int X, int Y)>();
        var dx = new[] { 1, -1, 0, 0 };
        var dy = new[] { 0, 0, 1, -1 };

        for (var sx = 0; sx < size; sx++)
        {
            for (var sy = 0; sy < size; sy++)
            {
                if (label[sx, sy] != 0)
                    continue;

                count++;
                label[sx, sy] = count;
                stack.Push((sx, sy));
                while (stack.Count > 0)
                {
                    var (cx, cy) = stack.Pop();
                    for (var i = 0; i < 4; i++)
                    {
                        var nx = cx + dx[i];
                        var ny = cy + dy[i];
                        if (nx < 0 || ny < 0 || nx >= size || ny >= size || label[nx, ny] != 0)
                            continue;

                        label[nx, ny] = count;
                        stack.Push((nx, ny));
                    }
                }
            }
        }

        for (var i = 0; i < size; i++)
        {
            foreach (var l in new[] { label[0, i], label[size - 1, i], label[i, 0], label[i, size - 1] })
            {
                if (l > 0)
                    good.Add(l);
            }
        }

        if (good.Count == 0 && label[r, r] > 0)
            good.Add(label[r, r]);

        return good.Count > 0;
    }

    private void EnsureLabels(XenoMazePlan plan)
    {
        var now = _timing.CurTime;
        if (plan.Labels != null && (now - plan.LabelsAt).TotalSeconds < 1.5)
            return;

        if (plan.Owner == null || !TryComp<MapGridComponent>(plan.Grid, out var gridComp))
            return;

        BuildLabels(plan, plan.Owner, gridComp, out _, out _, out var label, out var good);
        plan.Labels = label;
        plan.LabelsAt = now;
        plan.Good.Clear();
        foreach (var g in good)
        {
            plan.Good.Add(g);
        }
    }

    private int LabelAtOffset(XenoMazePlan plan, Vector2i offset)
    {
        var r = plan.Radius;
        var lx = offset.X + r;
        var ly = offset.Y + r;
        if (plan.Labels is not { } labels || lx < 0 || ly < 0 || lx >= labels.GetLength(0) || ly >= labels.GetLength(1))
            return -2;

        return labels[lx, ly];
    }

    public bool CanReach(XenoMazePlan plan, Vector2 from, Vector2i target)
    {
        EnsureLabels(plan);
        if (plan.Labels == null || !TryComp<MapGridComponent>(plan.Grid, out var gridComp))
            return true;

        var targetLabel = LabelAtOffset(plan, target);
        if (targetLabel <= 0)
            return false;

        var tile = _maps.WorldToTile(plan.Grid, gridComp, from);
        var fromLabel = LabelAtOffset(plan, new Vector2i(tile.X - plan.Origin.X, tile.Y - plan.Origin.Y));

        if (fromLabel == -2)
            return plan.Good.Contains(targetLabel);

        if (fromLabel <= 0)
            return true;

        return fromLabel == targetLabel || (plan.Good.Contains(fromLabel) && plan.Good.Contains(targetLabel));
    }

    private void FixEnclosed(XenoClutchComponent clutch)
    {
        if (clutch.Plan is not { } plan || !TryComp<MapGridComponent>(plan.Grid, out var gridComp))
            return;

        if (!BuildLabels(plan, clutch, gridComp, out var cls, out var walls, out var label, out var good))
            return;

        var r = plan.Radius;
        var size = 2 * r + 1;

        var enclosed = new HashSet<int>();
        foreach (var open in plan.Open)
        {
            var l = label[open.X + r, open.Y + r];
            if (l > 0 && !good.Contains(l))
                enclosed.Add(l);
        }

        if (enclosed.Count == 0)
            return;

        var best = new Dictionary<int, (XenoPlanEntry Entry, bool ToGood, bool Strong)>();
        foreach (var entry in plan.Entries)
        {
            if (entry.State != XenoEntryState.Built || entry.Kind == XenoPlanKind.Door)
                continue;

            var strong = entry.Kind == XenoPlanKind.Wall;
            var ex = entry.Offset.X + r;
            var ey = entry.Offset.Y + r;
            if (ex < 1 || ey < 1 || ex >= size - 1 || ey >= size - 1 || cls[ex, ey] != TileClass.Wall)
                continue;

            for (var axis = 0; axis < 2; axis++)
            {
                var a = axis == 0 ? label[ex - 1, ey] : label[ex, ey - 1];
                var b = axis == 0 ? label[ex + 1, ey] : label[ex, ey + 1];
                if (a <= 0 || b <= 0 || a == b)
                    continue;

                var inner = enclosed.Contains(a) ? a : b;
                var other = inner == a ? b : a;
                if (!enclosed.Contains(inner))
                    continue;

                var toGood = good.Contains(other);
                if (!best.TryGetValue(inner, out var current)
                    || (!current.Strong && strong)
                    || (current.Strong == strong && !current.ToGood && toGood))
                {
                    best[inner] = (entry, toGood, strong);
                }
            }
        }

        foreach (var (_, choice) in best)
        {
            var entry = choice.Entry;
            var wall = walls[entry.Offset.X + r, entry.Offset.Y + r];
            if (wall != default)
                QueueDel(wall);

            entry.State = XenoEntryState.Removed;
            plan.Claims.Remove(entry.Offset);
            plan.Open.Add(entry.Offset);
            plan.OpenList.Add(entry.Offset);
        }

        plan.Labels = null;
    }

    public XenoPlanEntry? PickEntry(Entity<XenoClutchComponent> hive, XenoMazePlan plan, Vector2 from,
        EntityUid builder, TimeSpan now)
    {
        XenoPlanEntry? best = null;
        var bestDist = float.MaxValue;
        var minPriority = int.MaxValue;

        foreach (var entry in plan.Entries)
        {
            if (entry.State is XenoEntryState.Built or XenoEntryState.Removed)
                continue;

            if (entry.State == XenoEntryState.Skipped)
            {
                if (now < entry.RetryAt)
                    continue;

                entry.State = XenoEntryState.Pending;
            }

            if (minPriority != int.MaxValue && entry.Priority > minPriority + 1)
                break;

            if (plan.Claims.TryGetValue(entry.Offset, out var claim) && claim.Builder != builder && now < claim.Expire)
                continue;

            switch (Inspect(plan, hive.Comp, entry))
            {
                case XenoTileState.Built:
                    entry.State = XenoEntryState.Built;
                    continue;
                case XenoTileState.Invalid:
                case XenoTileState.Blocked:
                    entry.State = XenoEntryState.Skipped;
                    entry.RetryAt = now + TimeSpan.FromSeconds(120);
                    continue;
                case XenoTileState.Busy:
                    continue;
            }

            if (!TryPickStand(plan, entry, from, out _))
                continue;

            minPriority = Math.Min(minPriority, entry.Priority);
            var dist = (EntryWorld(plan, entry.Offset) - from).LengthSquared();
            if (dist < bestDist)
            {
                bestDist = dist;
                best = entry;
            }
        }

        if (best != null)
            plan.Claims[best.Offset] = (builder, now + TimeSpan.FromSeconds(40));

        return best;
    }

    public bool TryPickStand(XenoMazePlan plan, XenoPlanEntry entry, Vector2 from, out Vector2i stand)
    {
        stand = default;
        var bestDist = float.MaxValue;
        var found = false;

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                var cand = new Vector2i(entry.Offset.X + dx, entry.Offset.Y + dy);
                if (!plan.Open.Contains(cand) || !CanReach(plan, from, cand))
                    continue;

                var dist = (EntryWorld(plan, cand) - from).LengthSquared();
                if (dist < bestDist)
                {
                    bestDist = dist;
                    stand = cand;
                    found = true;
                }
            }
        }

        return found;
    }

    public bool TryRandomOpen(XenoMazePlan plan, Vector2 from, out Vector2i tile)
    {
        tile = default;
        var count = 0;
        foreach (var t in plan.OpenList)
        {
            if (!CanReach(plan, from, t))
                continue;

            count++;
            if (_random.Next(count) == 0)
                tile = t;
        }

        return count > 0;
    }

    public bool AnyPending(XenoMazePlan plan)
    {
        foreach (var entry in plan.Entries)
        {
            if (entry.State == XenoEntryState.Pending)
                return true;
        }

        return false;
    }

    public void Release(XenoMazePlan plan, XenoPlanEntry entry)
    {
        plan.Claims.Remove(entry.Offset);
    }

    public void Skip(XenoMazePlan plan, XenoPlanEntry entry, TimeSpan now, float seconds)
    {
        entry.State = XenoEntryState.Skipped;
        entry.RetryAt = now + TimeSpan.FromSeconds(seconds);
        plan.Claims.Remove(entry.Offset);
    }

    private bool DoorClear(XenoMazePlan plan, XenoClutchComponent clutch, XenoPlanEntry entry)
    {
        if (!TryComp<MapGridComponent>(plan.Grid, out var gridComp))
            return false;

        var axis = entry.Rotated ? new Vector2i(1, 0) : new Vector2i(0, 1);
        for (var side = -1; side <= 1; side += 2)
        {
            for (var d = 1; d <= 3; d++)
            {
                var tile = Abs(plan, new Vector2i(entry.Offset.X + axis.X * side * d, entry.Offset.Y + axis.Y * side * d));
                if (!_maps.TryGetTileRef(plan.Grid, gridComp, tile, out var tileRef) || tileRef.Tile.IsEmpty)
                    return false;

                if (HasBuilt(plan, clutch, gridComp, tile))
                    continue;

                if (_turf.IsTileBlocked(tileRef, StructureMask))
                    return false;
            }
        }

        return true;
    }

    public bool TryPickSpot(XenoMazePlan plan, int minRing, int maxRing, out Vector2i tile, Vector2? from = null)
    {
        tile = default;
        var count = 0;
        foreach (var t in plan.OpenList)
        {
            var ring = Math.Max(Math.Abs(t.X), Math.Abs(t.Y));
            if (ring < minRing || ring > maxRing)
                continue;

            if (from is { } origin && !CanReach(plan, origin, t))
                continue;

            count++;
            if (_random.Next(count) == 0)
                tile = t;
        }

        return count > 0;
    }

    public XenoPlaceResult Place(Entity<XenoClutchComponent> hive, XenoMazePlan plan, XenoPlanEntry entry, TimeSpan now)
    {
        switch (Inspect(plan, hive.Comp, entry))
        {
            case XenoTileState.Busy:
                return XenoPlaceResult.Busy;
            case XenoTileState.Built:
                entry.State = XenoEntryState.Built;
                plan.Claims.Remove(entry.Offset);
                return XenoPlaceResult.Placed;
            case XenoTileState.Free:
                break;
            default:
                Skip(plan, entry, now, 120);
                return XenoPlaceResult.Skip;
        }

        if (entry.Kind == XenoPlanKind.Door && !DoorClear(plan, hive.Comp, entry))
        {
            Skip(plan, entry, now, 300);
            return XenoPlaceResult.Skip;
        }

        var proto = entry.Kind switch
        {
            XenoPlanKind.Reinforced => hive.Comp.ReinforcedProto,
            XenoPlanKind.Door => hive.Comp.DoorProto,
            _ => hive.Comp.WallProto,
        };

        var spawned = Spawn(proto, EntryCoords(plan, entry.Offset));
        if (entry.Rotated)
            _transform.SetLocalRotation(spawned, Angle.FromDegrees(90));

        entry.State = XenoEntryState.Built;
        plan.Claims.Remove(entry.Offset);
        return XenoPlaceResult.Placed;
    }
}
