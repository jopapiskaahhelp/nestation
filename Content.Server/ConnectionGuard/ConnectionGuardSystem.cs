using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Server.ConnectionGuard;

[CVarDefs]
public sealed class GuardCVars : CVars
{
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("guard.enabled", true, CVar.SERVERONLY);

    public static readonly CVarDef<int> MaxPerIp =
        CVarDef.Create("guard.max_connections_per_ip", 5, CVar.SERVERONLY);

    public static readonly CVarDef<int> MaxAttempts =
        CVarDef.Create("guard.max_attempts", 10, CVar.SERVERONLY);

    public static readonly CVarDef<int> WindowSeconds =
        CVarDef.Create("guard.window_seconds", 60, CVar.SERVERONLY);

    public static readonly CVarDef<int> BlockSeconds =
        CVarDef.Create("guard.block_seconds", 600, CVar.SERVERONLY);
}

public sealed partial class ConnectionGuardSystem : EntitySystem
{
    [Dependency] private IServerNetManager _net = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const int MaxTracked = 50_000;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);

    private readonly Dictionary<IPAddress, IpState> _state = new();
    private TimeSpan _nextCleanup;

    private sealed class IpState
    {
        public TimeSpan WindowStart;
        public int Attempts;
        public TimeSpan BlockedUntil;
    }

    public override void Initialize()
    {
        base.Initialize();
        _net.Connecting += OnConnecting;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _net.Connecting -= OnConnecting;
    }

    private Task OnConnecting(NetConnectingArgs args)
    {
        if (!_cfg.GetCVar(GuardCVars.Enabled))
            return Task.CompletedTask;

        var ip = Normalize(args.IP.Address);

        if (IPAddress.IsLoopback(ip))
            return Task.CompletedTask;

        var now = _timing.RealTime;
        var window = TimeSpan.FromSeconds(_cfg.GetCVar(GuardCVars.WindowSeconds));

        if (!_state.TryGetValue(ip, out var st))
        {
            if (_state.Count >= MaxTracked)
                return Task.CompletedTask;

            st = new IpState { WindowStart = now };
            _state[ip] = st;
        }

        if (st.BlockedUntil > now)
        {
            args.Deny("Слишком много попыток подключения. Попробуйте позже.");
            return Task.CompletedTask;
        }

        if (now - st.WindowStart > window)
        {
            st.WindowStart = now;
            st.Attempts = 0;
        }

        st.Attempts++;

        if (st.Attempts > _cfg.GetCVar(GuardCVars.MaxAttempts))
        {
            st.BlockedUntil = now + TimeSpan.FromSeconds(_cfg.GetCVar(GuardCVars.BlockSeconds));
            Log.Warning($"ConnectionGuard: блокировка {ip}: слишком частые попытки подключения");
            args.Deny("Слишком много попыток подключения. Попробуйте позже.");
            return Task.CompletedTask;
        }

        var maxPerIp = _cfg.GetCVar(GuardCVars.MaxPerIp);
        var concurrent = 0;
        foreach (var channel in _net.Channels)
        {
            if (Normalize(channel.RemoteEndPoint.Address).Equals(ip))
                concurrent++;
        }

        if (concurrent >= maxPerIp)
        {
            Log.Warning($"ConnectionGuard: отказ {ip}: уже {concurrent} соединений");
            args.Deny("Слишком много одновременных подключений с вашего IP.");
        }

        return Task.CompletedTask;
    }

    public override void Update(float frameTime)
    {
        var now = _timing.RealTime;
        if (now < _nextCleanup)
            return;

        _nextCleanup = now + CleanupInterval;
        var window = TimeSpan.FromSeconds(_cfg.GetCVar(GuardCVars.WindowSeconds));

        List<IPAddress>? stale = null;
        foreach (var (ip, st) in _state)
        {
            if (st.BlockedUntil <= now && now - st.WindowStart > window)
                (stale ??= new()).Add(ip);
        }

        if (stale == null)
            return;

        foreach (var ip in stale)
            _state.Remove(ip);
    }

    private static IPAddress Normalize(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            return ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            Span<byte> bytes = stackalloc byte[16];
            if (ip.TryWriteBytes(bytes, out _))
            {
                bytes[8..].Clear();
                return new IPAddress(bytes);
            }
        }

        return ip;
    }
}
