// Nestation Anarhey CC BY-NC 3.0
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client.Zones;

public sealed partial class ZoneBannerOverlay : Overlay
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IResourceCache _cache = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private const float FadeIn = 0.6f;
    private const float Hold = 2.4f;
    private const float FadeOut = 1.0f;
    private const float Total = FadeIn + Hold + FadeOut;

    private const float SlideDistance = 14f;
    private const float TopMargin = 36f;

    private readonly Font _font;

    private string? _text;
    private TimeSpan _start;

    public ZoneBannerOverlay()
    {
        IoCManager.InjectDependencies(this);

        _font = new VectorFont(_cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 20);
    }

    public void Show(string text)
    {
        var upper = text.ToUpperInvariant();

        if (_text == upper && (_timing.RealTime - _start).TotalSeconds < Total)
            return;

        _text = upper;
        _start = _timing.RealTime;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _text != null;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_text == null)
            return;

        var t = (float) (_timing.RealTime - _start).TotalSeconds;
        if (t >= Total)
        {
            _text = null;
            return;
        }

        float alpha;
        float slide;

        if (t < FadeIn)
        {
            var k = t / FadeIn;
            var eased = 1f - MathF.Pow(1f - k, 3f);
            alpha = eased;
            slide = (eased - 1f) * SlideDistance;
        }
        else if (t < FadeIn + Hold)
        {
            alpha = 1f;
            slide = 0f;
        }
        else
        {
            alpha = 1f - (t - FadeIn - Hold) / FadeOut;
            slide = 0f;
        }

        var scale = _cfg.GetCVar(CVars.DisplayUIScale);
        if (scale == 0f)
            scale = _ui.DefaultUIScale;

        var handle = args.ScreenHandle;
        var size = handle.GetDimensions(_font, _text, scale);
        var centerX = args.ViewportBounds.Left + args.ViewportBounds.Width / 2f;
        var y = args.ViewportBounds.Top + (TopMargin + slide) * scale;
        var pos = new Vector2(centerX - size.X / 2f, y);

        handle.DrawString(_font, pos + new Vector2(1f, 1f) * scale, _text, scale, Color.Black.WithAlpha(alpha * 0.6f));
        handle.DrawString(_font, pos, _text, scale, Color.White.WithAlpha(alpha));

        var lineWidth = size.X * 0.5f;
        var lineY = y + size.Y + 6f * scale;
        var line = new UIBox2(centerX - lineWidth / 2f, lineY, centerX + lineWidth / 2f, lineY + MathF.Max(1f, scale));
        handle.DrawRect(line, Color.White.WithAlpha(alpha * 0.6f));
    }
}
