using System;

namespace Content.Client.RealisticLook;

public sealed class RealisticLookSettings
{
    public float Exposure = 1.0f;
    public float Contrast = 1.0f;
    public float Saturation = 1.0f;
    public float ShadowDesat = 0.0f;
    public float BlackCrush = 0.0f;
    public float Tonemap = 0.5f;

    public float ShadowR = 1f, ShadowG = 1f, ShadowB = 1f;
    public float HighlightR = 1f, HighlightG = 1f, HighlightB = 1f;

    public float Vignette = 0.5f;
    public float VignetteRadius = 0.4f;
    public float VignetteSoftness = 0.7f;

    public float Grain = 0.03f;
    public float GrainSize = 1.5f;
    public float Aberration = 0.004f;

    public bool BloomEnabled = true;
    public float Bloom = 0.4f;
    public float BloomThreshold = 0.5f;
    public float BloomRadius = 8f;
    public float BloomR = 1f, BloomG = 0.9f, BloomB = 0.75f;

    public bool FogEnabled = true;
    public float Fog = 0.2f;
    public float FogScale = 0.12f;
    public float FogSpeed = 0.03f;
    public float FogWindX = 1f, FogWindY = 0.3f;
    public float FogLit = 0.8f;
    public float FogAmbient = 0.03f;
    public float FogR = 0.5f, FogG = 0.58f, FogB = 0.55f;

    public static readonly string[] Keys =
    {
        "exposure", "contrast", "saturation", "shadowdesat", "blackcrush", "tonemap",
        "vignette", "vignetteradius", "vignettesoft",
        "grain", "grainsize", "aberration",
        "bloom", "bloomthreshold", "bloomradius",
        "fog", "fogscale", "fogspeed", "foglit", "fogambient",
    };

    public bool TrySet(string key, float v)
    {
        switch (key.ToLowerInvariant())
        {
            case "exposure": Exposure = v; return true;
            case "contrast": Contrast = v; return true;
            case "saturation": Saturation = v; return true;
            case "shadowdesat": ShadowDesat = v; return true;
            case "blackcrush": BlackCrush = v; return true;
            case "tonemap": Tonemap = v; return true;
            case "vignette": Vignette = v; return true;
            case "vignetteradius": VignetteRadius = v; return true;
            case "vignettesoft": VignetteSoftness = v; return true;
            case "grain": Grain = v; return true;
            case "grainsize": GrainSize = v; return true;
            case "aberration": Aberration = v; return true;
            case "bloom": Bloom = v; return true;
            case "bloomthreshold": BloomThreshold = v; return true;
            case "bloomradius": BloomRadius = v; return true;
            case "fog": Fog = v; return true;
            case "fogscale": FogScale = v; return true;
            case "fogspeed": FogSpeed = v; return true;
            case "foglit": FogLit = v; return true;
            case "fogambient": FogAmbient = v; return true;
            default: return false;
        }
    }
}

public static class RealisticLookPresets
{
    public static readonly string[] Names = { "horror", "cinematic", "noir", "clean" };

    public static RealisticLookSettings? Get(string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "horror":
                return new RealisticLookSettings
                {
                    Exposure = 1.05f, Contrast = 1.18f, Saturation = 0.82f,
                    ShadowDesat = 0.45f, BlackCrush = 0.015f, Tonemap = 0.55f,
                    ShadowR = 0.78f, ShadowG = 0.90f, ShadowB = 1.10f,
                    HighlightR = 1.12f, HighlightG = 1.0f, HighlightB = 0.82f,
                    Vignette = 0.85f, VignetteRadius = 0.30f, VignetteSoftness = 0.75f,
                    Grain = 0.045f, GrainSize = 1.4f, Aberration = 0.006f,
                    BloomEnabled = true, Bloom = 0.55f, BloomThreshold = 0.45f, BloomRadius = 10f,
                    BloomR = 1.0f, BloomG = 0.82f, BloomB = 0.60f,
                    FogEnabled = true, Fog = 0.30f, FogScale = 0.12f, FogSpeed = 0.03f,
                    FogWindX = 1.0f, FogWindY = 0.3f, FogLit = 0.8f, FogAmbient = 0.025f,
                    FogR = 0.50f, FogG = 0.58f, FogB = 0.55f,
                };

            case "cinematic":
                return new RealisticLookSettings
                {
                    Exposure = 1.10f, Contrast = 1.10f, Saturation = 0.95f,
                    ShadowDesat = 0.15f, BlackCrush = 0.0f, Tonemap = 0.5f,
                    ShadowR = 0.92f, ShadowG = 0.97f, ShadowB = 1.05f,
                    HighlightR = 1.06f, HighlightG = 1.0f, HighlightB = 0.92f,
                    Vignette = 0.45f, VignetteRadius = 0.45f, VignetteSoftness = 0.8f,
                    Grain = 0.025f, GrainSize = 1.4f, Aberration = 0.003f,
                    BloomEnabled = true, Bloom = 0.35f, BloomThreshold = 0.55f, BloomRadius = 8f,
                    BloomR = 1.0f, BloomG = 0.9f, BloomB = 0.75f,
                    FogEnabled = true, Fog = 0.10f, FogScale = 0.10f, FogSpeed = 0.025f,
                    FogWindX = 1.0f, FogWindY = 0.2f, FogLit = 0.7f, FogAmbient = 0.03f,
                    FogR = 0.65f, FogG = 0.70f, FogB = 0.75f,
                };

            case "noir":
                return new RealisticLookSettings
                {
                    Exposure = 1.05f, Contrast = 1.30f, Saturation = 0.05f,
                    ShadowDesat = 0.0f, BlackCrush = 0.02f, Tonemap = 0.4f,
                    Vignette = 0.8f, VignetteRadius = 0.32f, VignetteSoftness = 0.7f,
                    Grain = 0.06f, GrainSize = 1.6f, Aberration = 0.002f,
                    BloomEnabled = true, Bloom = 0.40f, BloomThreshold = 0.50f, BloomRadius = 9f,
                    BloomR = 1.0f, BloomG = 1.0f, BloomB = 1.0f,
                    FogEnabled = true, Fog = 0.15f, FogScale = 0.11f, FogSpeed = 0.025f,
                    FogWindX = 1.0f, FogWindY = 0.25f, FogLit = 0.8f, FogAmbient = 0.03f,
                    FogR = 0.6f, FogG = 0.6f, FogB = 0.6f,
                };

            case "clean":
                return new RealisticLookSettings
                {
                    Exposure = 1.0f, Contrast = 1.05f, Saturation = 1.0f,
                    ShadowDesat = 0.0f, BlackCrush = 0.0f, Tonemap = 0.25f,
                    Vignette = 0.25f, VignetteRadius = 0.5f, VignetteSoftness = 0.8f,
                    Grain = 0.015f, GrainSize = 1.4f, Aberration = 0.0f,
                    BloomEnabled = true, Bloom = 0.20f, BloomThreshold = 0.6f, BloomRadius = 7f,
                    BloomR = 1.0f, BloomG = 0.95f, BloomB = 0.85f,
                    FogEnabled = false,
                };

            default:
                return null;
        }
    }
}
