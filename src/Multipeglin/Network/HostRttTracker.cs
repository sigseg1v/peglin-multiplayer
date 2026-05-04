using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Logging;
using Multipeglin.DI;
using Multipeglin.Events.Network;
using Multipeglin.Multiplayer;
using Multipeglin.Network.Protocol;

namespace Multipeglin.Network;

/// <summary>
/// Host-side per-peer RTT tracker. Sends a HostPingEvent to each connected
/// peer every <see cref="ProbeIntervalSeconds"/> seconds and keeps the latest
/// RTT measurement per peerId so we can log host→client latency
/// independently for each player.
/// </summary>
public sealed class HostRttTracker
{
    // 5 s probe cadence: each pair is ~60 B raw (deflate-framed under
    // CompressionThreshold so it sends raw + 1-byte header). 4 peers × 2
    // packets / 5 s ≈ 96 B/s of overhead — invisible next to the 20 Hz ball
    // stream and well under any QoS limit.
    private const double ProbeIntervalSeconds = 5.0;
    private const double LogIntervalSeconds = 10.0;

    private readonly IMultiplayerMode _mode;
    private readonly ManualLogSource _log;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private long _nextToken = 1;
    private double _lastProbeAt = -ProbeIntervalSeconds;
    private double _lastLogAt;
    private bool _loggedFirstTick;
    private int _probeCount;
    private int _pongCount;

    // peerId → (token, sentTicks). Only the most-recent outstanding probe per
    // peer counts; older tokens just expire silently when superseded.
    private readonly Dictionary<int, (long Token, long SentTicks)> _outstanding = new Dictionary<int, (long, long)>();

    private readonly Dictionary<int, int> _latestRttMs = new Dictionary<int, int>();

    public HostRttTracker(IMultiplayerMode mode, ManualLogSource log)
    {
        _mode = mode;
        _log = log;
    }

    public IReadOnlyDictionary<int, int> PerPeerRttMs => _latestRttMs;

    public void Tick()
    {
        if (_mode == null || !_mode.IsHosting)
        {
            return;
        }

        var services = MultiplayerPlugin.Services;
        if (services == null
            || !services.TryResolve<INetworkTransport>(out var transport)
            || !services.TryResolve<INetworkSerializer>(out var serializer))
        {
            return;
        }

        if (!_loggedFirstTick)
        {
            _loggedFirstTick = true;
            _log.LogInfo($"[HostRtt] tracker armed (probe={ProbeIntervalSeconds}s, log={LogIntervalSeconds}s)");
        }

        var nowSec = _clock.Elapsed.TotalSeconds;

        if (nowSec - _lastProbeAt >= ProbeIntervalSeconds)
        {
            _lastProbeAt = nowSec;
            ProbeAllPeers(transport, serializer);
        }

        if (nowSec - _lastLogAt >= LogIntervalSeconds)
        {
            _lastLogAt = nowSec;
            LogPerPeerRtt(services);
        }
    }

    private void ProbeAllPeers(INetworkTransport transport, INetworkSerializer serializer)
    {
        var peers = transport.ConnectedPeerIds;
        var n = 0;
        foreach (var peerId in peers)
        {
            var token = _nextToken++;
            _outstanding[peerId] = (token, _clock.ElapsedTicks);
            var bytes = serializer.Serialize(new HostPingEvent { Token = token });
            transport.SendTo(peerId, bytes);
            n++;
        }

        _probeCount += n;
        if (n == 0)
        {
            _log.LogWarning("[HostRtt] probe cycle: 0 connected peers");
        }
    }

    public void OnPongReceived(int peerId, long token)
    {
        if (!_outstanding.TryGetValue(peerId, out var probe) || probe.Token != token)
        {
            _log.LogWarning($"[HostRtt] stale pong: peer={peerId} token={token}");
            return;
        }

        var elapsedTicks = _clock.ElapsedTicks - probe.SentTicks;
        var rttMs = (int)(elapsedTicks * 1000L / Stopwatch.Frequency);
        if (rttMs < 0)
        {
            rttMs = 0;
        }

        _latestRttMs[peerId] = rttMs;
        _pongCount++;
    }

    private void LogPerPeerRtt(IServiceContainer services)
    {
        if (_latestRttMs.Count == 0)
        {
            _log.LogInfo($"[HostRtt] no pongs received yet (probesSent={_probeCount}, pongsRecv={_pongCount})");
            return;
        }

        services.TryResolve<PlayerRegistry>(out var registry);

        var sb = new System.Text.StringBuilder();
        sb.Append("[HostRtt] per-peer round-trip:");
        foreach (var kv in _latestRttMs)
        {
            var name = registry?.GetSlotByPeerId(kv.Key)?.PlayerName ?? "?";
            sb.Append($" peer={kv.Key}({name}):{kv.Value}ms");
        }

        sb.Append($" (probesSent={_probeCount}, pongsRecv={_pongCount})");
        _log.LogInfo(sb.ToString());
    }
}
