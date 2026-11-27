// Nestation New City CC BY-NC 3.0
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Nestation.Acv30;

[AdminCommand(AdminFlags.Fun)]
public sealed partial class Acv30Command : LocalizedEntityCommands
{
    public override string Command => "acv30";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteLine("acv30 spawn [группа] | acv30 detonate");
            return;
        }

        var system = EntityManager.System<Acv30System>();

        switch (args[0])
        {
            case "spawn":
                var spawned = system.SpawnAll(args.Length > 1 ? args[1] : null);
                shell.WriteLine($"Запущено машин: {spawned}");
                break;
            case "detonate":
                var detonated = system.DetonateAll();
                shell.WriteLine($"Подорвано машин: {detonated}");
                break;
            default:
                shell.WriteError("Неизвестная подкоманда. Доступно: spawn, detonate.");
                break;
        }
    }
}
