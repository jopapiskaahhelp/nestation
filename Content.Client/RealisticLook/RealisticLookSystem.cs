using System;
using System.Globalization;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Client.RealisticLook;

public sealed partial class RealisticLookSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IConsoleHost _console = default!;

    private RealisticFogOverlay? _fog;
    private RealisticBloomOverlay? _bloom;
    private RealisticGradeOverlay? _grade;

    public RealisticLookSettings Settings { get; private set; } = RealisticLookPresets.Get("horror")!;
    public string PresetName { get; private set; } = "horror";
    public bool Enabled { get; private set; } = true;

    public override void Initialize()
    {
        base.Initialize();

        Enabled = _cfg.GetCVar(RealisticLookCVars.Enabled);
        var saved = _cfg.GetCVar(RealisticLookCVars.Preset);
        var preset = RealisticLookPresets.Get(saved);
        if (preset != null)
        {
            Settings = preset;
            PresetName = saved;
        }

        _fog = new RealisticFogOverlay(this);
        _bloom = new RealisticBloomOverlay(this);
        _grade = new RealisticGradeOverlay(this);
        _overlays.AddOverlay(_fog);
        _overlays.AddOverlay(_bloom);
        _overlays.AddOverlay(_grade);

        _console.RegisterCommand(
            "rl",
            "Управление реалистичной графикой (туман, bloom, грейдинг).",
            "rl [on|off|toggle|preset <имя>|set <параметр> <значение>|bloom on|off|fog on|off|list]",
            OnCommand);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _console.UnregisterCommand("rl");

        if (_fog != null) _overlays.RemoveOverlay(_fog);
        if (_bloom != null) _overlays.RemoveOverlay(_bloom);
        if (_grade != null) _overlays.RemoveOverlay(_grade);
    }

    private void SetEnabled(bool value)
    {
        Enabled = value;
        _cfg.SetCVar(RealisticLookCVars.Enabled, value);
    }

    private void OnCommand(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine($"rl: {(Enabled ? "включено" : "выключено")}, пресет: {PresetName}");
            shell.WriteLine("Использование: rl on | off | toggle | preset <имя> | set <параметр> <значение> | bloom on|off | fog on|off | list");
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "on":
                SetEnabled(true);
                shell.WriteLine("rl: включено");
                break;

            case "off":
                SetEnabled(false);
                shell.WriteLine("rl: выключено");
                break;

            case "toggle":
                SetEnabled(!Enabled);
                shell.WriteLine($"rl: {(Enabled ? "включено" : "выключено")}");
                break;

            case "preset":
                if (args.Length < 2)
                {
                    shell.WriteLine("Пресеты: " + string.Join(", ", RealisticLookPresets.Names));
                    break;
                }

                var preset = RealisticLookPresets.Get(args[1]);
                if (preset == null)
                {
                    shell.WriteError("Нет такого пресета. Доступны: " + string.Join(", ", RealisticLookPresets.Names));
                    break;
                }

                Settings = preset;
                PresetName = args[1].ToLowerInvariant();
                _cfg.SetCVar(RealisticLookCVars.Preset, PresetName);
                shell.WriteLine($"rl: пресет {PresetName}");
                break;

            case "set":
                if (args.Length < 3)
                {
                    shell.WriteError("rl set <параметр> <значение>. Список параметров: rl list");
                    break;
                }

                var text = args[2].Replace(',', '.');
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    shell.WriteError("Значение должно быть числом.");
                    break;
                }

                if (!Settings.TrySet(args[1], value))
                {
                    shell.WriteError("Нет такого параметра. Список: rl list");
                    break;
                }

                shell.WriteLine($"rl: {args[1]} = {value.ToString(CultureInfo.InvariantCulture)}");
                break;

            case "bloom":
                if (args.Length < 2 || !TryParseOnOff(args[1], out var bloomOn))
                {
                    shell.WriteError("rl bloom on|off");
                    break;
                }

                Settings.BloomEnabled = bloomOn;
                shell.WriteLine($"rl: bloom {(bloomOn ? "вкл" : "выкл")}");
                break;

            case "fog":
                if (args.Length < 2 || !TryParseOnOff(args[1], out var fogOn))
                {
                    shell.WriteError("rl fog on|off");
                    break;
                }

                Settings.FogEnabled = fogOn;
                shell.WriteLine($"rl: туман {(fogOn ? "вкл" : "выкл")}");
                break;

            case "list":
                shell.WriteLine("Параметры: " + string.Join(", ", RealisticLookSettings.Keys));
                shell.WriteLine("Пресеты: " + string.Join(", ", RealisticLookPresets.Names));
                break;

            default:
                shell.WriteError("Неизвестная подкоманда. Введите rl без аргументов для справки.");
                break;
        }
    }

    private static bool TryParseOnOff(string s, out bool value)
    {
        switch (s.ToLowerInvariant())
        {
            case "on":
            case "1":
            case "true":
                value = true;
                return true;
            case "off":
            case "0":
            case "false":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }
}
