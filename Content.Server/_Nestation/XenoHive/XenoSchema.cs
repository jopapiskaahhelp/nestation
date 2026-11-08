// Nestation new city CC BY-NC 3.0
using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Server._Nestation.XenoHive;

public sealed class XenoSituation
{
    public readonly float[] Threat = new float[8];
    public readonly float[] Nearest = new float[8];
    public int Players;
    public int Inside;
    public int Wounds;

    public XenoSituation()
    {
        Array.Fill(Nearest, float.MaxValue);
    }
}

public readonly record struct XenoSchemaTile(Vector2i Offset, XenoPlanKind Kind, bool Rotated);

public static class XenoSchemaPlanner
{
    public const float MinThreat = 0.3f;
    public const float MidThreat = 0.8f;
    public const float HighThreat = 2f;
    public const int MinRadius = 7;

    public static int SectorOf(Vector2 rel)
    {
        var angle = MathF.Atan2(rel.Y, rel.X);
        return ((int) MathF.Round(angle / (MathF.PI / 4f)) + 8) % 8;
    }

    private static Vector2 Dir(int sector)
    {
        var a = sector * MathF.PI / 4f;
        return new Vector2(MathF.Cos(a), MathF.Sin(a));
    }

    private static int Snap(float v)
    {
        return (int) MathF.Floor(v + 0.5f);
    }

    private static List<(Vector2i Tile, float A)> Line(Vector2 center, Vector2 tangent, int half)
    {
        var result = new List<(Vector2i Tile, float A)>();
        Vector2i? prev = null;

        for (var a = -half; a <= half; a++)
        {
            var p = center + tangent * a;
            var tile = new Vector2i(Snap(p.X), Snap(p.Y));

            if (prev is { } last)
            {
                if (tile == last)
                    continue;

                if (tile.X != last.X && tile.Y != last.Y)
                    result.Add((new Vector2i(tile.X, last.Y), a));
            }

            result.Add((tile, a));
            prev = tile;
        }

        return result;
    }

    private static HashSet<float> DoorSpots(List<(Vector2i Tile, float A)> line, float gap, float gapHalf)
    {
        var spots = new HashSet<float>();
        var walls = 0;

        foreach (var (_, a) in line)
        {
            if (MathF.Abs(a) <= 1.2f && gapHalf <= 0f)
            {
                spots.Add(a);
                walls = 0;
                continue;
            }

            if (gapHalf > 0f && MathF.Abs(a - gap) <= gapHalf)
            {
                walls = 0;
                continue;
            }

            walls++;
            if (walls < 6)
                continue;

            spots.Add(a);
            walls = 0;
        }

        return spots;
    }

    private static void Add(XenoMazePlan plan, List<XenoSchemaTile> result, HashSet<Vector2i> used, int cap,
        Vector2i tile, XenoPlanKind kind, Vector2 dir)
    {
        if (result.Count >= cap
            || Math.Max(Math.Abs(tile.X), Math.Abs(tile.Y)) < MinRadius
            || !plan.Open.Contains(tile)
            || !used.Add(tile))
        {
            return;
        }

        var rotated = kind == XenoPlanKind.Door && Math.Abs(dir.X) > Math.Abs(dir.Y);
        result.Add(new XenoSchemaTile(tile, kind, rotated));
    }

    private static void Bulwark(XenoMazePlan plan, List<XenoSchemaTile> result, HashSet<Vector2i> used, int cap,
        Vector2 dir, int radius, int half, XenoPlanKind wall)
    {
        var tangent = new Vector2(-dir.Y, dir.X);
        var spots = DoorSpots(Line(dir * radius, tangent, half), 0f, 0f);

        for (var layer = 0; layer < 2; layer++)
        {
            foreach (var (tile, a) in Line(dir * (radius + layer), tangent, half))
            {
                Add(plan, result, used, cap, tile, spots.Contains(a) ? XenoPlanKind.Door : wall, dir);
            }
        }
    }

    private static void Baffles(XenoMazePlan plan, List<XenoSchemaTile> result, HashSet<Vector2i> used, int cap,
        Vector2 dir, int radius)
    {
        const int half = 7;
        var tangent = new Vector2(-dir.Y, dir.X);

        for (var i = 0; i < 3; i++)
        {
            var gap = (i % 2 == 0 ? -1f : 1f) * (half - 2.5f);
            var line = Line(dir * (radius + 3 + 3 * i), tangent, half);
            var spots = DoorSpots(line, gap, 1.6f);

            foreach (var (tile, a) in line)
            {
                if (MathF.Abs(a - gap) <= 1.6f)
                    continue;

                Add(plan, result, used, cap, tile, spots.Contains(a) ? XenoPlanKind.Door : XenoPlanKind.Wall, dir);
            }
        }
    }

    public static List<XenoSchemaTile> Build(XenoMazePlan plan, XenoSituation situation, int maxSectors, int maxEntries)
    {
        var result = new List<XenoSchemaTile>();
        var used = new HashSet<Vector2i>();
        var sectors = new List<int>();

        for (var i = 0; i < 8; i++)
        {
            if (situation.Threat[i] >= MinThreat)
                sectors.Add(i);
        }

        sectors.Sort((a, b) => situation.Threat[b].CompareTo(situation.Threat[a]));

        var maxRadius = Math.Max(MinRadius, plan.Radius - 2);
        var count = Math.Min(sectors.Count, maxSectors);

        for (var i = 0; i < count && result.Count < maxEntries; i++)
        {
            var sector = sectors[i];
            var threat = situation.Threat[sector];
            var near = situation.Nearest[sector];
            var dir = Dir(sector);
            var radius = near < float.MaxValue
                ? Math.Clamp((int) (near * 0.6f), MinRadius, maxRadius)
                : maxRadius;

            var high = threat >= HighThreat;
            Bulwark(plan, result, used, maxEntries, dir, radius, high ? 10 : 7,
                high ? XenoPlanKind.Reinforced : XenoPlanKind.Wall);

            if (threat >= MidThreat)
                Baffles(plan, result, used, maxEntries, dir, radius);
        }

        return result;
    }
}
