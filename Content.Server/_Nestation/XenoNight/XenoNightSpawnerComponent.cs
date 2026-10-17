// Nestation Anarhey CC BY-NC 3.0
using Robust.Shared.Prototypes;

namespace Content.Server._Nestation.XenoNight;

/// <summary>
/// Зона ночного спавна ксеноморфов. Пока на карте ночь, раз в <see cref="SpawnInterval"/>
/// спавнит одного случайного ксеноморфа в случайной свободной точке внутри <see cref="Radius"/>.
/// </summary>
[RegisterComponent]
public sealed partial class XenoNightSpawnerComponent : Component
{
    /// <summary>Радиус зоны в тайлах.</summary>
    [DataField]
    public float Radius = 8f;

    /// <summary>Интервал между спавнами.</summary>
    [DataField]
    public TimeSpan SpawnInterval = TimeSpan.FromSeconds(60);

    /// <summary>Максимум живых ксеноморфов, порождённых этой зоной (защита сервера от перегруза).</summary>
    [DataField]
    public int MaxAlive = 20;

    /// <summary>Что спавнить: прототип -> вес.</summary>
    [DataField]
    public Dictionary<EntProtoId, float> Spawns = new();

    [ViewVariables]
    public TimeSpan NextSpawn;

    [ViewVariables]
    public bool WasNight;

    [ViewVariables]
    public readonly HashSet<EntityUid> Alive = new();
}
