using System;
using System.Collections.Generic;
using Battle.Attacks;
using BepInEx.Logging;
using UnityEngine;

namespace Multipeglin.Utility;

/// <summary>
/// Central registry mapping GUIDs to orb GameObjects.
/// Host side: assigns GUIDs when orbs are captured.
/// Client side: registers orbs with host-provided GUIDs.
/// </summary>
public class OrbIdentifier
{
    private readonly Dictionary<string, GameObject> _guidToOrb = new Dictionary<string, GameObject>();
    private readonly Dictionary<GameObject, string> _orbToGuid = new Dictionary<GameObject, string>();

    private static ManualLogSource Log => MultiplayerPlugin.Logger;

    public string GetOrAssignGuid(GameObject orb)
    {
        if (orb == null) return "null";

        if (_orbToGuid.TryGetValue(orb, out var existing))
            return existing;

        var guid = Guid.NewGuid().ToString("N")[..12];
        _guidToOrb[guid] = orb;
        _orbToGuid[orb] = guid;
        return guid;
    }

    public void Register(GameObject orb, string guid)
    {
        if (orb == null || string.IsNullOrEmpty(guid)) return;

        if (_guidToOrb.TryGetValue(guid, out var old) && old != orb)
            _orbToGuid.Remove(old);
        if (_orbToGuid.TryGetValue(orb, out var oldGuid) && oldGuid != guid)
            _guidToOrb.Remove(oldGuid);

        _guidToOrb[guid] = orb;
        _orbToGuid[orb] = guid;
    }

    public GameObject Find(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;
        if (_guidToOrb.TryGetValue(guid, out var orb) && orb != null)
            return orb;
        if (orb == null && _guidToOrb.ContainsKey(guid))
            _guidToOrb.Remove(guid);
        return null;
    }

    public string GetGuid(GameObject orb)
    {
        if (orb == null) return null;
        return _orbToGuid.TryGetValue(orb, out var guid) ? guid : null;
    }

    /// <summary>Backward-compatible name-based ID for orbs (used in events).</summary>
    public string GetId(GameObject ball)
    {
        if (ball == null) return "unknown";
        var attack = ball.GetComponent<Attack>();
        if (attack != null && !string.IsNullOrEmpty(attack.locNameString))
            return attack.locNameString;
        return ball.name;
    }

    public int GetLevel(GameObject ball)
    {
        if (ball == null) return 0;
        var attack = ball.GetComponent<Attack>();
        return attack?.Level ?? 0;
    }

    public void Clear()
    {
        var count = _guidToOrb.Count;
        _guidToOrb.Clear();
        _orbToGuid.Clear();
        if (count > 0)
            Log?.LogInfo($"[OrbGUID] Cleared {count} entries");
    }

    public int Count => _guidToOrb.Count;
}
