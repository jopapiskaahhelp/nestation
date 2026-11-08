// Nestation New City CC BY-NC 3.0
using Robust.Shared.Graphics;

namespace Content.Client.Explosion;

[RegisterComponent]
public sealed partial class ExplosionVisualsTexturesComponent : Component
{
    public EntityUid LightEntity;

    public Color? FireColor;

    public bool Started;

    public TimeSpan StartTime;

    public float Seed;

    public float Power = 1f;

    public float MaxIntensity = 1f;

    public float Total;

    public int BurnSeed;

    public Dictionary<EntityUid, ExplosionField?> Fields = new();

    public ExplosionField? SpaceField;

    public bool SpaceFieldBuilt;

    public void DisposeFields()
    {
        foreach (var field in Fields.Values)
        {
            field?.Dispose();
        }

        Fields.Clear();
        SpaceField?.Dispose();
        SpaceField = null;
    }
}
