// Nestation new city CC BY-NC 3.0
using Robust.Shared.Prototypes;

namespace Content.Server._Nestation.XenoHive;

[DataDefinition]
public sealed partial class XenoHiveEntry
{
    [DataField(required: true)]
    public EntProtoId Proto;

    [DataField(required: true)]
    public TimeSpan Delay;

    [DataField]
    public int Class = 1;

    [DataField]
    public TimeSpan Maturation = TimeSpan.FromSeconds(120);

    [DataField]
    public TimeSpan MaturationMax;

    [DataField]
    public bool NoScale;
}

[RegisterComponent]
public sealed partial class XenoClutchComponent : Component
{
    [DataField]
    public EntProtoId QueenProto = "MobXenoHiveQueen";

    [DataField]
    public EntProtoId EggProto = "XenoEgg";

    [DataField]
    public EntProtoId WallProto = "WallSolidChitin";

    [DataField]
    public EntProtoId ReinforcedProto = "WallReinforcedChitin";

    [DataField]
    public EntProtoId DoorProto = "AirlockXeno";

    [DataField]
    public List<XenoHiveEntry> Opening = new();

    [DataField]
    public List<XenoHiveEntry> Cycle = new();

    [DataField]
    public TimeSpan BuildInterval = TimeSpan.FromSeconds(4);

    [DataField]
    public TimeSpan ReinforcedBuildInterval = TimeSpan.FromSeconds(6);

    [DataField]
    public int MaxRadius = 34;

    [DataField]
    public bool SchemaEnabled = true;

    [DataField]
    public TimeSpan SchemaInterval = TimeSpan.FromSeconds(300);

    [DataField]
    public TimeSpan SchemaFirstDelay = TimeSpan.FromSeconds(60);

    [DataField]
    public int SchemaMaxSectors = 3;

    [DataField]
    public int SchemaMaxEntries = 160;

    [DataField]
    public int InitialBurrowers;

    [DataField]
    public EntProtoId? BurrowerProto;

    [DataField]
    public TimeSpan InitialBurrowerSpacing = TimeSpan.FromSeconds(4);

    [DataField]
    public float SpawnDelayScale = 1f;

    [DataField]
    public TimeSpan HeadStart;

    [DataField]
    public float HeadStartBuildScale = 0.5f;

    [DataField]
    public int SchemaFrontSector = -1;

    [DataField]
    public float SchemaFrontThreat;

    [DataField]
    public List<string> NoBuildTiles = new();

    [DataField]
    public TimeSpan MendInterval = TimeSpan.FromSeconds(1.5);

    [ViewVariables]
    public bool Started;

    [ViewVariables]
    public TimeSpan Start;

    [ViewVariables]
    public EntityUid? Queen;

    [ViewVariables]
    public int Cursor;

    [ViewVariables]
    public TimeSpan LastSpawnAt;

    [ViewVariables]
    public bool HasNext;

    [ViewVariables]
    public TimeSpan NextSpawnAt;

    [ViewVariables]
    public TimeSpan NextLayAt;

    [ViewVariables]
    public int Stage = 1;

    public XenoMazePlan? Plan;
    public TimeSpan NextRescan;
    public TimeSpan NextFix;
    public TimeSpan NextAudit;
    public TimeSpan NextSchemaAt;
}
