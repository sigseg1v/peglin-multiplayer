using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace Multipeglin.Utility;

/// <summary>
/// Central registry mapping GUIDs to Peg instances.
/// Host side: assigns GUIDs when pegs are captured (via GetOrAssignGuid).
/// Client side: registers pegs with host-provided GUIDs (via Register).
/// Both sides: look up pegs by GUID (via Find).
/// </summary>
public class PegIdentifier
{
    private readonly Dictionary<string, Peg> _guidToPeg = new Dictionary<string, Peg>();
    private readonly Dictionary<Peg, string> _pegToGuid = new Dictionary<Peg, string>();

    private static ManualLogSource Log => MultiplayerPlugin.Logger;

    /// <summary>
    /// Get the GUID for a peg, or assign a new one if it doesn't have one yet.
    /// Used on the HOST.
    /// </summary>
    public string GetOrAssignGuid(Peg peg)
    {
        if (peg == null)
            return "null";

        if (_pegToGuid.TryGetValue(peg, out var existing))
            return existing;

        var guid = Guid.NewGuid().ToString("N")[..12];
        _guidToPeg[guid] = peg;
        _pegToGuid[peg] = guid;
        return guid;
    }

    /// <summary>
    /// Register a peg with a specific GUID (from the host).
    /// Used on the CLIENT.
    /// </summary>
    public void Register(Peg peg, string guid)
    {
        if (peg == null || string.IsNullOrEmpty(guid))
            return;

        if (_guidToPeg.TryGetValue(guid, out var oldPeg) && oldPeg != peg)
            _pegToGuid.Remove(oldPeg);

        if (_pegToGuid.TryGetValue(peg, out var oldGuid) && oldGuid != guid)
            _guidToPeg.Remove(oldGuid);

        _guidToPeg[guid] = peg;
        _pegToGuid[peg] = guid;
    }

    /// <summary>
    /// Find a peg by its GUID.
    /// </summary>
    public Peg Find(string guid)
    {
        if (string.IsNullOrEmpty(guid))
            return null;

        if (_guidToPeg.TryGetValue(guid, out var peg) && peg != null)
            return peg;

        if (peg == null && _guidToPeg.ContainsKey(guid))
            _guidToPeg.Remove(guid);

        return null;
    }

    /// <summary>Get GUID for a peg, or null if not registered.</summary>
    public string GetGuid(Peg peg)
    {
        if (peg == null)
            return null;
        return _pegToGuid.TryGetValue(peg, out var guid) ? guid : null;
    }

    public void Clear()
    {
        var count = _guidToPeg.Count;
        _guidToPeg.Clear();
        _pegToGuid.Clear();
        if (count > 0)
            Log?.LogInfo($"[PegGUID] Cleared {count} entries");
    }

    public int Count => _guidToPeg.Count;

    public void DumpState(string trigger)
    {
        Log?.LogInfo($"[PegGUID] === DUMP ({trigger}) {_guidToPeg.Count} entries ===");
        int shown = 0;
        foreach (var kvp in _guidToPeg)
        {
            var p = kvp.Value;
            if (p != null)
                Log?.LogInfo($"[PegGUID]   {kvp.Key} → type={p.pegType} pos=({p.transform.position.x:F2},{p.transform.position.y:F2}) active={p.gameObject.activeSelf}");
            else
                Log?.LogInfo($"[PegGUID]   {kvp.Key} → DESTROYED");

            if (++shown >= 20)
            {
                Log?.LogInfo($"[PegGUID]   ... and {_guidToPeg.Count - shown} more");
                break;
            }
        }
    }
}
