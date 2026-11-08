// Nestation New City CC BY-NC 3.0
using System.Numerics;

namespace Content.Shared.Explosion;

public static class BlastBurn
{
    private const float PowerReference = 1900f;
    private const int CellSize = 4;
    private const float CellChance = 0.2f;

    public static float Hash(Vector2i tile, int seed)
    {
        unchecked
        {
            var h = (uint) (tile.X * 73856093) ^ (uint) (tile.Y * 19349663) ^ (uint) (seed * 83492791);
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (h & 0xFFFFFFu) / 16777216f;
        }
    }

    public static bool IsBurning(Vector2i tile, int seed, float fraction)
    {
        var density = 0.35f + 0.65f * (1f - Math.Clamp(fraction, 0f, 1f));
        if (Hash(tile, seed + 9) >= density)
            return false;

        var cellX = (int) MathF.Floor(tile.X / (float) CellSize);
        var cellY = (int) MathF.Floor(tile.Y / (float) CellSize);
        var center = new Vector2(tile.X + 0.5f, tile.Y + 0.5f);

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var cell = new Vector2i(cellX + dx, cellY + dy);
                if (Hash(cell, seed) >= CellChance)
                    continue;

                var position = new Vector2(
                    cell.X * CellSize + Hash(cell, seed + 1) * CellSize,
                    cell.Y * CellSize + Hash(cell, seed + 2) * CellSize);
                var radius = 0.9f + 1.1f * Hash(cell, seed + 3);

                if (Vector2.DistanceSquared(center, position) < radius * radius)
                    return true;
            }
        }

        return false;
    }

    public static float LingerSeconds(float power)
    {
        return 5f + 5f * Math.Clamp(MathF.Sqrt(power / PowerReference), 0f, 1f);
    }
}
