// Nestation new city CC BY-NC 3.0
using System.Numerics;
using Content.Shared._NeStation.Marine;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.Client._NeStation.Marine;

public enum MarineMapTool
{
    Pen,
    Eraser,
}

public sealed class MarineMapControl : Control
{
    private static readonly Color Background = Color.FromHex("#2a2e37");
    private static readonly Color BlipColor = Color.FromHex("#2f7bff");

    private static readonly Color SpaceColor = Color.FromHex("#4a505c");
    private static readonly Color FloorColor = Color.FromHex("#6f7785");
    private static readonly Color WallColor = Color.FromHex("#c8cfda");

    private MarineMapLayout? _layout;
    private readonly List<(int X, int Y, int Len, byte Type)> _runs = new();
    private List<MarineMapStroke> _strokes = new();
    private List<MarineBlip> _blips = new();
    private NetEntity _self;

    private MarineMapStroke? _current;
    private bool _erasing;
    private Vector2 _lastErase;
    private Vector2 _mouse;
    private bool _hasMouse;

    private Vector2 _offset;
    private float _scale = 1f;

    public bool CanDraw;
    public MarineMapTool Tool = MarineMapTool.Pen;
    public byte PenColor;
    public float PenSize = 1.5f;
    public float EraserSize = 3f;

    public event Action<MarineMapStroke>? OnStroke;
    public event Action<Vector2, float>? OnErase;

    public MarineMapControl()
    {
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;
        MinSize = new Vector2(440, 440);
        HorizontalExpand = true;
        VerticalExpand = true;
    }

    public void SetLayout(MarineMapLayout layout)
    {
        _layout = layout;
        _runs.Clear();

        for (var y = 0; y < layout.Height; y++)
        {
            var x = 0;
            while (x < layout.Width)
            {
                var type = layout.Tiles[y * layout.Width + x];
                if (type == MarineMapTile.Empty)
                {
                    x++;
                    continue;
                }

                var start = x;
                while (x < layout.Width && layout.Tiles[y * layout.Width + x] == type)
                    x++;

                _runs.Add((start, y, x - start, type));
            }
        }
    }

    public void SetStrokes(List<MarineMapStroke> strokes) => _strokes = strokes;

    public void SetBlips(List<MarineBlip> blips, NetEntity self)
    {
        _blips = blips;
        _self = self;
    }

    public void AddRemoteStroke(MarineMapStroke stroke)
    {
        _strokes.Add(stroke);
        while (_strokes.Count > MarineMapPalette.MaxStrokes)
            _strokes.RemoveAt(0);
    }

    public void EraseRemote(Vector2 pos, float radius) => MarineMapEraser.Erase(_strokes, pos, radius);

    private bool UpdateTransform()
    {
        if (_layout == null || _layout.Width <= 0 || _layout.Height <= 0)
            return false;

        var size = PixelSize;
        _scale = MathF.Min(size.X / (float) _layout.Width, size.Y / (float) _layout.Height);
        _offset = new Vector2(
            (size.X - _layout.Width * _scale) / 2f,
            (size.Y - _layout.Height * _scale) / 2f);
        return _scale > 0f;
    }

    private Vector2 TileToScreen(Vector2 t)
    {
        return _offset + new Vector2(
            (t.X - _layout!.Origin.X) * _scale,
            (_layout.Origin.Y + _layout.Height - t.Y) * _scale);
    }

    private Vector2 ScreenToTile(Vector2 s)
    {
        return new Vector2(
            _layout!.Origin.X + (s.X - _offset.X) / _scale,
            _layout.Origin.Y + _layout.Height - (s.Y - _offset.Y) / _scale);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        handle.DrawRect(UIBox2.FromDimensions(Vector2.Zero, PixelSize), Background);

        if (!UpdateTransform())
            return;

        handle.DrawRect(
            UIBox2.FromDimensions(_offset, new Vector2(_layout!.Width * _scale, _layout.Height * _scale)),
            SpaceColor);

        foreach (var (rx, ry, len, type) in _runs)
        {
            var left = MathF.Floor(_offset.X + rx * _scale);
            var right = MathF.Ceiling(_offset.X + (rx + len) * _scale);
            var bottom = MathF.Ceiling(_offset.Y + (_layout.Height - ry) * _scale);
            var top = MathF.Floor(_offset.Y + (_layout.Height - ry - 1) * _scale);

            handle.DrawRect(new UIBox2(left, top, right, bottom), type == MarineMapTile.Wall ? WallColor : FloorColor);
        }

        foreach (var stroke in _strokes)
            DrawStroke(handle, stroke);

        if (_current != null)
            DrawStroke(handle, _current);

        var blipRadius = MathF.Max(3f, _scale * 0.9f);
        foreach (var blip in _blips)
        {
            var pos = TileToScreen(blip.Pos);
            if (blip.Entity == _self)
                handle.DrawCircle(pos, blipRadius + 2f, Color.White);

            handle.DrawCircle(pos, blipRadius, BlipColor);
        }

        if (CanDraw && Tool == MarineMapTool.Eraser && _hasMouse)
            handle.DrawCircle(_mouse, EraserSize * 0.5f * _scale, Color.White, false);
    }

    private void DrawStroke(DrawingHandleScreen handle, MarineMapStroke stroke)
    {
        var color = MarineMapPalette.Colors[Math.Min(stroke.Color, (byte) (MarineMapPalette.Colors.Length - 1))];
        var radius = MathF.Max(1f, stroke.Size * _scale * 0.5f);
        var step = MathF.Max(1f, radius * 0.6f);

        Vector2? prev = null;
        foreach (var p in stroke.Points)
        {
            var s = TileToScreen(p);
            if (prev is { } a)
            {
                var n = Math.Min((int) (Vector2.Distance(a, s) / step), 64);
                for (var k = 1; k < n; k++)
                    handle.DrawCircle(Vector2.Lerp(a, s, k / (float) n), radius, color);
            }

            handle.DrawCircle(s, radius, color);
            prev = s;
        }
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick || !CanDraw || _layout == null || !UpdateTransform())
            return;

        args.Handle();
        var tile = ScreenToTile(args.RelativePixelPosition);

        if (Tool == MarineMapTool.Eraser)
        {
            _erasing = true;
            EraseAt(tile);
        }
        else
        {
            _current = new MarineMapStroke(PenColor, PenSize, new List<Vector2> { tile });
        }
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        _mouse = args.RelativePixelPosition;
        _hasMouse = true;

        if (_layout == null || !UpdateTransform())
            return;

        var tile = ScreenToTile(_mouse);

        if (_erasing)
        {
            if (Vector2.Distance(tile, _lastErase) >= MathF.Max(0.25f, EraserSize * 0.2f))
                EraseAt(tile);
        }
        else if (_current != null)
        {
            if (Vector2.Distance(_current.Points[^1], tile) < 0.3f)
                return;

            _current.Points.Add(tile);

            if (_current.Points.Count >= MarineMapPalette.MaxPointsPerStroke)
            {
                var last = _current.Points[^1];
                FinishStroke();
                _current = new MarineMapStroke(PenColor, PenSize, new List<Vector2> { last });
            }
        }
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        _erasing = false;
        FinishStroke();
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hasMouse = false;
    }

    private void FinishStroke()
    {
        if (_current == null)
            return;

        var stroke = _current;
        _current = null;

        _strokes.Add(stroke);
        while (_strokes.Count > MarineMapPalette.MaxStrokes)
            _strokes.RemoveAt(0);

        OnStroke?.Invoke(stroke);
    }

    private void EraseAt(Vector2 tile)
    {
        _lastErase = tile;
        var radius = EraserSize * 0.5f;
        MarineMapEraser.Erase(_strokes, tile, radius);
        OnErase?.Invoke(tile, radius);
    }
}
