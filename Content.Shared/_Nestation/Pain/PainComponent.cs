// Nestation New City CC BY-NC 3.0
using Robust.Shared.GameStates;

namespace Content.Shared._Nestation.Pain;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class PainComponent : Component
{
    [DataField, AutoNetworkedField]
    public PainLevel Level;

    [DataField, AutoNetworkedField]
    public PainKind Dominant;

    [DataField, AutoNetworkedField]
    public float Intensity;

    [DataField, AutoNetworkedField]
    public bool Adrenaline;

    [DataField, AutoNetworkedField]
    public bool Shock;

    [DataField, AutoNetworkedField]
    public float SpeedModifier = 1f;

    [DataField, AutoNetworkedField]
    public float Muffle;

    [DataField, AutoNetworkedField]
    public byte WeakPulse;

    [ViewVariables]
    public Dictionary<PainKind, float> Channels = new();

    [ViewVariables]
    public float Accumulator;

    [ViewVariables]
    public float SecondAccumulator;

    [ViewVariables]
    public float BurnMomentum = 1f;

    [ViewVariables]
    public float BurnIdle = 99f;

    [ViewVariables]
    public float AdrenalinePoints;

    [ViewVariables]
    public int AdrenalineHits;

    [ViewVariables]
    public float AdrenalineWindow;

    [ViewVariables]
    public float AdrenalineTimer;

    [ViewVariables]
    public float AdrenalineCooldown;

    [ViewVariables]
    public float MoanTimer = 120f;

    [ViewVariables]
    public float WeakPulseTimer;

    [ViewVariables]
    public float FireGrace;

    [ViewVariables]
    public float[] DrugPresence = new float[3];

    [ViewVariables]
    public float[] DrugOnset = new float[3];

    [ViewVariables]
    public float TramadolTurnCooldown;

    [ViewVariables]
    public bool Depression;

    [ViewVariables]
    public float DepressionTime;

    [ViewVariables]
    public float DepressionClear;

    [ViewVariables]
    public bool Arrest;

    [ViewVariables]
    public float ConditionSpeed = 1f;

    [ViewVariables]
    public float ConditionStamina = 1f;

    [ViewVariables]
    public float NauseaTimer = 60f;

    [ViewVariables]
    public float BaseStaminaDecay = -1f;

    [ViewVariables]
    public float RawPain;

    [ViewVariables]
    public bool Numb;

    [ViewVariables]
    public bool ForcedSleep;

    [ViewVariables]
    public bool StaminaApplied;
}
