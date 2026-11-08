// Nestation new city CC BY-NC 3.0
using System.Numerics;
using Content.Shared._Nestation.XenoHive;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
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
using Robust.Shared.Player;
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

public enum XenoRepairKind : byte
{
    Rebuild,
    Mend,
    Upgrade,
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
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;

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
        if (args.Origin is { } attacker && !HasComp<XenoCasteComponent>(attacker))
            RecordPressure(uid);

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
        clutch.NextSchemaAt = clutch.Start + clutch.SchemaFirstDelay;
        SeedInitialBurrowers(clutch);
        clutch.Queen = Spawn(clutch.QueenProto, Transform(uid).Coordinates);
    }

    private void SeedInitialBurrowers(XenoClutchComponent clutch)
    {
        if (clutch.InitialBurrowers > 0)
        {
            var proto = clutch.BurrowerProto;
            if (proto == null && clutch.Opening.Count > 0)
                proto = clutch.Opening[0].Proto;

            if (proto is { } burrower)
            {
                if (clutch.Opening.Count > 0 && clutch.Opening[0].Proto == burrower)
                    clutch.Opening.RemoveAt(0);

                for (var i = 0; i < clutch.InitialBurrowers; i++)
                {
                    clutch.Opening.Insert(i, new XenoHiveEntry
                    {
                        Proto = burrower,
                        Delay = clutch.InitialBurrowerSpacing,
                        Class = 1,
                        Maturation = TimeSpan.FromSeconds(2),
                        NoScale = true,
                    });
                }
            }
        }
    }

    private static TimeSpan ScaledDelay(XenoClutchComponent clutch, XenoHiveEntry entry)
    {
        if (entry.NoScale || clutch.SpawnDelayScale == 1f)
            return entry.Delay;

        return TimeSpan.FromSeconds(entry.Delay.TotalSeconds * clutch.SpawnDelayScale);
    }

    private void RecordPressure(EntityUid structure)
    {
        var xform = Transform(structure);
        if (!TryGetHive(xform.MapID, out var hive)
            || hive.Comp.Plan is not { } plan
            || !TryComp<MapGridComponent>(plan.Grid, out var gridComp))
        {
            return;
        }

        var tile = _maps.WorldToTile(plan.Grid, gridComp, _transform.GetWorldPosition(xform));
        var rel = new Vector2(tile.X - plan.Origin.X, tile.Y - plan.Origin.Y);
        if (rel.LengthSquared() < 1f)
            return;

        var sector = XenoSchemaPlanner.SectorOf(rel);
        plan.Pressure[sector] = MathF.Min(plan.Pressure[sector] + 0.05f, 6f);
    }

    private XenoSituation Analyze(XenoClutchComponent clutch, XenoMazePlan plan, MapGridComponent gridComp, MapId map)
    {
        var situation = new XenoSituation();

        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var mob, out var xform))
        {
            if (xform.MapID != map || HasComp<XenoCasteComponent>(uid) || !_mobState.IsAlive(uid, mob))
                continue;

            var tile = _maps.WorldToTile(plan.Grid, gridComp, _transform.GetWorldPosition(xform));
            var rel = new Vector2(tile.X - plan.Origin.X, tile.Y - plan.Origin.Y);
            var dist = rel.Length();
            if (dist < 0.5f)
                continue;

            var sector = XenoSchemaPlanner.SectorOf(rel);
            situation.Threat[sector] += 1f / (1f + dist / 25f);
            situation.Nearest[sector] = MathF.Min(situation.Nearest[sector], dist);
            situation.Players++;

            if (dist <= plan.Radius)
                situation.Inside++;
        }

        foreach (var entry in plan.Entries)
        {
            if (!entry.Wound || entry.State == XenoEntryState.Built)
                continue;

            situation.Threat[XenoSchemaPlanner.SectorOf(new Vector2(entry.Offset.X, entry.Offset.Y))] += 0.25f;
            situation.Wounds++;
        }

        for (var i = 0; i < 8; i++)
        {
            situation.Threat[i] += plan.Pressure[i];
            plan.Pressure[i] *= 0.5f;
        }

        if (clutch.SchemaFrontSector is >= 0 and < 8)
            situation.Threat[clutch.SchemaFrontSector] += clutch.SchemaFrontThreat;

        return situation;
    }

    private void PlanSchema(Entity<XenoClutchComponent> hive)
    {
        var plan = GetPlan(hive);
        if (plan == null || !TryComp<MapGridComponent>(plan.Grid, out var gridComp))
            return;

        var situation = Analyze(hive.Comp, plan, gridComp, Transform(hive.Owner).MapID);
        plan.Situation = situation;

        var tiles = XenoSchemaPlanner.Build(plan, situation, hive.Comp.SchemaMaxSectors, hive.Comp.SchemaMaxEntries);
        if (tiles.Count == 0)
            return;

        var added = plan.ApplySchema(_random, tiles);
        Log.Debug($"Xeno queen schema #{plan.SchemaEpoch}: players={situation.Players} inside={situation.Inside} wounds={situation.Wounds} tiles={added}");
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

            if (clutch.SchemaEnabled && now >= clutch.NextSchemaAt)
            {
                clutch.NextSchemaAt = now + clutch.SchemaInterval;
                PlanSchema((uid, clutch));
            }

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

            if (now >= clutch.NextAudit)
            {
                clutch.NextAudit = now + TimeSpan.FromSeconds(20);
                Audit(clutch, now);
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

                clutch.NextSpawnAt = clutch.LastSpawnAt + ScaledDelay(clutch, entry);
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
        var mob = Spawn(egg.Hatch, xform.Coordinates);
        EnsureComp<XenoHiveMemberComponent>(mob);

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

    private bool IsOffsetBlocked(XenoMazePlan plan, MapGridComponent gridComp, Vector2i offset)
    {
        var tile = Abs(plan, offset);
        if (!_maps.TryGetTileRef(plan.Grid, gridComp, tile, out var tileRef) || tileRef.Tile.IsEmpty)
            return true;

        if (plan.Owner is { NoBuildTiles.Count: > 0 } owner && owner.NoBuildTiles.Contains(_tileDefs[tileRef.Tile.TypeId].ID))
            return true;

        return _turf.IsTileBlocked(tileRef, StructureMask);
    }

    public TimeSpan BuildDelay(XenoClutchComponent clutch, TimeSpan interval, TimeSpan now)
    {
        if (clutch.HeadStart <= TimeSpan.Zero || now - clutch.Start >= clutch.HeadStart)
            return interval;

        return TimeSpan.FromSeconds(interval.TotalSeconds * clutch.HeadStartBuildScale);
    }

    public bool TryExpand(Entity<XenoClutchComponent> hive, XenoMazePlan plan)
    {
        if (plan.Radius >= XenoMazePlan.AlignRadius(hive.Comp.MaxRadius))
            return false;

        if (!TryComp<MapGridComponent>(plan.Grid, out var gridComp))
            return false;

        if (!plan.Expand(_random, offset => IsOffsetBlocked(plan, gridComp, offset), hive.Comp.MaxRadius))
            return false;

        plan.Good.Clear();
        return true;
    }

    public bool CanExpand(Entity<XenoClutchComponent> hive, XenoMazePlan plan)
    {
        return plan.Radius < XenoMazePlan.AlignRadius(hive.Comp.MaxRadius);
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
            if (entry.State != XenoEntryState.Built)
                continue;

            if (HasBuilt(plan, clutch, gridComp, Abs(plan, entry.Offset)))
            {
                SetClass(plan, entry.Offset, entry.Kind == XenoPlanKind.Door ? TileClass.Door : TileClass.Wall);
            }
            else
            {
                entry.State = XenoEntryState.Pending;
                entry.Wound = true;
                SetClass(plan, entry.Offset, TileClass.Clear);
            }
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

    private static readonly int[] Dx = { 1, -1, 0, 0 };
    private static readonly int[] Dy = { 0, 0, 1, -1 };

    private static bool Passable(byte c)
    {
        return c == (byte) TileClass.Clear || c == (byte) TileClass.Door;
    }

    private void EnsureCls(XenoMazePlan plan, XenoClutchComponent clutch, MapGridComponent gridComp)
    {
        var r = plan.Radius;
        var size = 2 * r + 1;
        var now = _timing.CurTime;
        var cache = plan.ClsCache;
        if (cache != null && cache.GetLength(0) == size && now < plan.ClsFullAt)
            return;

        cache = new byte[size, size];
        for (var x = 0; x < size; x++)
        {
            for (var y = 0; y < size; y++)
            {
                cache[x, y] = (byte) Classify(plan, clutch, gridComp, Abs(plan, new Vector2i(x - r, y - r)), out _);
            }
        }

        plan.ClsCache = cache;
        plan.ClsFullAt = now + TimeSpan.FromSeconds(120);
        plan.ClsVersion++;
        plan.Labels = null;
    }

    private void SetClass(XenoMazePlan plan, Vector2i offset, TileClass cls)
    {
        var cache = plan.ClsCache;
        var r = plan.Radius;
        var size = 2 * r + 1;
        var x = offset.X + r;
        var y = offset.Y + r;
        if (cache == null || cache.GetLength(0) != size || x < 0 || y < 0 || x >= size || y >= size)
            return;

        if (cache[x, y] == (byte) cls)
            return;

        cache[x, y] = (byte) cls;
        plan.ClsVersion++;
    }

    private bool BuildLabels(XenoMazePlan plan, XenoClutchComponent clutch, MapGridComponent gridComp,
        out TileClass[,] cls, out int[,] label, out HashSet<int> good)
    {
        EnsureCls(plan, clutch, gridComp);

        var r = plan.Radius;
        var size = 2 * r + 1;
        var cache = plan.ClsCache!;
        cls = new TileClass[size, size];
        label = new int[size, size];
        good = new HashSet<int>();

        for (var x = 0; x < size; x++)
        {
            for (var y = 0; y < size; y++)
            {
                cls[x, y] = (TileClass) cache[x, y];
                label[x, y] = Passable(cache[x, y]) ? 0 : -1;
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
        if (plan.Owner == null || !TryComp<MapGridComponent>(plan.Grid, out var gridComp))
            return;

        EnsureCls(plan, plan.Owner, gridComp);
        if (plan.Labels != null && plan.LabelsVersion == plan.ClsVersion)
            return;

        BuildLabels(plan, plan.Owner, gridComp, out _, out var label, out var good);
        plan.Labels = label;
        plan.LabelsVersion = plan.ClsVersion;
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

        if (!BuildLabels(plan, clutch, gridComp, out var cls, out var label, out var good))
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

        var now = _timing.CurTime;
        if (enclosed.Count == 0)
        {
            plan.EnclosedSince = TimeSpan.Zero;
            return;
        }

        if (plan.EnclosedSince == TimeSpan.Zero)
        {
            plan.EnclosedSince = now;
            return;
        }

        if ((now - plan.EnclosedSince).TotalSeconds < 60.0)
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
            if (FindStructure(clutch, plan, gridComp, Abs(plan, entry.Offset), out var wall, out _))
                QueueDel(wall);

            SetClass(plan, entry.Offset, TileClass.Clear);

            entry.State = XenoEntryState.Removed;
            entry.RetryAt = now + TimeSpan.FromSeconds(90);
            plan.Claims.Remove(entry.Offset);
            plan.Open.Add(entry.Offset);
            plan.OpenList.Add(entry.Offset);
        }

        plan.EnclosedSince = TimeSpan.Zero;
        plan.Labels = null;
    }

    private void Audit(XenoClutchComponent clutch, TimeSpan now)
    {
        if (clutch.Plan is not { } plan || !TryComp<MapGridComponent>(plan.Grid, out var gridComp))
            return;

        if (now >= plan.NextRecheck)
        {
            plan.NextRecheck = now + TimeSpan.FromSeconds(60);
            plan.RecheckBlocked(_random, offset => IsOffsetBlocked(plan, gridComp, offset));
        }

        foreach (var entry in plan.Entries)
        {
            if (entry.State == XenoEntryState.Skipped && now >= entry.RetryAt)
                entry.State = XenoEntryState.Pending;
        }

        if (now >= plan.NextRevive)
        {
            plan.NextRevive = now + TimeSpan.FromSeconds(60);
            ReviveRemoved(clutch, plan, gridComp, now);
        }
    }

    private bool SafeToClose(TileClass[,] cls, int size, int ex, int ey)
    {
        var dx = new[] { 1, -1, 0, 0 };
        var dy = new[] { 0, 0, 1, -1 };

        for (var n = 0; n < 4; n++)
        {
            var sx = ex + dx[n];
            var sy = ey + dy[n];
            if (sx < 0 || sy < 0 || sx >= size || sy >= size || cls[sx, sy] is not (TileClass.Clear or TileClass.Door))
                continue;

            var seen = new bool[size, size];
            var stack = new Stack<(int X, int Y)>();
            stack.Push((sx, sy));
            seen[sx, sy] = true;
            var reachesBorder = false;

            while (stack.Count > 0 && !reachesBorder)
            {
                var (cx, cy) = stack.Pop();
                if (cx == 0 || cy == 0 || cx == size - 1 || cy == size - 1)
                {
                    reachesBorder = true;
                    break;
                }

                for (var i = 0; i < 4; i++)
                {
                    var nx = cx + dx[i];
                    var ny = cy + dy[i];
                    if (nx < 0 || ny < 0 || nx >= size || ny >= size || seen[nx, ny] || (nx == ex && ny == ey))
                        continue;

                    if (cls[nx, ny] is not (TileClass.Clear or TileClass.Door))
                        continue;

                    seen[nx, ny] = true;
                    stack.Push((nx, ny));
                }
            }

            if (!reachesBorder)
                return false;
        }

        return true;
    }

    private void ReviveRemoved(XenoClutchComponent clutch, XenoMazePlan plan, MapGridComponent gridComp, TimeSpan now)
    {
        var any = false;
        foreach (var entry in plan.Entries)
        {
            if (entry.State == XenoEntryState.Removed && now >= entry.RetryAt)
            {
                any = true;
                break;
            }
        }

        if (!any)
            return;

        BuildLabels(plan, clutch, gridComp, out var cls, out _, out _);
        var r = plan.Radius;
        var size = 2 * r + 1;
        var revived = false;

        foreach (var entry in plan.Entries)
        {
            if (entry.State != XenoEntryState.Removed || now < entry.RetryAt)
                continue;

            var ex = entry.Offset.X + r;
            var ey = entry.Offset.Y + r;
            if (ex < 1 || ey < 1 || ex >= size - 1 || ey >= size - 1 || cls[ex, ey] != TileClass.Clear)
            {
                entry.RetryAt = now + TimeSpan.FromSeconds(120);
                continue;
            }

            if (!SafeToClose(cls, size, ex, ey))
            {
                entry.RetryAt = now + TimeSpan.FromSeconds(120);
                continue;
            }

            entry.State = XenoEntryState.Pending;
            entry.Fails = 0;
            plan.Open.Remove(entry.Offset);
            plan.OpenList.Remove(entry.Offset);
            cls[ex, ey] = TileClass.Wall;
            revived = true;
        }

        if (revived)
            plan.Labels = null;
    }

    public bool AnyGap(XenoMazePlan plan)
    {
        foreach (var entry in plan.Entries)
        {
            if (entry.State == XenoEntryState.Pending)
                return true;

            if (entry.State == XenoEntryState.Skipped && entry.Fails < XenoMazePlan.StubbornFails)
                return true;
        }

        return false;
    }

    public int ParkUnreachable(XenoMazePlan plan, Vector2 from, TimeSpan now)
    {
        var parked = 0;
        foreach (var entry in plan.Entries)
        {
            if (entry.State != XenoEntryState.Pending || TryPickStand(plan, entry, from, out _))
                continue;

            entry.State = XenoEntryState.Skipped;
            entry.Fails = XenoMazePlan.StubbornFails;
            entry.RetryAt = now + TimeSpan.FromSeconds(60);
            plan.Claims.Remove(entry.Offset);
            parked++;
        }

        return parked;
    }

    public bool HasMendWork(Entity<XenoClutchComponent> hive, XenoMazePlan plan, TimeSpan now)
    {
        RefreshRepairLists(hive.Comp, plan, now);
        return plan.Damaged.Count > 0;
    }

    public XenoPlanEntry? PickEntry(Entity<XenoClutchComponent> hive, XenoMazePlan plan, Vector2 from,
        EntityUid builder, TimeSpan now, bool woundsOnly = false)
    {
        var field = WalkField(plan, from);
        if (field == null)
            return null;

        XenoPlanEntry? best = null;
        var bestCost = int.MaxValue;
        var minPriority = int.MaxValue;

        foreach (var entry in plan.Entries)
        {
            if (entry.State is XenoEntryState.Built or XenoEntryState.Removed)
                continue;

            if (woundsOnly && !entry.Wound)
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
                    entry.Wound = false;
                    entry.Fails = 0;
                    continue;
                case XenoTileState.Invalid:
                case XenoTileState.Blocked:
                    Skip(plan, entry, now, 120f);
                    continue;
                case XenoTileState.Busy:
                    continue;
            }

            if (!BestStand(plan, field, entry, out _, out var cost))
                continue;

            minPriority = Math.Min(minPriority, entry.Priority);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = entry;
            }
        }

        if (best != null)
            plan.Claims[best.Offset] = (builder, now + TimeSpan.FromSeconds(40));

        return best;
    }

    public int[,]? WalkField(XenoMazePlan plan, Vector2 from)
    {
        if (plan.Owner == null || !TryComp<MapGridComponent>(plan.Grid, out var gridComp))
            return null;

        EnsureCls(plan, plan.Owner, gridComp);
        var cache = plan.ClsCache;
        if (cache == null)
            return null;

        var r = plan.Radius;
        var size = 2 * r + 1;
        var tile = _maps.WorldToTile(plan.Grid, gridComp, from);
        var start = new Vector2i(tile.X - plan.Origin.X + r, tile.Y - plan.Origin.Y + r);

        if (plan.Walk != null && plan.Walk.GetLength(0) == size && plan.WalkVersion == plan.ClsVersion && plan.WalkFrom == start)
            return plan.Walk;

        var sx = -1;
        var sy = -1;
        for (var ring = 0; ring <= 2 && sx < 0; ring++)
        {
            for (var ox = -ring; ox <= ring && sx < 0; ox++)
            {
                for (var oy = -ring; oy <= ring; oy++)
                {
                    if (Math.Max(Math.Abs(ox), Math.Abs(oy)) != ring)
                        continue;

                    var cx = start.X + ox;
                    var cy = start.Y + oy;
                    if (cx < 0 || cy < 0 || cx >= size || cy >= size || !Passable(cache[cx, cy]))
                        continue;

                    sx = cx;
                    sy = cy;
                    break;
                }
            }
        }

        if (sx < 0)
            return null;

        var dist = plan.Walk;
        if (dist == null || dist.GetLength(0) != size)
        {
            dist = new int[size, size];
            plan.Walk = dist;
            plan.WalkQueue = new int[size * size];
        }
        else
        {
            Array.Clear(dist);
        }

        var queue = plan.WalkQueue!;
        var head = 0;
        var tail = 0;
        dist[sx, sy] = 1;
        queue[tail++] = sx * size + sy;

        while (head < tail)
        {
            var cur = queue[head++];
            var qx = cur / size;
            var qy = cur % size;
            var next = dist[qx, qy] + 1;

            for (var i = 0; i < 4; i++)
            {
                var nx = qx + Dx[i];
                var ny = qy + Dy[i];
                if (nx < 0 || ny < 0 || nx >= size || ny >= size || dist[nx, ny] != 0 || !Passable(cache[nx, ny]))
                    continue;

                dist[nx, ny] = next;
                queue[tail++] = nx * size + ny;
            }
        }

        plan.WalkFrom = start;
        plan.WalkVersion = plan.ClsVersion;
        return dist;
    }

    public bool WalkClear(XenoMazePlan plan, Vector2 from, Vector2 to)
    {
        var cache = plan.ClsCache;
        var r = plan.Radius;
        var size = 2 * r + 1;
        if (cache == null || cache.GetLength(0) != size || !TryComp<MapGridComponent>(plan.Grid, out var gridComp))
            return false;

        var delta = to - from;
        var len = delta.Length();
        if (len < 0.05f)
            return true;

        var dir = delta / len;
        var side = new Vector2(-dir.Y, dir.X) * 0.35f;

        for (var t = 0f; ; t += 0.5f)
        {
            var step = MathF.Min(t, len);
            for (var s = -1; s <= 1; s++)
            {
                var tile = _maps.WorldToTile(plan.Grid, gridComp, from + dir * step + side * s);
                var ix = tile.X - plan.Origin.X + r;
                var iy = tile.Y - plan.Origin.Y + r;
                if (ix < 0 || iy < 0 || ix >= size || iy >= size || !Passable(cache[ix, iy]))
                    return false;
            }

            if (t >= len)
                return true;
        }
    }

    public int WalkSteps(XenoMazePlan plan, Vector2 from, Vector2i offset)
    {
        var field = WalkField(plan, from);
        if (field == null)
            return -1;

        var x = offset.X + plan.Radius;
        var y = offset.Y + plan.Radius;
        if (x < 0 || y < 0 || x >= field.GetLength(0) || y >= field.GetLength(1))
            return -1;

        var d = field[x, y];
        return d == 0 ? -1 : d - 1;
    }

    private bool BestStand(XenoMazePlan plan, int[,] field, XenoPlanEntry entry, out Vector2i stand, out int cost)
    {
        stand = default;
        cost = int.MaxValue;
        var r = plan.Radius;
        var size = field.GetLength(0);

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                var cand = new Vector2i(entry.Offset.X + dx, entry.Offset.Y + dy);
                var fx = cand.X + r;
                var fy = cand.Y + r;
                if (fx < 0 || fy < 0 || fx >= size || fy >= size || !plan.Open.Contains(cand) || field[fx, fy] == 0)
                    continue;

                var score = (field[fx, fy] - 1) * 2 + (dx != 0 && dy != 0 ? 1 : 0);
                if (score >= cost)
                    continue;

                cost = score;
                stand = cand;
            }
        }

        return cost != int.MaxValue;
    }

    public bool TryPickStand(XenoMazePlan plan, XenoPlanEntry entry, Vector2 from, out Vector2i stand)
    {
        stand = default;
        var field = WalkField(plan, from);
        return field != null && BestStand(plan, field, entry, out stand, out _);
    }

    public bool TryWalkPath(XenoMazePlan plan, Vector2 from, Vector2i goal, List<Vector2i> path)
    {
        path.Clear();
        var field = WalkField(plan, from);
        if (field == null)
            return false;

        var r = plan.Radius;
        var size = field.GetLength(0);
        var x = goal.X + r;
        var y = goal.Y + r;
        if (x < 0 || y < 0 || x >= size || y >= size || field[x, y] == 0)
            return false;

        path.Add(goal);
        while (field[x, y] > 1)
        {
            var d = field[x, y];
            var moved = false;
            for (var i = 0; i < 4; i++)
            {
                var nx = x + Dx[i];
                var ny = y + Dy[i];
                if (nx < 0 || ny < 0 || nx >= size || ny >= size || field[nx, ny] != d - 1)
                    continue;

                x = nx;
                y = ny;
                path.Add(new Vector2i(x - r, y - r));
                moved = true;
                break;
            }

            if (!moved)
            {
                path.Clear();
                return false;
            }
        }

        path.Reverse();
        return true;
    }

    public bool TryRandomReachable(XenoMazePlan plan, Vector2 from, int maxSteps, out Vector2i tile)
    {
        tile = default;
        var field = WalkField(plan, from);
        if (field == null)
            return false;

        var origin = new Vector2i(plan.WalkFrom.X - plan.Radius, plan.WalkFrom.Y - plan.Radius);
        for (var i = 0; i < 30; i++)
        {
            var cand = new Vector2i(origin.X + _random.Next(-12, 13), origin.Y + _random.Next(-12, 13));
            if (!plan.Open.Contains(cand))
                continue;

            var steps = WalkSteps(plan, from, cand);
            if (steps < 3 || steps > maxSteps)
                continue;

            tile = cand;
            return true;
        }

        return false;
    }

    public bool TryPickExit(XenoMazePlan plan, Vector2 from, out Vector2 world)
    {
        world = default;
        var r = plan.Radius;
        var gates = new[] { new Vector2i(r, 0), new Vector2i(-r, 0), new Vector2i(0, r), new Vector2i(0, -r) };
        var start = _random.Next(gates.Length);

        for (var i = 0; i < gates.Length; i++)
        {
            var gate = gates[(start + i) % gates.Length];
            if (!CanReach(plan, from, gate))
                continue;

            var outward = new Vector2(Math.Sign(gate.X), Math.Sign(gate.Y));
            world = EntryWorld(plan, gate) + outward * (8f + _random.NextFloat() * 6f);
            return true;
        }

        return false;
    }

    public bool TryRandomOpen(XenoMazePlan plan, Vector2 from, out Vector2i tile)
    {
        tile = default;

        for (var i = 0; i < 48 && plan.OpenList.Count > 0; i++)
        {
            var sample = plan.OpenList[_random.Next(plan.OpenList.Count)];
            if (!CanReach(plan, from, sample))
                continue;

            tile = sample;
            return true;
        }

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
        entry.Fails++;
        entry.RetryAt = now + TimeSpan.FromSeconds(Math.Min(seconds, 10f * entry.Fails));
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

    public bool TryPickSpot(XenoMazePlan plan, int minRing, int maxRing, out Vector2i tile, Vector2? from = null, float minFrom = 0f)
    {
        tile = default;

        for (var i = 0; i < 96 && plan.OpenList.Count > 0; i++)
        {
            var sample = plan.OpenList[_random.Next(plan.OpenList.Count)];
            var sampleRing = Math.Max(Math.Abs(sample.X), Math.Abs(sample.Y));
            if (sampleRing < minRing || sampleRing > maxRing)
                continue;

            if (from is { } start
                && (!CanReach(plan, start, sample) || (minFrom > 0f && (EntryWorld(plan, sample) - start).Length() < minFrom)))
            {
                continue;
            }

            tile = sample;
            return true;
        }

        var count = 0;
        foreach (var t in plan.OpenList)
        {
            var ring = Math.Max(Math.Abs(t.X), Math.Abs(t.Y));
            if (ring < minRing || ring > maxRing)
                continue;

            if (from is { } origin && (!CanReach(plan, origin, t) || (minFrom > 0f && (EntryWorld(plan, t) - origin).Length() < minFrom)))
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
                entry.Wound = false;
                entry.Fails = 0;
                SetClass(plan, entry.Offset, entry.Kind == XenoPlanKind.Door ? TileClass.Door : TileClass.Wall);
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
        entry.Wound = false;
        entry.Fails = 0;
        SetClass(plan, entry.Offset, entry.Kind == XenoPlanKind.Door ? TileClass.Door : TileClass.Wall);
        plan.Claims.Remove(entry.Offset);
        return XenoPlaceResult.Placed;
    }

    private bool FindStructure(XenoClutchComponent clutch, XenoMazePlan plan, MapGridComponent gridComp, Vector2i tile,
        out EntityUid uid, out string id)
    {
        uid = default;
        id = string.Empty;

        foreach (var anchored in _maps.GetAnchoredEntities(plan.Grid, gridComp, tile))
        {
            var protoId = MetaData(anchored).EntityPrototype?.ID;
            if (protoId == null)
                continue;

            if (protoId == clutch.WallProto.Id || protoId == clutch.ReinforcedProto.Id || protoId == clutch.DoorProto.Id)
            {
                uid = anchored;
                id = protoId;
                return true;
            }
        }

        return false;
    }

    private void RefreshRepairLists(XenoClutchComponent clutch, XenoMazePlan plan, TimeSpan now)
    {
        if (now < plan.RepairScanAt)
            return;

        plan.RepairScanAt = now + TimeSpan.FromSeconds(8);
        plan.Damaged.Clear();
        plan.Weak.Clear();

        if (!TryComp<MapGridComponent>(plan.Grid, out var gridComp))
            return;

        foreach (var entry in plan.Entries)
        {
            if (entry.State != XenoEntryState.Built)
                continue;

            if (!FindStructure(clutch, plan, gridComp, Abs(plan, entry.Offset), out var uid, out var id))
                continue;

            if (TryComp<DamageableComponent>(uid, out var dmg) && _damageable.GetTotalDamage((uid, dmg)).Float() > 0.5f)
                plan.Damaged.Add(entry);

            if (entry.Kind == XenoPlanKind.Wall && id == clutch.WallProto.Id)
                plan.Weak.Add(entry);
        }
    }

    private bool PickFrom(XenoMazePlan plan, List<XenoPlanEntry> list, Vector2 from, EntityUid builder, TimeSpan now,
        bool byPriority, out XenoPlanEntry best)
    {
        best = default!;
        var field = WalkField(plan, from);
        if (field == null)
            return false;

        var found = false;
        var bestPriority = int.MaxValue;
        var bestCost = int.MaxValue;

        foreach (var entry in list)
        {
            if (entry.State != XenoEntryState.Built || now < entry.RetryAt)
                continue;

            if (plan.Claims.TryGetValue(entry.Offset, out var claim) && claim.Builder != builder && now < claim.Expire)
                continue;

            if (byPriority && entry.Priority > bestPriority)
                continue;

            if (!BestStand(plan, field, entry, out _, out var cost))
                continue;

            var better = byPriority && entry.Priority < bestPriority || cost < bestCost;
            if (!better)
                continue;

            best = entry;
            bestPriority = entry.Priority;
            bestCost = cost;
            found = true;
        }

        if (found)
            plan.Claims[best.Offset] = (builder, now + TimeSpan.FromSeconds(40));

        return found;
    }

    public bool TryPickRepair(Entity<XenoClutchComponent> hive, XenoMazePlan plan, Vector2 from, EntityUid builder,
        TimeSpan now, bool allowUpgrade, out XenoPlanEntry entry, out XenoRepairKind kind)
    {
        entry = default!;
        kind = XenoRepairKind.Rebuild;

        if (PickEntry(hive, plan, from, builder, now, woundsOnly: true) is { } wound)
        {
            entry = wound;
            return true;
        }

        RefreshRepairLists(hive.Comp, plan, now);

        if (PickFrom(plan, plan.Damaged, from, builder, now, false, out entry))
        {
            kind = XenoRepairKind.Mend;
            return true;
        }

        if (allowUpgrade && PickFrom(plan, plan.Weak, from, builder, now, true, out entry))
        {
            kind = XenoRepairKind.Upgrade;
            return true;
        }

        return false;
    }

    public void Defer(XenoMazePlan plan, XenoPlanEntry entry, TimeSpan now, float seconds)
    {
        entry.RetryAt = now + TimeSpan.FromSeconds(seconds);
        plan.Claims.Remove(entry.Offset);
    }

    public XenoPlaceResult Repair(Entity<XenoClutchComponent> hive, XenoMazePlan plan, XenoPlanEntry entry,
        XenoRepairKind kind, TimeSpan now)
    {
        if (kind == XenoRepairKind.Rebuild)
            return Place(hive, plan, entry, now);

        if (!TryComp<MapGridComponent>(plan.Grid, out var gridComp))
            return XenoPlaceResult.Skip;

        if (!FindStructure(hive.Comp, plan, gridComp, Abs(plan, entry.Offset), out var wall, out var id))
        {
            entry.State = XenoEntryState.Pending;
            entry.Wound = true;
            SetClass(plan, entry.Offset, TileClass.Clear);
            plan.Claims.Remove(entry.Offset);
            return XenoPlaceResult.Skip;
        }

        plan.Claims.Remove(entry.Offset);

        if (kind == XenoRepairKind.Mend)
        {
            plan.Damaged.Remove(entry);
            if (TryComp<DamageableComponent>(wall, out var dmg))
            {
                var total = _damageable.GetTotalDamage((wall, dmg)).Float();
                if (total > 0f)
                    _damageable.HealEvenly(new Entity<DamageableComponent?>(wall, dmg), FixedPoint2.New(-total));
            }

            return XenoPlaceResult.Placed;
        }

        plan.Weak.Remove(entry);
        if (id != hive.Comp.WallProto.Id)
            return XenoPlaceResult.Skip;

        QueueDel(wall);
        Spawn(hive.Comp.ReinforcedProto, EntryCoords(plan, entry.Offset));
        entry.Kind = XenoPlanKind.Reinforced;
        return XenoPlaceResult.Placed;
    }
}
