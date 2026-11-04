// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Robust.Client.Graphics;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client.Explosion;

public sealed class ExplosionField
{
    public readonly Vector2i Origin;
    public readonly Vector2i Size;
    public readonly Vector2 Epicenter;
    public readonly Vector2 EpicenterTiles;
    public readonly float Speed;

    public readonly OwnedTexture Presence;
    public readonly OwnedTexture Heat;
    public readonly OwnedTexture Birth;

    public readonly Rgba32[] PresenceData;
    public readonly Rgba32[] HeatData;
    public readonly Rgba32[] BirthData;

    public readonly ShaderInstance Shader;

    public int Revealed = -1;

    public ExplosionField(
        IClyde clyde,
        ShaderInstance shader,
        Vector2i origin,
        Vector2i size,
        Vector2 epicenterTiles,
        float speed)
    {
        Origin = origin;
        Size = size;
        EpicenterTiles = epicenterTiles;
        Epicenter = new Vector2(epicenterTiles.X - origin.X, size.Y - (epicenterTiles.Y - origin.Y));
        Speed = speed;
        Shader = shader;

        var count = size.X * size.Y;
        PresenceData = new Rgba32[count];
        HeatData = new Rgba32[count];
        BirthData = new Rgba32[count];

        Presence = clyde.CreateBlankTexture<Rgba32>(size);
        Heat = clyde.CreateBlankTexture<Rgba32>(size);
        Birth = clyde.CreateBlankTexture<Rgba32>(size);

        Upload();
    }

    public int IndexOf(Vector2i tile)
    {
        var x = tile.X - Origin.X;
        var y = Size.Y - 1 - (tile.Y - Origin.Y);
        return y * Size.X + x;
    }

    public void Upload()
    {
        Presence.SetSubImage(Vector2i.Zero, Size, new ReadOnlySpan<Rgba32>(PresenceData));
        Heat.SetSubImage(Vector2i.Zero, Size, new ReadOnlySpan<Rgba32>(HeatData));
        Birth.SetSubImage(Vector2i.Zero, Size, new ReadOnlySpan<Rgba32>(BirthData));
    }

    public void Dispose()
    {
        (Presence as IDisposable)?.Dispose();
        (Heat as IDisposable)?.Dispose();
        (Birth as IDisposable)?.Dispose();
    }
}
