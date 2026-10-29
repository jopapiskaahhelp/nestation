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
}

public sealed class XenoMazePlan
{
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

    private static Tile Layout(int x, int y, int coreSide, out int priority)
    {
        var ax = Math.Abs(x);
        var ay = Math.Abs(y);
        var k = Math.Max(ax, ay);
        priority = 0;

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

    public static XenoMazePlan Generate(IRobustRandom rng, Func<Vector2i, bool> blocked)
    {
        const int radius = 10;
        var plan = new XenoMazePlan { Radius = radius };
        var coreSide = rng.Next(4);

        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                var offset = new Vector2i(x, y);
                var tile = Layout(x, y, coreSide, out var priority);

                if (blocked(offset))
                    continue;

                if (tile == Tile.Open)
                {
                    plan.Open.Add(offset);
                    continue;
                }

                var kind = tile switch
                {
                    Tile.Reinforced => XenoPlanKind.Reinforced,
                    Tile.Door => XenoPlanKind.Door,
                    _ => XenoPlanKind.Wall,
                };

                plan.Entries.Add(new XenoPlanEntry
                {
                    Offset = offset,
                    Kind = kind,
                    Priority = priority,
                    Rotated = kind == XenoPlanKind.Door && Math.Abs(x) > Math.Abs(y),
                    Tie = rng.NextFloat(),
                });
            }
        }

        plan.Entries.Sort((a, b) =>
        {
            var c = a.Priority.CompareTo(b.Priority);
            return c != 0 ? c : a.Tie.CompareTo(b.Tie);
        });

        foreach (var entry in plan.Entries)
        {
            plan.ByOffset[entry.Offset] = entry;
        }

        plan.OpenList.AddRange(plan.Open);
        return plan;
    }
}
