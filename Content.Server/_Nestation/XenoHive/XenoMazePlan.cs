// Nestation new city CC BY-NC 3.0
using Robust.Shared.Maths;
using Robust.Shared.Random;

namespace Content.Server._Nestation.XenoHive;

public enum XenoPlanKind : byte
{
    Wall,
    Reinforced,
    Door,
}

public enum XenoEntryState : byte
{
    Pending,
    Built,
    Skipped,
    Removed,
}

public sealed class XenoPlanEntry
{
    public Vector2i Offset;
    public XenoPlanKind Kind;
    public int Priority;
    public bool Rotated;
    public float Tie;
    public XenoEntryState State;
    public TimeSpan RetryAt;

    public bool Wound;
    public int Fails;
    public int Schema;
}

public sealed class XenoMazePlan
{
    public const int BaseRadius = 10;
    public const int RingWidth = 6;
    public const int RingsPerExpansion = 2;
    public const int ExpandStep = RingWidth * RingsPerExpansion;
    public const int StubbornFails = 6;
    public const int SchemaPriority = 3;

    public readonly List<XenoPlanEntry> Entries = new();
    public readonly HashSet<Vector2i> Open = new();
    public readonly List<Vector2i> OpenList = new();
    public readonly Dictionary<Vector2i, (EntityUid Builder, TimeSpan Expire)> Claims = new();
    public readonly Dictionary<Vector2i, XenoPlanEntry> ByOffset = new();
    public readonly HashSet<int> Good = new();
    public XenoClutchComponent? Owner;
    public int[,]? Labels;
    public TimeSpan LabelsAt;
    public EntityUid Grid;
    public Vector2i Origin;
    public int Radius;
    public int CoreSide;

    public readonly List<XenoPlanEntry> Damaged = new();
    public readonly List<XenoPlanEntry> Weak = new();
    public TimeSpan RepairScanAt;
    public TimeSpan EnclosedSince;
    public TimeSpan NextRevive;
    public TimeSpan NextRecheck;
    public TimeSpan ClsFullAt;
    public byte[,]? ClsCache;
    public int ClsVersion;
    public int LabelsVersion;
    public int[,]? Walk;
    public int[]? WalkQueue;
    public Vector2i WalkFrom;
    public int WalkVersion = -1;
    public readonly float[] Pressure = new float[8];
    public int SchemaEpoch;
    public XenoSituation? Situation;

    private readonly List<DeferredTile> _deferred = new();

    private readonly record struct DeferredTile(Vector2i Offset, Tile Tile, int Priority);

    private enum Tile : byte
    {
        Open,
        Wall,
        Reinforced,
        Door,
    }

    private static bool IsCoreDoor(int x, int y, int side)
    {
        return side switch
        {
            0 => x == 3 && Math.Abs(y) <= 1,
            1 => x == -3 && Math.Abs(y) <= 1,
            2 => y == 3 && Math.Abs(x) <= 1,
            _ => y == -3 && Math.Abs(x) <= 1,
        };
    }

    private static bool IsInnerDoor(int x, int y)
    {
        var ax = Math.Abs(x);
        var ay = Math.Abs(y);
        if (ax == 6 && ay <= 5)
            return ay <= 1 || ay == 5;

        if (ay == 6 && ax <= 5)
            return ax <= 1 || ax == 4;

        return false;
    }

    private static bool IsGate(int x, int y)
    {
        return (Math.Abs(x) == 10 && Math.Abs(y) <= 1) || (Math.Abs(y) == 10 && Math.Abs(x) <= 1);
    }

    private static Tile OuterLayout(int x, int y, out int priority)
    {
        var ax = Math.Abs(x);
        var ay = Math.Abs(y);
        var k = Math.Max(ax, ay);
        var g = k - BaseRadius;
        priority = 0;

        if (g % RingWidth != 0)
            return Tile.Open;

        var ring = g / RingWidth;
        var lateral = Math.Abs(ax > ay ? y : x);
        var gate = ring % 2 == 0
            ? lateral <= 1
            : Math.Abs(lateral - k / 2) <= 1;

        priority = gate ? 4 * ring : 4 * ring + 2;
        return gate ? Tile.Door : Tile.Wall;
    }

    private static Tile Layout(int x, int y, int coreSide, out int priority)
    {
        var ax = Math.Abs(x);
        var ay = Math.Abs(y);
        var k = Math.Max(ax, ay);
        priority = 0;

        if (k > BaseRadius)
            return OuterLayout(x, y, out priority);

        if (k <= 2 || k == 4 || k == 5)
            return Tile.Open;

        if (k == 3)
            return IsCoreDoor(x, y, coreSide) ? Tile.Door : Tile.Reinforced;

        priority = k == 6 ? 1 : 2;

        if (k == 6)
            return IsInnerDoor(x, y) ? Tile.Door : Tile.Wall;

        if (k == 10)
            return IsGate(x, y) ? Tile.Door : Tile.Wall;

        if (ax >= 7)
            return ay == 3 ? Tile.Wall : Tile.Open;

        if (ax == 6 || ax == 3)
            return Tile.Wall;

        return Tile.Open;
    }

    private void AddTile(IRobustRandom rng, Func<Vector2i, bool> blocked, int x, int y)
    {
        var offset = new Vector2i(x, y);
        var tile = Layout(x, y, CoreSide, out var priority);

        if (blocked(offset))
        {
            _deferred.Add(new DeferredTile(offset, tile, priority));
            return;
        }

        Insert(rng, offset, tile, priority);
    }

    private void Insert(IRobustRandom rng, Vector2i offset, Tile tile, int priority)
    {
        var x = offset.X;
        var y = offset.Y;

        if (tile == Tile.Open)
        {
            if (Open.Add(offset))
                OpenList.Add(offset);

            return;
        }

        var kind = tile switch
        {
            Tile.Reinforced => XenoPlanKind.Reinforced,
            Tile.Door => XenoPlanKind.Door,
            _ => XenoPlanKind.Wall,
        };

        var entry = new XenoPlanEntry
        {
            Offset = offset,
            Kind = kind,
            Priority = priority,
            Rotated = kind == XenoPlanKind.Door && Math.Abs(x) > Math.Abs(y),
            Tie = rng.NextFloat(),
        };

        Entries.Add(entry);
        ByOffset[offset] = entry;
    }

    private static List<Vector2i> RingTiles(int k)
    {
        var list = new List<Vector2i>(8 * k);
        for (var y = -k; y < k; y++)
        {
            list.Add(new Vector2i(k, y));
        }

        for (var x = k; x > -k; x--)
        {
            list.Add(new Vector2i(x, k));
        }

        for (var y = k; y > -k; y--)
        {
            list.Add(new Vector2i(-k, y));
        }

        for (var x = -k; x < k; x++)
        {
            list.Add(new Vector2i(x, -k));
        }

        return list;
    }

    private void EnsureRingDoors(int k, int doorPriority)
    {
        var ring = RingTiles(k);
        var n = ring.Count;
        var start = 0;
        for (var i = 0; i < n; i++)
        {
            if (ByOffset.TryGetValue(ring[i], out var first) && first.Kind == XenoPlanKind.Door)
            {
                start = i;
                break;
            }
        }

        var walls = 0;
        var pending = false;
        for (var step = 1; step <= n; step++)
        {
            var tile = ring[(start + step) % n];
            if (!ByOffset.TryGetValue(tile, out var entry) || entry.Kind == XenoPlanKind.Door)
            {
                walls = 0;
                pending = false;
                continue;
            }

            walls++;
            if (walls < 6 && !pending)
                continue;

            if (Math.Abs(tile.X) == Math.Abs(tile.Y))
            {
                pending = true;
                continue;
            }

            entry.Kind = XenoPlanKind.Door;
            entry.Priority = doorPriority;
            entry.Rotated = Math.Abs(tile.X) > Math.Abs(tile.Y);
            walls = 0;
            pending = false;
        }
    }

    private void SortEntries()
    {
        Entries.Sort((a, b) =>
        {
            var c = a.Priority.CompareTo(b.Priority);
            return c != 0 ? c : a.Tie.CompareTo(b.Tie);
        });
    }

    public int CancelSchema()
    {
        var cancelled = 0;
        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            var entry = Entries[i];
            if (entry.Schema == 0 || entry.State == XenoEntryState.Built)
                continue;

            Entries.RemoveAt(i);
            ByOffset.Remove(entry.Offset);
            Claims.Remove(entry.Offset);
            entry.State = XenoEntryState.Removed;

            if (Open.Add(entry.Offset))
                OpenList.Add(entry.Offset);

            cancelled++;
        }

        return cancelled;
    }

    public int ApplySchema(IRobustRandom rng, List<XenoSchemaTile> tiles)
    {
        CancelSchema();
        SchemaEpoch++;
        var added = 0;

        foreach (var tile in tiles)
        {
            if (!Open.Remove(tile.Offset))
                continue;

            OpenList.Remove(tile.Offset);

            var entry = new XenoPlanEntry
            {
                Offset = tile.Offset,
                Kind = tile.Kind,
                Priority = SchemaPriority,
                Rotated = tile.Rotated,
                Tie = rng.NextFloat(),
                Schema = SchemaEpoch,
            };

            Entries.Add(entry);
            ByOffset[tile.Offset] = entry;
            added++;
        }

        if (added > 0)
        {
            SortEntries();
            Labels = null;
        }

        return added;
    }

    public int RecheckBlocked(IRobustRandom rng, Func<Vector2i, bool> blocked)
    {
        var revived = 0;
        for (var i = _deferred.Count - 1; i >= 0; i--)
        {
            var d = _deferred[i];
            if (blocked(d.Offset))
                continue;

            _deferred.RemoveAt(i);
            Insert(rng, d.Offset, d.Tile, d.Priority);
            revived++;
        }

        if (revived > 0)
        {
            SortEntries();
            Labels = null;
        }

        return revived;
    }

    public static XenoMazePlan Generate(IRobustRandom rng, Func<Vector2i, bool> blocked)
    {
        var plan = new XenoMazePlan { Radius = BaseRadius, CoreSide = rng.Next(4) };

        for (var x = -BaseRadius; x <= BaseRadius; x++)
        {
            for (var y = -BaseRadius; y <= BaseRadius; y++)
            {
                plan.AddTile(rng, blocked, x, y);
            }
        }

        plan.EnsureRingDoors(BaseRadius, 2);
        plan.SortEntries();
        return plan;
    }

    public static int AlignRadius(int max)
    {
        return BaseRadius + Math.Max(0, max - BaseRadius) / ExpandStep * ExpandStep;
    }

    public bool Expand(IRobustRandom rng, Func<Vector2i, bool> blocked, int maxRadius)
    {
        maxRadius = AlignRadius(maxRadius);
        if (Radius >= maxRadius)
            return false;

        var from = Radius;
        var to = Math.Min(maxRadius, Radius + ExpandStep);

        for (var x = -to; x <= to; x++)
        {
            for (var y = -to; y <= to; y++)
            {
                if (Math.Max(Math.Abs(x), Math.Abs(y)) <= from)
                    continue;

                AddTile(rng, blocked, x, y);
            }
        }

        for (var k = BaseRadius + RingWidth * ((from - BaseRadius) / RingWidth + 1); k <= to; k += RingWidth)
        {
            EnsureRingDoors(k, 4 * ((k - BaseRadius) / RingWidth));
        }

        Radius = to;
        SortEntries();
        Labels = null;
        return true;
    }
}
