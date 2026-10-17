using Content.Shared.Light.Components;
using Robust.Shared.Map.Components;

namespace Content.Shared.Light.EntitySystems;

public abstract class SharedLightCycleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LightCycleComponent, MapInitEvent>(OnCycleMapInit);
        SubscribeLocalEvent<LightCycleComponent, ComponentShutdown>(OnCycleShutdown);
    }

    protected virtual void OnCycleMapInit(Entity<LightCycleComponent> ent, ref MapInitEvent args)
    {
        if (TryComp(ent.Owner, out MapLightComponent? mapLight))
        {
            ent.Comp.OriginalColor = mapLight.AmbientLightColor;
            Dirty(ent);
        }
    }

    private void OnCycleShutdown(Entity<LightCycleComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp(ent.Owner, out MapLightComponent? mapLight))
        {
            mapLight.AmbientLightColor = ent.Comp.OriginalColor;
            Dirty(ent.Owner, mapLight);
        }
    }

    public void SetOffset(Entity<LightCycleComponent> entity, TimeSpan offset)
    {
        entity.Comp.Offset = offset;
        var ev = new LightCycleOffsetEvent(offset);

        RaiseLocalEvent(entity, ref ev);
        Dirty(entity);
    }

    public static Color GetColor(Entity<LightCycleComponent> cycle, Color color, float time)
    {
        if (cycle.Comp.Enabled)
        {
            var lightLevel = CalculateLightLevel(cycle.Comp, time);
            var colorLevel = CalculateColorLevel(cycle.Comp, time);
            return new Color(
                (byte)Math.Min(255, color.RByte * colorLevel.R * lightLevel),
                (byte)Math.Min(255, color.GByte * colorLevel.G * lightLevel),
                (byte)Math.Min(255, color.BByte * colorLevel.B * lightLevel)
            );
        }

        return color;
    }

    /// <summary>
    /// How "daytime" it is at the given time: 0 = full night, 1 = full day, smoothly changing during dawn and dusk.
    /// The cycle repeats forever: night (middle of it at t=0) -> dawn -> day -> dusk -> night.
    /// With the default values: 20 min night, 5 min dawn, 20 min full day, 5 min dusk (50 min total).
    /// </summary>
    public static float CalculateDayFactor(LightCycleComponent comp, float time)
    {
        var total = MathF.Max(1f, (float) comp.Duration.TotalSeconds);
        var nightFrac = Math.Clamp((float) comp.NightDuration.TotalSeconds / total, 0f, 1f);
        var transFrac = Math.Clamp((float) comp.TransitionDuration.TotalSeconds / total, 0f, (1f - nightFrac) / 2f);

        // Position inside the cycle, mirrored so that dawn and dusk are symmetric:
        // 0 = middle of the night, 0.5 = middle of the day.
        var pos = time / total;
        pos -= MathF.Floor(pos);
        if (pos > 0.5f)
            pos = 1f - pos;

        var dawnStart = nightFrac / 2f;
        if (pos <= dawnStart)
            return 0f;

        if (transFrac <= 0f)
            return 1f;

        var t = (pos - dawnStart) / transFrac;
        if (t >= 1f)
            return 1f;

        return t * t * (3f - 2f * t); // smoothstep
    }

    /// <summary>
    /// Calculates light intensity as a function of time.
    /// </summary>
    public static double CalculateLightLevel(LightCycleComponent comp, float time)
    {
        var day = CalculateDayFactor(comp, time);
        var crest = MathF.Max(0f, comp.MaxLightLevel);
        var shift = MathF.Max(0f, comp.MinLightLevel);
        return Math.Min(comp.ClipLight, (crest - shift) * day + shift);
    }

    /// <summary>
    /// Each color channel follows the day factor differently, which "simulates" the atmosphere and creates the dawn and dusk tint:
    /// green lags behind red when the sun rises (so it is red/orange at dawn), and the blue component dips in the middle of
    /// dawn and dusk (warm color) while being at its maximum at night and at day. Maximums serve as clip thresholds.
    /// </summary>
    public static Color CalculateColorLevel(LightCycleComponent comp, float time)
    {
        var day = CalculateDayFactor(comp, time);
        var blueCurve = (2f * day - 1f) * (2f * day - 1f); // 1 at full night / full day, 0 in the middle of dawn / dusk

        var red = MathF.Min(comp.ClipLevel.R,
            MathF.Max(0f, comp.MinLevel.R) + (MathF.Max(0f, comp.MaxLevel.R) - MathF.Max(0f, comp.MinLevel.R)) * day);

        var green = MathF.Min(comp.ClipLevel.G,
            MathF.Max(0f, comp.MinLevel.G) + (MathF.Max(0f, comp.MaxLevel.G) - MathF.Max(0f, comp.MinLevel.G)) * day * day);

        var blue = MathF.Min(comp.ClipLevel.B,
            MathF.Max(0f, comp.MinLevel.B) + (MathF.Max(0f, comp.MaxLevel.B) - MathF.Max(0f, comp.MinLevel.B)) * blueCurve);

        return new Color(red, green, blue);
    }

    /// <summary>
    /// Generates a sinusoidal curve as a function of x (time). The other parameters serve to adjust the behavior of the curve.
    /// </summary>
    /// <param name="x"> It corresponds to the independent variable of the function, which in the context of this algorithm is the current time. </param>
    /// <param name="waveLength"> It's the wavelength of the function, it can be said to be the total duration of the light cycle. </param>
    /// <param name="crest"> It's the maximum point of the function, where it will have its greatest value. </param>
    /// <param name="shift"> It's the vertical displacement of the function, in practice it corresponds to the minimum value of the function. </param>
    /// <param name="exponent"> It is the exponent of the sine, serves to "flatten" the function close to its minimum points and make it "steeper" close to its maximum. </param>
    /// <param name="phase"> It changes the phase of the wave, like a "horizontal shift". It is important to transform the sinusoidal function into cosine, when necessary. </param>
    /// <returns> The result of the function. </returns>
    public static float CalculateCurve(float x,
        float waveLength,
        float crest,
        float shift,
        float exponent,
        float phase = 0)
    {
        var sen = MathF.Pow(MathF.Sin((MathF.PI * (phase + x)) / waveLength), exponent);
        return (crest - shift) * sen + shift;
    }
}

/// <summary>
/// Raised when the offset on <see cref="LightCycleComponent"/> changes.
/// </summary>
[ByRefEvent]
public record struct LightCycleOffsetEvent(TimeSpan Offset)
{
    public readonly TimeSpan Offset = Offset;
}
