// Nestation CC BY-NC 3.0
namespace Content.Server._Nestation.Reactor;

public static class NestationPhysics
{
    public const float TimeCompression = 20f;
    public const float WaterHeatCapacity = 4186f;
    public const float LatentHeat = 2_257_000f;
    public const float TntEnergy = 4_184_000f;
    public const float HydrogenHeat = 120_000_000f;
    public const float ZirconiumReactionHeat = 6_420_000f;
    public const float HydrogenPerZirconium = 0.0442f;
    public const float WaterPerZirconium = 0.395f;
    public const float HydrogenVolumePerKg = 11.1f;
    public const float FuelSpecificHeat = 300f;
    public const float SoundSpeed = 343f;

    public const float IntensityPerBar = 100f;
    public const float MaxTileIntensity = 120f;
    public const float MaxBlastRadius = 30f;
    public const float MaxTotalIntensity = 60_000f;

    public static float DecayHeatFraction(float seconds, float operatingSeconds)
    {
        var t = MathF.Max(seconds, 1f);
        return 0.0622f * (MathF.Pow(t, -0.2f) - MathF.Pow(t + operatingSeconds, -0.2f));
    }

    public static float EvaporationTime(float mass, float deltaT, float power)
    {
        if (power <= 0f)
            return -1f;

        return mass * (WaterHeatCapacity * MathF.Max(deltaT, 0f) + LatentHeat) / power;
    }

    public static float ZirconiumRate(float tempC)
    {
        var tk = MathF.Max(tempC + 273.15f, 1000f);
        return 5e-5f * MathF.Exp(22853f * (1f / 1473.15f - 1f / tk));
    }

    public static float HydrogenConcentration(float massKg, float hallVolume)
    {
        return hallVolume <= 0f ? 0f : massKg * HydrogenVolumePerKg / hallVolume;
    }

    public static float HydrogenEfficiency(float concentration)
    {
        if (concentration >= 0.18f)
            return 0.30f;

        return concentration >= 0.08f ? 0.12f : 0.04f;
    }

    public static float HydrogenTnt(float massKg, float concentration)
    {
        return HydrogenEfficiency(concentration) * massKg * HydrogenHeat / TntEnergy;
    }

    public static float SteamExplosionTnt(float corium, float tempC, float waterKg)
    {
        var efficiency = waterKg > corium * 0.5f ? 0.08f : 0.005f;
        return efficiency * corium * FuelSpecificHeat * MathF.Max(0f, tempC - 100f) / TntEnergy;
    }

    public static float VesselBurstTnt(float waterKg, float tempC, float pressureBar, float steamVolume)
    {
        var flash = 0.15f * waterKg * WaterHeatCapacity * MathF.Max(0f, tempC - 100f);
        var gas = MathF.Max(0f, pressureBar - 1f) * 1e5f * steamVolume / 0.3f;
        return (flash + gas) / TntEnergy;
    }

    public static float ExcursionTnt(float k, float beta, float heatCapacity, float alpha)
    {
        var rho = 1f - 1f / MathF.Max(k, 1.0001f);
        var energy = 2f * MathF.Max(0f, rho - beta) * heatCapacity / MathF.Max(alpha, 1e-6f);
        return 0.2f * energy / TntEnergy;
    }

    public static float RotorTnt(float inertia, float rpmFraction)
    {
        var omega = 2f * MathF.PI * 50f * rpmFraction;
        return 0.25f * 0.5f * inertia * omega * omega / TntEnergy;
    }

    public static float Overpressure(float tnt, float radius)
    {
        var r = MathF.Max(radius, 0.1f);
        var cbrt = MathF.Cbrt(MathF.Max(tnt, 0f));
        return 0.84f * cbrt / r + 2.7f * cbrt * cbrt / (r * r) + 7.0f * tnt / (r * r * r);
    }

    public static float RadiusAtPressure(float tnt, float bar)
    {
        var lo = 0.2f;
        var hi = 2000f;

        for (var i = 0; i < 40; i++)
        {
            var mid = 0.5f * (lo + hi);

            if (Overpressure(tnt, mid) > bar)
                lo = mid;
            else
                hi = mid;
        }

        return 0.5f * (lo + hi);
    }

    public static float SafeRadius(float tnt, float k = 15f)
    {
        return k * MathF.Cbrt(MathF.Max(tnt, 0f));
    }

    public static float SoundDistance(float seconds)
    {
        return SoundSpeed * seconds;
    }

    public static float SoundDistanceKm(float seconds)
    {
        return seconds / 3f;
    }

    public static (float Total, float Slope, float Max) ToExplosion(float tnt)
    {
        var radius = Math.Clamp(RadiusAtPressure(tnt, 0.05f), 3f, MaxBlastRadius);
        var total = 0f;

        for (var r = 1; r <= (int)radius; r++)
        {
            var intensity = MathF.Min(IntensityPerBar * Overpressure(tnt, r), MaxTileIntensity);
            total += 2f * MathF.PI * r * intensity;
        }

        var near = MathF.Min(IntensityPerBar * Overpressure(tnt, 1.5f), MaxTileIntensity);
        var far = IntensityPerBar * Overpressure(tnt, radius);
        var slope = MathF.Max(0.5f, (near - far) / MathF.Max(1f, radius - 1.5f));
        return (MathF.Min(total, MaxTotalIntensity), slope, near);
    }
}
