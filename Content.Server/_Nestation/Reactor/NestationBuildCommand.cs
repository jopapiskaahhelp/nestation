// Nestation CC BY-NC 3.0
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._Nestation.Reactor;

[AdminCommand(AdminFlags.Mapping)]
public sealed partial class NestationBuildCommand : LocalizedEntityCommands
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;

    public override string Command => "nestation_build";

    private static readonly (string Proto, int X, int Y)[] Layout =
    {
        ("NestationReactorCore", 0, 0),
        ("NestationConsole", 0, 3),
        ("NestationPipeControl", 0, 2),
        ("NestationPipeCoolant", 2, 0),
        ("NestationPipeCoolant", 3, 0),
        ("NestationPipeCoolant", 3, 1),
        ("NestationPipeCoolant", 4, 1),
        ("NestationPipeCoolant", 5, 1),
        ("NestationSteamGenerator", 7, 0),
        ("NestationPipeCoolant", 5, -1),
        ("NestationPipeCoolant", 5, -2),
        ("NestationPipeCoolant", 5, -3),
        ("NestationPipeCoolant", 5, -4),
        ("NestationPipeCoolant", 4, -4),
        ("NestationPipeCoolant", 3, -4),
        ("NestationPipeCoolant", 2, -4),
        ("NestationPrimaryPump", 0, -4),
        ("NestationPipeCoolant", -2, -4),
        ("NestationPipeCoolant", -2, -3),
        ("NestationPipeCoolant", -2, -2),
        ("NestationPipeCoolant", -2, -1),
        ("NestationPipeCoolant", -2, 0),
        ("NestationPipeSteam", 7, 3),
        ("NestationTurbine", 9, 5),
        ("NestationGenerator", 15, 5),
        ("CableHV", 15, 5),
        ("NestationPipeSteam", 11, 3),
        ("NestationCondenser", 11, 1),
        ("NestationPipeFeed", 9, -1),
        ("NestationPipeFeed", 10, -1),
        ("NestationPipeFeed", 11, -1),
        ("NestationPipeFeed", 11, -2),
        ("NestationPipeFeed", 11, -3),
        ("NestationPipeFeed", 11, -4),
        ("NestationFeedPump", 9, -4),
        ("NestationPipeFeed", 7, -4),
        ("NestationPipeFeed", 7, -3),
        ("NestationPipeCooling", 14, 0),
        ("NestationCirculationPump", 16, 0),
        ("NestationPipeCooling", 14, 2),
        ("NestationPipeCooling", 15, 2),
        ("NestationPipeCooling", 16, 2),
        ("NestationPipeCooling", 17, 2),
    };

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player?.AttachedEntity is not { } player)
        {
            shell.WriteError("Нужен подконтрольный персонаж.");
            return;
        }

        var xform = EntityManager.GetComponent<TransformComponent>(player);

        if (xform.GridUid is not { } gridUid || !EntityManager.TryGetComponent(gridUid, out MapGridComponent? gridComp))
        {
            shell.WriteError("Встаньте на грид (станцию или платформу).");
            return;
        }

        var grid = new Entity<MapGridComponent>(gridUid, gridComp);
        var origin = _map.TileIndicesFor(grid, xform.Coordinates);
        var floor = new Tile(_tiles["FloorSteel"].TileId);

        for (var x = -7; x <= 25; x++)
        {
            for (var y = -8; y <= 9; y++)
            {
                var pos = origin + new Vector2i(x, y);

                if (_map.GetTileRef(grid, pos).Tile.IsEmpty)
                    _map.SetTile(grid, pos, floor);
            }
        }

        for (var x = 18; x <= 23; x++)
        {
            for (var y = -3; y <= 3; y++)
            {
                Spawn(grid, origin + new Vector2i(x, y), "NestationPoolSegment");
            }
        }

        EntityUid? core = null;

        foreach (var (proto, x, y) in Layout)
        {
            var uid = Spawn(grid, origin + new Vector2i(x, y), proto);

            if (proto == "NestationReactorCore")
                core = uid;
        }

        if (core != null)
        {
            var coords = _map.GridTileToLocal(grid.Owner, grid.Comp, origin);
            var fuel = EntityManager.SpawnAtPosition("NestationFuelAssembly", coords);
            _slots.TryInsert(core.Value, NestationPlantSystem.FuelSlot, fuel, null);
        }

        shell.WriteLine("Станция построена. Активная зона 3x3 в вашей клетке, пульт над ней, бассейн справа.");
    }

    private EntityUid Spawn(Entity<MapGridComponent> grid, Vector2i tile, string proto)
    {
        var uid = EntityManager.SpawnAtPosition(proto, _map.GridTileToLocal(grid.Owner, grid.Comp, tile));
        var transform = EntityManager.GetComponent<TransformComponent>(uid);

        if (!transform.Anchored)
            EntityManager.System<SharedTransformSystem>().AnchorEntity((uid, transform));

        return uid;
    }
}
