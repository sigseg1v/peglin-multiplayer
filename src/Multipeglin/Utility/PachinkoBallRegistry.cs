using System.Collections.Generic;
using HarmonyLib;

namespace Multipeglin.Utility;

/// <summary>
/// Static registry of every live <see cref="PachinkoBall"/> in the scene,
/// maintained by Harmony patches on Awake / OnDestroy. Replaces hot-path
/// <c>FindObjectsOfType&lt;PachinkoBall&gt;()</c> calls in HostBallSync and
/// BallPositionSync, which were running every 50–100 ms and allocating a
/// fresh array each tick.
///
/// With Summoning Circles chain-spawning 27–60+ balls, the previous
/// FindObjectsOfType-per-tick scan was a leading cause of host lag.
/// </summary>
public static class PachinkoBallRegistry
{
    // HashSet for O(1) add/remove; the enumeration order doesn't matter for our
    // use (we filter by IsFiring/IsDummy at read time).
    private static readonly HashSet<PachinkoBall> _live = new HashSet<PachinkoBall>();

    /// <summary>Snapshot the live set. Allocates a list — call infrequently.</summary>
    public static List<PachinkoBall> Snapshot()
    {
        var list = new List<PachinkoBall>(_live.Count);
        foreach (var ball in _live)
        {
            if (ball != null)
            {
                list.Add(ball);
            }
        }

        return list;
    }

    /// <summary>
    /// Reusable iteration into a caller-provided buffer to avoid allocating
    /// a fresh list on every hot-path tick.
    /// </summary>
    public static void CopyInto(List<PachinkoBall> buffer)
    {
        buffer.Clear();
        if (buffer.Capacity < _live.Count)
        {
            buffer.Capacity = _live.Count;
        }

        foreach (var ball in _live)
        {
            if (ball != null)
            {
                buffer.Add(ball);
            }
        }
    }

    /// <summary>Find any non-dummy ball in the set, or null.</summary>
    public static PachinkoBall FindAnyNonDummy()
    {
        foreach (var ball in _live)
        {
            if (ball != null && !ball.IsDummy)
            {
                return ball;
            }
        }

        return null;
    }

    public static int Count => _live.Count;

    internal static void Add(PachinkoBall ball)
    {
        if (ball != null)
        {
            _live.Add(ball);
        }
    }

    internal static void Remove(PachinkoBall ball)
    {
        if (ball != null)
        {
            _live.Remove(ball);
        }
    }

    public static void Clear() => _live.Clear();
}

/// <summary>
/// Harmony patches that mirror PachinkoBall lifecycle into
/// <see cref="PachinkoBallRegistry"/>. Active on both host and client; the
/// client just never reads the registry.
/// </summary>
[HarmonyPatch]
internal static class PachinkoBallRegistryPatches
{
    [HarmonyPatch(typeof(PachinkoBall), "Awake")]
    [HarmonyPostfix]
    public static void Awake_Postfix(PachinkoBall __instance)
    {
        PachinkoBallRegistry.Add(__instance);
    }

    [HarmonyPatch(typeof(PachinkoBall), "OnDestroy")]
    [HarmonyPostfix]
    public static void OnDestroy_Postfix(PachinkoBall __instance)
    {
        PachinkoBallRegistry.Remove(__instance);
    }
}
