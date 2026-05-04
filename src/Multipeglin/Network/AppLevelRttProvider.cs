using System.Diagnostics;
using BepInEx.Logging;
using Multipeglin.Events.Network;
using Multipeglin.Multiplayer;

namespace Multipeglin.Network;

/// <summary>
/// Transport-agnostic RTT measurement. The client sends a PingEvent every
/// <see cref="PingIntervalSeconds"/> with a monotonic token; the host echoes
/// it back as a PongEvent; we time the round trip with Stopwatch (frame-rate
/// independent, monotonic, robust against system-clock skew).
///
/// Works equally over LiteNetLib and Steam P2P — Steam's old ISteamNetworking
/// API doesn't expose RTT, and migrating to ISteamNetworkingSockets purely
/// for diagnostics would be a large transport refactor. App-level pings cost
/// one tiny packet per second per client and give us a real number we can
/// drive interpolation off of on either transport.
/// </summary>
public sealed class AppLevelRttProvider : IRttProvider
{
    // 5 s ping cadence balances bandwidth (≈24 B/s overhead per client after
    // deflate framing) against the renderer's adaptive-delay reaction time —
    // the median-of-5 smoothing means a real RTT spike still lands in the
    // tier within ~25 s, which is fine for ball-flight buffering.
    private const double PingIntervalSeconds = 5.0;
    private const int MaxRttSamples = 5;

    private readonly IMultiplayerMode _mode;
    private readonly ManualLogSource _log;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private long _nextToken = 1;
    private long _lastPingToken;
    private long _lastPingSentTicks;
    private double _lastSentSeconds = -PingIntervalSeconds;
    private readonly int[] _samples = new int[MaxRttSamples];
    private int _sampleCount;
    private int _sampleIndex;
    private int _smoothedMs;

    public AppLevelRttProvider(IMultiplayerMode mode, ManualLogSource log)
    {
        _mode = mode;
        _log = log;
    }

    /// <summary>Median of the last few pong samples, or 0 when nothing has come back yet.</summary>
    public int CurrentRttMs => _smoothedMs;

    /// <summary>
    /// Drive from a per-frame Update on the client. No-op when not connected
    /// as a client — host has no peer to ping (we only need RTT on the side
    /// that adapts visuals).
    /// </summary>
    public void Tick()
    {
        if (_mode == null || !_mode.IsSpectating)
        {
            return;
        }

        var services = MultiplayerPlugin.Services;
        if (services == null
            || !services.TryResolve<INetworkTransport>(out var transport)
            || !transport.IsConnected
            || !services.TryResolve<IMessageSender>(out var sender))
        {
            return;
        }

        var nowSec = _clock.Elapsed.TotalSeconds;
        if (nowSec - _lastSentSeconds < PingIntervalSeconds)
        {
            return;
        }

        _lastSentSeconds = nowSec;
        _lastPingToken = _nextToken++;
        _lastPingSentTicks = _clock.ElapsedTicks;
        sender.Send(new PingEvent { Token = _lastPingToken });
    }

    /// <summary>
    /// Called by PongClientHandler. Drops stale pongs (only the most recent
    /// outstanding ping counts — older ones got superseded by a newer probe).
    /// </summary>
    public void OnPongReceived(long token)
    {
        if (token != _lastPingToken)
        {
            return;
        }

        var elapsedTicks = _clock.ElapsedTicks - _lastPingSentTicks;
        var rttMs = (int)(elapsedTicks * 1000L / Stopwatch.Frequency);
        if (rttMs < 0)
        {
            rttMs = 0;
        }

        _samples[_sampleIndex] = rttMs;
        _sampleIndex = (_sampleIndex + 1) % MaxRttSamples;
        if (_sampleCount < MaxRttSamples)
        {
            _sampleCount++;
        }

        _smoothedMs = Median();
    }

    private int Median()
    {
        var copy = new int[_sampleCount];
        for (var i = 0; i < _sampleCount; i++)
        {
            copy[i] = _samples[i];
        }

        System.Array.Sort(copy);
        return copy[_sampleCount / 2];
    }
}
