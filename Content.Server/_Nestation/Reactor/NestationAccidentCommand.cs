// Nestation CC BY-NC 3.0
using Content.Server.Administration;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._Nestation.Reactor;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server._Nestation.Reactor;

[AdminCommand(AdminFlags.Fun)]
public sealed class NestationAccidentCommand : LocalizedEntityCommands
{
    [Dependency] private readonly ExplosionSystem _explosion = default!;

    public override string Command => "nestation_accident";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteLine("Сценарии: blackout, loca, blast [кг ТНТ], rods, turbine, steam, heat");
            return;
        }

        var query = EntityManager.EntityQueryEnumerator<NestationReactorCoreComponent>();

        if (!query.MoveNext(out var coreUid, out var core))
        {
            shell.WriteError("Реактор не найден.");
            return;
        }

        var plant = EntityManager.System<NestationPlantSystem>();
        var c = plant.BuildContext(coreUid);

        switch (args[0])
        {
            case "blackout":
                core.ProtectionsBypassed = true;
                core.AutoMode = true;
                core.Setpoint = 1f;

                if (c.Primary != null)
                    c.Primary.Setpoint = 0f;

                if (c.Gen != null)
                    c.Gen.Reserve = 0f;
                break;

            case "loca":
                core.ProtectionsBypassed = true;
                core.ForcedBreach = 0.5f;
                core.Inventory = core.InventoryNominal * 0.35f;
                core.ForcedJam = true;
                break;

            case "blast":
                var tnt = args.Length > 1 && float.TryParse(args[1], out var parsed) ? parsed : 30f;
                var (total, slope, max) = NestationPhysics.ToExplosion(tnt);
                _explosion.QueueExplosion(coreUid, ExplosionSystem.DefaultExplosionPrototypeId, total, slope, max);
                break;

            case "rods":
                core.ProtectionsBypassed = true;
                core.AutoMode = false;
                core.Scrammed = false;
                core.RodInsertion = 0f;
                core.RodTarget = 0f;
                break;

            case "turbine":
                core.ProtectionsBypassed = true;

                if (c.Gen != null)
                    c.Gen.BreakerClosed = false;
                break;

            case "steam":
                core.ProtectionsBypassed = true;

                if (c.Turbine != null)
                    c.Turbine.Tripped = true;

                if (c.Cond != null)
                    c.Cond.Temp = 130f;
                break;

            case "heat":
                core.FuelTemp = 1500f;
                core.ProtectionsBypassed = true;
                break;

            default:
                shell.WriteError("Неизвестный сценарий.");
                return;
        }

        shell.WriteLine("Сценарий запущен: " + args[0]);
    }
}
