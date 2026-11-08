using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.RealisticLook;

public abstract partial class RealisticLookOverlayBase : Overlay
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IEyeManager _eye = default!;

    protected readonly RealisticLookSystem Sys;
    protected readonly ShaderInstance Shader;

    public override bool RequestScreenTexture => true;

    protected RealisticLookOverlayBase(RealisticLookSystem sys, string shaderId)
    {
        IoCManager.InjectDependencies(this);
        Sys = sys;
        Shader = _proto.Index<ShaderPrototype>(shaderId).InstanceUnique();
    }

    protected abstract bool IsActive { get; }

    protected abstract void SetParameters(in OverlayDrawArgs args);

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!Sys.Enabled || !IsActive)
            return false;

        if (_player.LocalEntity == null || args.Viewport.Eye != _eye.CurrentEye)
            return false;

        return base.BeforeDraw(in args);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        Shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        SetParameters(in args);

        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(Shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}

public sealed class RealisticFogOverlay : RealisticLookOverlayBase
{
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public RealisticFogOverlay(RealisticLookSystem sys) : base(sys, "RealisticLookFog")
    {
        ZIndex = 0;
    }

    protected override bool IsActive => Sys.Settings.FogEnabled;

    protected override void SetParameters(in OverlayDrawArgs args)
    {
        var s = Sys.Settings;
        var aabb = args.WorldAABB;

        Shader.SetParameter("viewOriginX", aabb.Left);
        Shader.SetParameter("viewOriginY", aabb.Bottom);
        Shader.SetParameter("viewSizeX", aabb.Width);
        Shader.SetParameter("viewSizeY", aabb.Height);

        Shader.SetParameter("fogDensity", s.Fog);
        Shader.SetParameter("fogScale", s.FogScale);
        Shader.SetParameter("fogSpeed", s.FogSpeed);
        Shader.SetParameter("fogWindX", s.FogWindX);
        Shader.SetParameter("fogWindY", s.FogWindY);
        Shader.SetParameter("fogLit", s.FogLit);
        Shader.SetParameter("fogAmbient", s.FogAmbient);
        Shader.SetParameter("fogR", s.FogR);
        Shader.SetParameter("fogG", s.FogG);
        Shader.SetParameter("fogB", s.FogB);
    }
}

public sealed class RealisticBloomOverlay : RealisticLookOverlayBase
{
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public RealisticBloomOverlay(RealisticLookSystem sys) : base(sys, "RealisticLookBloom")
    {
        ZIndex = 10;
    }

    protected override bool IsActive => Sys.Settings.BloomEnabled;

    protected override void SetParameters(in OverlayDrawArgs args)
    {
        var s = Sys.Settings;
        Shader.SetParameter("bloomStrength", s.Bloom);
        Shader.SetParameter("bloomThreshold", s.BloomThreshold);
        Shader.SetParameter("bloomRadius", s.BloomRadius);
        Shader.SetParameter("bloomR", s.BloomR);
        Shader.SetParameter("bloomG", s.BloomG);
        Shader.SetParameter("bloomB", s.BloomB);
    }
}

public sealed class RealisticGradeOverlay : RealisticLookOverlayBase
{
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public RealisticGradeOverlay(RealisticLookSystem sys) : base(sys, "RealisticLookGrade")
    {
        ZIndex = 20;
    }

    protected override bool IsActive => true;

    protected override void SetParameters(in OverlayDrawArgs args)
    {
        var s = Sys.Settings;
        Shader.SetParameter("exposure", s.Exposure);
        Shader.SetParameter("contrast", s.Contrast);
        Shader.SetParameter("saturation", s.Saturation);
        Shader.SetParameter("shadowDesat", s.ShadowDesat);
        Shader.SetParameter("blackCrush", s.BlackCrush);
        Shader.SetParameter("tonemapAmount", s.Tonemap);

        Shader.SetParameter("shadowR", s.ShadowR);
        Shader.SetParameter("shadowG", s.ShadowG);
        Shader.SetParameter("shadowB", s.ShadowB);
        Shader.SetParameter("highlightR", s.HighlightR);
        Shader.SetParameter("highlightG", s.HighlightG);
        Shader.SetParameter("highlightB", s.HighlightB);

        Shader.SetParameter("vignetteStrength", s.Vignette);
        Shader.SetParameter("vignetteRadius", s.VignetteRadius);
        Shader.SetParameter("vignetteSoftness", s.VignetteSoftness);

        Shader.SetParameter("grainStrength", s.Grain);
        Shader.SetParameter("grainSize", s.GrainSize);
        Shader.SetParameter("aberration", s.Aberration);
    }
}
