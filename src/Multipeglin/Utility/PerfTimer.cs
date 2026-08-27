using System;
using System.Diagnostics;

namespace Multipeglin.Utility;

/// <summary>
/// Minimal main-thread stopwatch for the client apply path.
///
/// Everything the appliers do runs inside Unity's main thread, so any single
/// slow apply is a visible hitch: at 60fps a frame is 16.7ms, and the periodic
/// snapshot appliers run in one go on a heartbeat tick. A 200ms apply is a
/// twelve-frame freeze followed by a snap to the new state — exactly what
/// "the client stutters every couple of seconds" looks like.
///
/// Deliberately allocation-free on the fast path: <see cref="Now"/> is a raw
/// timestamp, <see cref="MsSince"/> is arithmetic, and no string is built
/// unless the caller decides the measurement is worth logging.
/// </summary>
public static class PerfTimer
{
    private static bool? _enabled;

    /// <summary>
    /// Milliseconds above which a phase is worth reporting. Half a 60fps frame:
    /// below this a phase cannot be the cause of a visible hitch on its own.
    /// </summary>
    public const double WarnMs = 8.0;

    /// <summary>
    /// True when MULTIPEGLIN_PERF is "1"/"true". Cached for the process lifetime.
    /// Off by default so a normal run pays nothing but two Stopwatch reads.
    /// </summary>
    public static bool Enabled
    {
        get
        {
            if (_enabled.HasValue)
            {
                return _enabled.Value;
            }

            var v = Environment.GetEnvironmentVariable("MULTIPEGLIN_PERF");
            _enabled = v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
            return _enabled.Value;
        }
    }

    public static long Now => Stopwatch.GetTimestamp();

    public static double MsSince(long start)
    {
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }
}
