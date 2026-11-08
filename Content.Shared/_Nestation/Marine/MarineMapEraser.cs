// Nestation new city CC BY-NC 3.0
using System.Numerics;

namespace Content.Shared._NeStation.Marine;

public static class MarineMapEraser
{
    public static void Erase(List<MarineMapStroke> strokes, Vector2 pos, float radius)
    {
        for (var i = strokes.Count - 1; i >= 0; i--)
        {
            var stroke = strokes[i];
            var pts = stroke.Points;
            var reach = radius + stroke.Size * 0.5f;

            if (pts.Count == 0)
            {
                strokes.RemoveAt(i);
                continue;
            }

            if (pts.Count == 1)
            {
                if (Vector2.Distance(pts[0], pos) <= reach)
                    strokes.RemoveAt(i);
                continue;
            }

            var hit = new bool[pts.Count - 1];
            var any = false;
            for (var j = 0; j < hit.Length; j++)
            {
                if (DistanceToSegment(pos, pts[j], pts[j + 1]) <= reach)
                {
                    hit[j] = true;
                    any = true;
                }
            }

            if (!any)
                continue;

            var pieces = new List<MarineMapStroke>();
            var current = new List<Vector2> { pts[0] };

            for (var j = 0; j < hit.Length; j++)
            {
                if (hit[j])
                {
                    Flush(pieces, current, stroke, pos, reach);
                    current = new List<Vector2> { pts[j + 1] };
                }
                else
                {
                    current.Add(pts[j + 1]);
                }
            }

            Flush(pieces, current, stroke, pos, reach);

            strokes.RemoveAt(i);
            strokes.InsertRange(i, pieces);
        }
    }

    private static void Flush(List<MarineMapStroke> pieces, List<Vector2> points, MarineMapStroke src, Vector2 pos, float reach)
    {
        if (points.Count >= 2 || (points.Count == 1 && Vector2.Distance(points[0], pos) > reach))
            pieces.Add(new MarineMapStroke(src.Color, src.Size, points));
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lenSq = ab.LengthSquared();
        if (lenSq < 0.0001f)
            return Vector2.Distance(p, a);

        var t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }
}
