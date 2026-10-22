// Nestation Anarhey CC BY-NC 3.0
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
    public EntityUid Grid;
    public Vector2i Origin;
    public int Radius;

    private static readonly Vector2i[] Dirs =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
    };

    private static bool InCore(Vector2i c) => Math.Abs(c.X) <= 1 && Math.Abs(c.Y) <= 1;

    public static XenoMazePlan Generate(IRobustRandom rng, int radius, float loopChance, float doorChance, int entrances)
    {
        radius = Math.Max(6, radius & ~1);

        var plan = new XenoMazePlan { Radius = radius };
        var core = new Vector2i(0, 0);
        var carved = new HashSet<Vector2i>();
        var doors = new HashSet<Vector2i>();
        var coreDoor = core;

        List<(Vector2i Node, Vector2i Pass)> Links(Vector2i node)
        {
            var list = new List<(Vector2i, Vector2i)>();
            var sources = new List<Vector2i>();
            if (node == core)
            {
                sources.Add(new Vector2i(1, 1));
                sources.Add(new Vector2i(1, -1));
                sources.Add(new Vector2i(-1, 1));
                sources.Add(new Vector2i(-1, -1));
            }
            else
            {
                sources.Add(node);
            }

            foreach (var src in sources)
            {
                foreach (var d in Dirs)
                {
                    var nb = new Vector2i(src.X + d.X * 2, src.Y + d.Y * 2);
                    if (Math.Abs(nb.X) > radius - 1 || Math.Abs(nb.Y) > radius - 1)
                        continue;

                    if (node == core && InCore(nb))
                        continue;

                    var pass = new Vector2i(src.X + d.X, src.Y + d.Y);
                    list.Add((InCore(nb) ? core : nb, pass));
                }
            }

            return list;
        }

        var visited = new HashSet<Vector2i> { core };
        var active = new List<Vector2i> { core };
        while (active.Count > 0)
        {
            var idx = rng.Prob(0.7f) ? active.Count - 1 : rng.Next(active.Count);
            var node = active[idx];

            var options = new List<(Vector2i Node, Vector2i Pass)>();
            foreach (var link in Links(node))
            {
                if (!visited.Contains(link.Node))
                    options.Add(link);
            }

            if (options.Count == 0)
            {
                active.RemoveAt(idx);
                continue;
            }

            var pick = options[rng.Next(options.Count)];
            carved.Add(pick.Pass);
            visited.Add(pick.Node);
            active.Add(pick.Node);

            if (node == core)
            {
                coreDoor = pick.Pass;
                active.Remove(core);
            }
        }

        for (var x = -(radius - 1); x <= radius - 1; x += 2)
        {
            for (var y = -(radius - 1); y <= radius - 1; y += 2)
            {
                var cell = new Vector2i(x, y);
                if (InCore(cell))
                    continue;

                foreach (var d in new[] { new Vector2i(1, 0), new Vector2i(0, 1) })
                {
                    var nb = new Vector2i(x + d.X * 2, y + d.Y * 2);
                    if (Math.Abs(nb.X) > radius - 1 || Math.Abs(nb.Y) > radius - 1 || InCore(nb))
                        continue;

                    var pass = new Vector2i(x + d.X, y + d.Y);
                    if (!carved.Contains(pass) && rng.Prob(loopChance))
                        carved.Add(pass);
                }
            }
        }

        doors.Add(coreDoor);

        var half = radius / 2;
        for (var i = 0; i < entrances; i++)
        {
            var t = 2 * rng.Next(-half, half) + 1;
            var tile = (i % 4) switch
            {
                0 => new Vector2i(radius, t),
                1 => new Vector2i(-radius, t),
                2 => new Vector2i(t, radius),
                _ => new Vector2i(t, -radius),
            };

            carved.Add(tile);
            doors.Add(tile);
        }

        foreach (var pass in carved)
        {
            if (doors.Contains(pass) || !rng.Prob(doorChance))
                continue;

            var near = false;
            foreach (var other in doors)
            {
                if (Math.Abs(other.X - pass.X) + Math.Abs(other.Y - pass.Y) <= 3)
                    near = true;
            }

            if (!near)
                doors.Add(pass);
        }

        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                var t = new Vector2i(x, y);
                var ring = Math.Max(Math.Abs(x), Math.Abs(y));

                if (InCore(t))
                {
                    plan.Open.Add(t);
                    continue;
                }

                if (x % 2 != 0 && y % 2 != 0)
                {
                    plan.Open.Add(t);
                    continue;
                }

                if (carved.Contains(t) && !doors.Contains(t))
                {
                    plan.Open.Add(t);
                    continue;
                }

                XenoPlanKind kind;
                int priority;
                if (doors.Contains(t))
                {
                    kind = XenoPlanKind.Door;
                    priority = t == coreDoor ? 0 : ring == radius ? radius / 2 + 1 : (ring + 1) / 2;
                }
                else if (ring == 2)
                {
                    kind = XenoPlanKind.Reinforced;
                    priority = 0;
                }
                else
                {
                    kind = XenoPlanKind.Wall;
                    priority = (ring + 1) / 2;
                }

                plan.Entries.Add(new XenoPlanEntry
                {
                    Offset = t,
                    Kind = kind,
                    Priority = priority,
                    Rotated = kind == XenoPlanKind.Door && x % 2 == 0,
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
