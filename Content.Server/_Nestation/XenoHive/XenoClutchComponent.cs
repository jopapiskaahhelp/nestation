// Nestation Anarhey CC BY-NC 3.0
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
}

[RegisterComponent]
public sealed partial class XenoClutchComponent : Component
{
    [DataField]
    public EntProtoId QueenProto = "MobXenoQueen";

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
}
