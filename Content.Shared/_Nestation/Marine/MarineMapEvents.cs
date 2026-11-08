// Nestation new city CC BY-NC 3.0
using System.Numerics;
using Content.Shared.Actions;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

namespace Content.Shared._NeStation.Marine;

public sealed partial class OpenMarineMapEvent : InstantActionEvent
{
}

public static class MarineMapTile
{
    public const byte Empty = 0;
    public const byte Floor = 1;
    public const byte Wall = 2;
}

public static class MarineMapPalette
{
    public static readonly Color[] Colors =
    {
        Color.FromHex("#101010"),
        Color.FromHex("#e03131"),
        Color.FromHex("#9c36b5"),
        Color.FromHex("#ffffff"),
    };

    public const int MaxStrokes = 1500;
    public const int MaxPointsPerStroke = 400;
    public const float MinPenSize = 0.5f;
    public const float MaxPenSize = 6f;
    public const float MinEraserSize = 1f;
    public const float MaxEraserSize = 12f;
}

[Serializable, NetSerializable]
public sealed class MarineMapLayout
{
    public Vector2i Origin;
    public int Width;
    public int Height;
    public byte[] Tiles = Array.Empty<byte>();

    public bool SameAs(MarineMapLayout other)
    {
        return Origin == other.Origin
               && Width == other.Width
               && Height == other.Height
               && Tiles.AsSpan().SequenceEqual(other.Tiles);
    }
}

[Serializable, NetSerializable]
public sealed class MarineMapStroke
{
    public byte Color;
    public float Size;
    public List<Vector2> Points = new();

    public MarineMapStroke()
    {
    }

    public MarineMapStroke(byte color, float size, List<Vector2> points)
    {
        Color = color;
        Size = size;
        Points = points;
    }
}

[Serializable, NetSerializable]
public sealed class MarineBlip
{
    public NetEntity Entity;
    public Vector2 Pos;

    public MarineBlip()
    {
    }

    public MarineBlip(NetEntity entity, Vector2 pos)
    {
        Entity = entity;
        Pos = pos;
    }
}

[Serializable, NetSerializable]
public sealed class MarineMapOpenEvent : EntityEventArgs
{
    public MarineMapLayout Layout;
    public List<MarineMapStroke> Strokes;
    public bool CanDraw;

    public MarineMapOpenEvent(MarineMapLayout layout, List<MarineMapStroke> strokes, bool canDraw)
    {
        Layout = layout;
        Strokes = strokes;
        CanDraw = canDraw;
    }
}

[Serializable, NetSerializable]
public sealed class MarineMapLayoutEvent : EntityEventArgs
{
    public MarineMapLayout Layout;

    public MarineMapLayoutEvent(MarineMapLayout layout)
    {
        Layout = layout;
    }
}

[Serializable, NetSerializable]
public sealed class MarineMapUpdateEvent : EntityEventArgs
{
    public List<MarineBlip> Blips;
    public bool CanDraw;

    public MarineMapUpdateEvent(List<MarineBlip> blips, bool canDraw)
    {
        Blips = blips;
        CanDraw = canDraw;
    }
}

[Serializable, NetSerializable]
public sealed class MarineMapCloseEvent : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class MarineMapClosedEvent : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class MarineMapStrokeEvent : EntityEventArgs
{
    public MarineMapStroke Stroke;

    public MarineMapStrokeEvent(MarineMapStroke stroke)
    {
        Stroke = stroke;
    }
}

[Serializable, NetSerializable]
public sealed class MarineMapEraseEvent : EntityEventArgs
{
    public Vector2 Pos;
    public float Radius;

    public MarineMapEraseEvent(Vector2 pos, float radius)
    {
        Pos = pos;
        Radius = radius;
    }
}
