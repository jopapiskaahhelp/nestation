// Nestation CC BY-NC 3.0
using System.Numerics;
using System.Text;
using Content.Shared._Nestation.Reactor;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.Client._Nestation.Reactor;

public sealed class NestationConsoleWindow : DefaultWindow
{
    public event Action<NestationConsoleMessage>? OnCommand;

    private readonly Label _status = new();
    private readonly Label _warnings = new() { FontColorOverride = Color.OrangeRed };
    private readonly Label _autoLabel = new();
    private readonly Button _autoButton = new();
    private readonly Button _breakerButton = new();
    private readonly Button _bypassButton = new();

    public NestationConsoleWindow()
    {
        Title = Loc.GetString("nestation-ui-title");
        MinSize = new Vector2(470, 640);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Margin = new Thickness(6),
        };

        root.AddChild(_status);
        root.AddChild(_warnings);

        var scram = Row(root);
        scram.AddChild(Make("nestation-ui-scram", () => Send(NestationCommand.Scram), Color.Red));
        scram.AddChild(Make("nestation-ui-reset-scram", () => Send(NestationCommand.ResetScram)));

        var rods = Row(root);
        rods.AddChild(_autoLabel);
        rods.AddChild(_autoButton);
        _autoButton.OnPressed += _ => Send(NestationCommand.ToggleAuto);
        rods.AddChild(Make("nestation-ui-rods-out", () => Send(NestationCommand.RodsDelta, -0.05f)));
        rods.AddChild(Make("nestation-ui-rods-in", () => Send(NestationCommand.RodsDelta, 0.05f)));

        var setpoint = Row(root);
        setpoint.AddChild(new Label { Text = Loc.GetString("nestation-ui-setpoint") });
        setpoint.AddChild(Make("nestation-ui-minus-5", () => Send(NestationCommand.SetpointDelta, -0.05f)));
        setpoint.AddChild(Make("nestation-ui-plus-5", () => Send(NestationCommand.SetpointDelta, 0.05f)));
        setpoint.AddChild(Make("nestation-ui-minus-10", () => Send(NestationCommand.SetpointDelta, -0.10f)));
        setpoint.AddChild(Make("nestation-ui-plus-10", () => Send(NestationCommand.SetpointDelta, 0.10f)));

        AddPumpRow(root, "nestation-ui-pump-primary", NestationPumpKind.Primary, false);
        AddPumpRow(root, "nestation-ui-pump-feed", NestationPumpKind.Feed, true);
        AddPumpRow(root, "nestation-ui-pump-circ", NestationPumpKind.Circulation, true);

        var turbine = Row(root);
        turbine.AddChild(_breakerButton);
        _breakerButton.OnPressed += _ => Send(NestationCommand.ToggleBreaker);
        turbine.AddChild(Make("nestation-ui-reset-turbine", () => Send(NestationCommand.ResetTurbine)));

        var bypass = Row(root);
        bypass.AddChild(_bypassButton);
        _bypassButton.OnPressed += _ => Send(NestationCommand.ToggleBypass);

        Contents.AddChild(root);
    }

    private static BoxContainer Row(BoxContainer parent)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
        };

        parent.AddChild(row);
        return row;
    }

    private void AddPumpRow(BoxContainer parent, string label, NestationPumpKind kind, bool hasAuto)
    {
        var row = Row(parent);
        row.AddChild(new Label { Text = Loc.GetString(label) });
        row.AddChild(Make("nestation-ui-minus-10", () => Send(NestationCommand.PumpDelta, -0.1f, kind)));
        row.AddChild(Make("nestation-ui-plus-10", () => Send(NestationCommand.PumpDelta, 0.1f, kind)));

        if (hasAuto)
            row.AddChild(Make("nestation-ui-auto", () => Send(NestationCommand.PumpAuto, 0f, kind)));
    }

    private Button Make(string key, Action action, Color? color = null)
    {
        var button = new Button { Text = Loc.GetString(key) };

        if (color != null)
            button.Modulate = color.Value;

        button.OnPressed += _ => action();
        return button;
    }

    private void Send(NestationCommand command, float amount = 0f, NestationPumpKind pump = NestationPumpKind.Primary)
    {
        OnCommand?.Invoke(new NestationConsoleMessage(command, amount, pump));
    }

    public void Update(NestationConsoleState s)
    {
        if (!s.Linked)
        {
            _status.Text = Loc.GetString("nestation-ui-no-link");
            _warnings.Text = string.Empty;
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine(Loc.GetString("nestation-ui-core",
            ("power", P(s.Neutron)),
            ("mw", MW(s.ThermalPower)),
            ("setpoint", P(s.Setpoint)),
            ("rods", P(s.Rods)),
            ("fuel", T(s.FuelTemp)),
            ("burnup", P(s.Burnup))));
        sb.AppendLine(Loc.GetString("nestation-ui-primary",
            ("coolant", T(s.CoolantTemp)),
            ("flow", P(s.PrimaryFlow)),
            ("setpoint", P(s.PrimarySetpoint))));
        sb.AppendLine(Loc.GetString("nestation-ui-steam",
            ("temp", T(s.SteamTemp)),
            ("pressure", s.SteamPressure.ToString("0.0")),
            ("level", P(s.SgLevel)),
            ("feed", P(s.FeedFlow)),
            ("feedset", P(s.FeedSetpoint)),
            ("feedauto", Loc.GetString(s.FeedAuto ? "nestation-ui-auto-on" : "nestation-ui-auto-off"))));
        sb.AppendLine(Loc.GetString("nestation-ui-turbine",
            ("rpm", (s.Rpm * 3000f).ToString("0")),
            ("eff", P(s.Efficiency)),
            ("breaker", Loc.GetString(s.BreakerClosed ? "nestation-breaker-closed" : "nestation-breaker-open"))));
        sb.AppendLine(Loc.GetString("nestation-ui-power",
            ("gross", KW(s.Gross)),
            ("own", KW(s.Own)),
            ("net", KW(s.Net)),
            ("reserve", s.ReserveMj.ToString("0.0"))));
        sb.AppendLine(Loc.GetString("nestation-ui-heat",
            ("cond", T(s.CondenserTemp)),
            ("pool", T(s.PoolTemp)),
            ("level", P(s.PoolLevel)),
            ("segments", s.PoolSegments),
            ("heat", MW(s.HeatToPool)),
            ("circ", P(s.CirculationFlow)),
            ("circset", P(s.CirculationSetpoint)),
            ("circauto", Loc.GetString(s.CirculationAuto ? "nestation-ui-auto-on" : "nestation-ui-auto-off"))));
        sb.AppendLine(Loc.GetString("nestation-ui-vessel",
            ("integrity", P(s.Integrity)),
            ("inventory", P(s.Inventory)),
            ("decay", s.DecayKw.ToString("0")),
            ("dryout", s.DryoutSeconds >= 0f ? s.DryoutSeconds.ToString("0") + " c" : "-")));
        sb.AppendLine(Loc.GetString("nestation-ui-hydrogen", ("h2", s.Hydrogen.ToString("0.0"))));

        if (s.PotentialTnt > 0.5f)
        {
            sb.AppendLine(Loc.GetString("nestation-ui-blast",
                ("tnt", s.PotentialTnt.ToString("0")),
                ("radius", s.SafeRadius.ToString("0"))));
        }

        if (s.Countdown >= 0f)
            sb.AppendLine(Loc.GetString("nestation-ui-countdown", ("seconds", s.Countdown.ToString("0.0"))));

        _status.Text = sb.ToString();

        var warn = new StringBuilder();

        foreach (NestationWarning flag in Enum.GetValues(typeof(NestationWarning)))
        {
            if (flag != NestationWarning.None && (s.Warnings & flag) == flag)
                warn.AppendLine(Loc.GetString("nestation-warning-" + flag));
        }

        _warnings.Text = warn.ToString();

        _autoLabel.Text = Loc.GetString("nestation-ui-rod-mode");
        _autoButton.Text = Loc.GetString(s.AutoMode ? "nestation-ui-mode-auto" : "nestation-ui-mode-manual");
        _breakerButton.Text = Loc.GetString(s.BreakerClosed ? "nestation-ui-breaker-open" : "nestation-ui-breaker-close");
        _bypassButton.Text = Loc.GetString(s.Bypassed ? "nestation-ui-bypass-off" : "nestation-ui-bypass-on");
        _bypassButton.Modulate = s.Bypassed ? Color.Red : Color.White;
    }

    private static string P(float v) => (v * 100f).ToString("0");
    private static string T(float v) => v.ToString("0");
    private static string MW(float v) => (v / 1_000_000f).ToString("0.00");
    private static string KW(float v) => (v / 1000f).ToString("0");
}
