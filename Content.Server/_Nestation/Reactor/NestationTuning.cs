// Nestation CC BY-NC 3.0
namespace Content.Server._Nestation.Reactor;

public static class NestationTuning
{
    public const float Ambient = 20f;
    public const float DecayFraction = 0.065f;
    public const float DecayTime = 1500f;
    public const float NeutronTime = 6f;
    public const float PromptTime = 0.7f;
    public const float ScramTime = 2f;
    public const float RodSpeed = 0.02f;
    public const float ScramRodSpeed = 0.2f;
    public const float RodGain = 0.1f;
    public const float SteamController = 12f;
    public const float SteamFlowNominal = 3_000_000f;

    public const float ScramFuelTemp = 850f;
    public const float ScramPower = 1.15f;
    public const float ScramFlow = 0.3f;
    public const float ScramPressure = 60f;
    public const float ScramCoolantTemp = 330f;
    public const float ScramSgLevel = 0.2f;
    public const float ScramCondenserTemp = 115f;
    public const float TripRpm = 1.10f;
    public const float TripSgLevel = 0.92f;
    public const float ExplodeRpm = 1.25f;
    public const float ExcursionPower = 2.0f;

    public static float SaturationPressure(float tempC)
    {
        return MathF.Exp(11.876f - 3967f / (MathF.Max(tempC, 0f) + 234.4f));
    }

    public static float MoveToward(float value, float target, float maxDelta)
    {
        if (MathF.Abs(target - value) <= maxDelta)
            return target;

        return value + MathF.Sign(target - value) * maxDelta;
    }
}
