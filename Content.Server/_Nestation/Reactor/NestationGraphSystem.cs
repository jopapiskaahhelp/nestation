// Nestation CC BY-NC 3.0
using Content.Shared._Nestation.Reactor;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.Reactor;

public readonly record struct NestationLink(EntityUid Device, NestationPortRole Role);

public sealed class NestationGraphSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private EntityQuery<NestationPipeComponent> _pipes;
    private EntityQuery<NestationPortsComponent> _ports;
    private EntityQuery<NestationPoolComponent> _pools;
    private EntityQuery<TransformComponent> _xforms;
    private EntityQuery<MapGridComponent> _grids;

    private readonly Dictionary<(EntityUid Grid, Vector2i Tile), List<EntityUid>> _occupancy = new();
    private uint _occupancyTick;
    private bool _occupancyBuilt;

    private static readonly Direction[] Cardinals =
    {
        Direction.North,
        Direction.East,
        Direction.South,
        Direction.West,
    };

    public override void Initialize()
    {
        base.Initialize();
        _pipes = GetEntityQuery<NestationPipeComponent>();
        _ports = GetEntityQuery<NestationPortsComponent>();
        _pools = GetEntityQuery<NestationPoolComponent>();
        _xforms = GetEntityQuery<TransformComponent>();
        _grids = GetEntityQuery<MapGridComponent>();
    }

    public List<NestationLink> Trace(EntityUid device, NestationPortRole role)
    {
        var result = new List<NestationLink>();
        RefreshOccupancy();

        if (!_ports.TryGetComponent(device, out var ports) ||
            !_xforms.TryGetComponent(device, out var xform) ||
            !TryGetTile(xform, out var grid, out var tile))
        {
            return result;
        }

        var visited = new HashSet<EntityUid> { device };
        var queue = new Queue<(EntityUid Node, NestationMedium Medium)>();
        var seen = new HashSet<NestationLink>();

        foreach (var port in ports.Ports)
        {
            if (port.Role != role)
                continue;

            var from = tile + RotateOffset(port.Offset, xform);
            Probe(grid, from, WorldSide(port.Side, xform), port.Medium, visited, queue, result, seen);
        }

        Flood(grid, visited, queue, result, seen);
        return result;
    }

    public List<NestationLink> TraceControl(EntityUid device)
    {
        var result = new List<NestationLink>();
        RefreshOccupancy();

        if (!_ports.TryGetComponent(device, out var ports) ||
            !_xforms.TryGetComponent(device, out var xform) ||
            !TryGetTile(xform, out var grid, out var tile))
        {
            return result;
        }

        var visited = new HashSet<EntityUid> { device };
        var queue = new Queue<(EntityUid Node, NestationMedium Medium)>();
        var seen = new HashSet<NestationLink>();

        foreach (var offset in Footprint(ports.Size, xform))
        {
            foreach (var dir in Cardinals)
            {
                Probe(grid, tile + offset, dir, NestationMedium.Control, visited, queue, result, seen);
            }
        }

        Flood(grid, visited, queue, result, seen);
        return result;
    }

    public List<EntityUid> GetPoolGroup(EntityUid seed)
    {
        var group = new List<EntityUid>();

        if (!_pools.HasComponent(seed) ||
            !_xforms.TryGetComponent(seed, out var xform) ||
            !TryGetTile(xform, out var grid, out _))
        {
            return group;
        }

        var visited = new HashSet<EntityUid> { seed };
        var queue = new Queue<EntityUid>();
        queue.Enqueue(seed);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            group.Add(current);

            if (!_xforms.TryGetComponent(current, out var cx) || !TryGetTile(cx, out _, out var tile))
                continue;

            foreach (var dir in Cardinals)
            {
                foreach (var other in _map.GetAnchoredEntities(grid, tile + dir.ToIntVec()))
                {
                    if (visited.Contains(other) || !_pools.HasComponent(other))
                        continue;

                    visited.Add(other);
                    queue.Enqueue(other);
                }
            }
        }

        return group;
    }

    private void RefreshOccupancy()
    {
        var tick = _timing.CurTick.Value;

        if (_occupancyBuilt && tick == _occupancyTick)
            return;

        _occupancyTick = tick;
        _occupancyBuilt = true;
        _occupancy.Clear();

        var query = EntityQueryEnumerator<NestationPortsComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var ports, out var xform))
        {
            if (!TryGetTile(xform, out var grid, out var center))
                continue;

            foreach (var offset in Footprint(ports.Size, xform))
            {
                var key = (grid.Owner, center + offset);

                if (!_occupancy.TryGetValue(key, out var list))
                {
                    list = new List<EntityUid>();
                    _occupancy[key] = list;
                }

                list.Add(uid);
            }
        }
    }

    private IEnumerable<Vector2i> NodeTiles(EntityUid node, TransformComponent xform)
    {
        if (_ports.TryGetComponent(node, out var ports))
            return Footprint(ports.Size, xform);

        return new[] { Vector2i.Zero };
    }

    private void Flood(
        Entity<MapGridComponent> grid,
        HashSet<EntityUid> visited,
        Queue<(EntityUid Node, NestationMedium Medium)> queue,
        List<NestationLink> result,
        HashSet<NestationLink> seen)
    {
        while (queue.Count > 0)
        {
            var (node, medium) = queue.Dequeue();

            if (!_xforms.TryGetComponent(node, out var xform) || !TryGetTile(xform, out _, out var tile))
                continue;

            foreach (var offset in NodeTiles(node, xform))
            {
                foreach (var dir in Cardinals)
                {
                    Probe(grid, tile + offset, dir, medium, visited, queue, result, seen);
                }
            }
        }
    }

    private void Probe(
        Entity<MapGridComponent> grid,
        Vector2i from,
        Direction dir,
        NestationMedium medium,
        HashSet<EntityUid> visited,
        Queue<(EntityUid Node, NestationMedium Medium)> queue,
        List<NestationLink> result,
        HashSet<NestationLink> seen)
    {
        var target = from + dir.ToIntVec();
        var back = dir.GetOpposite();

        foreach (var other in _map.GetAnchoredEntities(grid, target))
        {
            if (visited.Contains(other))
                continue;

            if (_pipes.TryGetComponent(other, out var pipe) && pipe.Medium == medium)
            {
                visited.Add(other);
                queue.Enqueue((other, medium));
            }
        }

        if (!_occupancy.TryGetValue((grid.Owner, target), out var devices))
            return;

        foreach (var other in devices)
        {
            if (visited.Contains(other) ||
                !_ports.TryGetComponent(other, out var ports) ||
                !_xforms.TryGetComponent(other, out var xform) ||
                !TryGetTile(xform, out _, out var center))
            {
                continue;
            }

            if (medium == NestationMedium.Control)
            {
                if (!ports.Control)
                    continue;

                visited.Add(other);
                queue.Enqueue((other, medium));
                var controlLink = new NestationLink(other, NestationPortRole.ConsoleLink);

                if (seen.Add(controlLink))
                    result.Add(controlLink);

                continue;
            }

            foreach (var port in ports.Ports)
            {
                if (port.Medium != medium ||
                    WorldSide(port.Side, xform) != back ||
                    center + RotateOffset(port.Offset, xform) != target)
                {
                    continue;
                }

                var link = new NestationLink(other, port.Role);

                if (seen.Add(link))
                    result.Add(link);
            }
        }
    }

    private bool TryGetTile(TransformComponent xform, out Entity<MapGridComponent> grid, out Vector2i tile)
    {
        grid = default;
        tile = default;

        if (!xform.Anchored || xform.GridUid is not { } gridUid || !_grids.TryGetComponent(gridUid, out var gridComp))
            return false;

        grid = (gridUid, gridComp);
        tile = _map.TileIndicesFor(grid, xform.Coordinates);
        return true;
    }

    private static Direction WorldSide(Direction local, TransformComponent xform)
    {
        return (local.ToAngle() + xform.LocalRotation).GetCardinalDir();
    }

    private static Vector2i RotateOffset(Vector2i local, TransformComponent xform)
    {
        var east = WorldSide(Direction.East, xform).ToIntVec();
        var north = WorldSide(Direction.North, xform).ToIntVec();
        return east * local.X + north * local.Y;
    }

    private static IEnumerable<Vector2i> Footprint(Vector2i size, TransformComponent xform)
    {
        var halfX = (size.X - 1) / 2;
        var halfY = (size.Y - 1) / 2;

        for (var x = -halfX; x <= halfX; x++)
        {
            for (var y = -halfY; y <= halfY; y++)
            {
                yield return RotateOffset(new Vector2i(x, y), xform);
            }
        }
    }

    public static EntityUid? First(List<NestationLink> links, NestationPortRole role)
    {
        foreach (var link in links)
        {
            if (link.Role == role)
                return link.Device;
        }

        return null;
    }
}
