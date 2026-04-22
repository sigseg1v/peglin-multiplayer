using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace Multipeglin.Utility;

/// <summary>
/// Central registry mapping GUIDs to in-flight PachinkoBall instances (host side only).
/// The client never calls this — it reconciles purely on GUIDs coming from the host.
/// </summary>
public class BallIdentifier
{
    private readonly Dictionary<string, PachinkoBall> _guidToBall = new Dictionary<string, PachinkoBall>();
    private readonly Dictionary<PachinkoBall, string> _ballToGuid = new Dictionary<PachinkoBall, string>();

    private static ManualLogSource Log => MultiplayerPlugin.Logger;

    /// <summary>Get the GUID for a ball, or assign a new one if it doesn't have one yet.</summary>
    public string GetOrAssignGuid(PachinkoBall ball)
    {
        if (ball == null) return "null";
        if (_ballToGuid.TryGetValue(ball, out var existing))
            return existing;

        var guid = Guid.NewGuid().ToString("N")[..12];
        _guidToBall[guid] = ball;
        _ballToGuid[ball] = guid;
        return guid;
    }

    public string GetGuid(PachinkoBall ball)
    {
        if (ball == null) return null;
        return _ballToGuid.TryGetValue(ball, out var guid) ? guid : null;
    }

    /// <summary>Forget a ball (e.g. it was destroyed). Safe on missing entries.</summary>
    public void Forget(PachinkoBall ball)
    {
        if (ball == null) return;
        if (_ballToGuid.TryGetValue(ball, out var guid))
        {
            _guidToBall.Remove(guid);
            _ballToGuid.Remove(ball);
        }
    }

    public void ForgetByGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return;
        if (_guidToBall.TryGetValue(guid, out var ball))
        {
            _guidToBall.Remove(guid);
            if (ball != null) _ballToGuid.Remove(ball);
        }
    }

    /// <summary>Prune entries whose PachinkoBall is null (GameObject destroyed).</summary>
    public List<string> PruneDestroyed()
    {
        var removed = new List<string>();
        var toRemove = new List<string>();
        foreach (var kvp in _guidToBall)
        {
            if (kvp.Value == null) toRemove.Add(kvp.Key);
        }
        foreach (var guid in toRemove)
        {
            _guidToBall.Remove(guid);
            removed.Add(guid);
        }
        // Clean reverse map of stale refs
        var ballsToRemove = new List<PachinkoBall>();
        foreach (var kvp in _ballToGuid)
            if (kvp.Key == null) ballsToRemove.Add(kvp.Key);
        foreach (var b in ballsToRemove) _ballToGuid.Remove(b);
        return removed;
    }

    public void Clear()
    {
        var count = _guidToBall.Count;
        _guidToBall.Clear();
        _ballToGuid.Clear();
        if (count > 0) Log?.LogInfo($"[BallGUID] Cleared {count} entries");
    }

    public int Count => _guidToBall.Count;
}
