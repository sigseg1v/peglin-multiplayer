using System;
using System.Collections.Generic;
using Battle.Enemies;
using BepInEx.Logging;
using UnityEngine;

namespace PeglinMods.Multiplayer.Utility;

/// <summary>
/// Central registry mapping GUIDs to Enemy instances.
/// Host side: generates GUIDs when enemies spawn (via GetOrAssignGuid).
/// Client side: registers enemies with host-provided GUIDs (via Register).
/// Both sides: look up enemies by GUID (via Find).
/// </summary>
public class EnemyIdentifier
{
    private readonly Dictionary<string, Enemy> _guidToEnemy = new Dictionary<string, Enemy>();
    private readonly Dictionary<Enemy, string> _enemyToGuid = new Dictionary<Enemy, string>();

    private static ManualLogSource Log => MultiplayerPlugin.Logger;

    /// <summary>
    /// Get the GUID for an enemy, or assign a new one if it doesn't have one yet.
    /// Used on the HOST to generate stable GUIDs for all enemies.
    /// </summary>
    public string GetOrAssignGuid(Enemy enemy)
    {
        if (enemy == null) return "null";

        if (_enemyToGuid.TryGetValue(enemy, out var existing))
            return existing;

        var guid = Guid.NewGuid().ToString("N")[..12]; // 12 hex chars, compact
        _guidToEnemy[guid] = enemy;
        _enemyToGuid[enemy] = guid;
        Log?.LogInfo($"[EnemyGUID] Assigned {guid} to '{enemy.locKey}' ({enemy.gameObject.name}) " +
            $"at ({enemy.transform.position.x:F1},{enemy.transform.position.y:F1}) hp={enemy.CurrentHealth}");
        return guid;
    }

    /// <summary>
    /// Register an enemy with a specific GUID (from the host).
    /// Used on the CLIENT when creating enemies from snapshots.
    /// </summary>
    public void Register(Enemy enemy, string guid)
    {
        if (enemy == null || string.IsNullOrEmpty(guid)) return;

        // Remove any stale mapping for this GUID (enemy may have been destroyed and recreated)
        if (_guidToEnemy.TryGetValue(guid, out var oldEnemy) && oldEnemy != enemy)
        {
            _enemyToGuid.Remove(oldEnemy);
            Log?.LogInfo($"[EnemyGUID] Replaced stale mapping for {guid} (was '{oldEnemy?.locKey}')");
        }

        // Remove any existing GUID for this enemy (it may have been registered before)
        if (_enemyToGuid.TryGetValue(enemy, out var oldGuid) && oldGuid != guid)
        {
            _guidToEnemy.Remove(oldGuid);
            Log?.LogInfo($"[EnemyGUID] Enemy '{enemy.locKey}' had old GUID {oldGuid}, replacing with {guid}");
        }

        _guidToEnemy[guid] = enemy;
        _enemyToGuid[enemy] = guid;
        Log?.LogInfo($"[EnemyGUID] Registered {guid} → '{enemy.locKey}' ({enemy.gameObject.name}) " +
            $"at ({enemy.transform.position.x:F1},{enemy.transform.position.y:F1}) hp={enemy.CurrentHealth}");
    }

    /// <summary>
    /// Find an enemy by its GUID.
    /// Returns null if the GUID is unknown or the enemy has been destroyed.
    /// </summary>
    public Enemy Find(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;

        if (_guidToEnemy.TryGetValue(guid, out var enemy) && enemy != null)
            return enemy;

        // Enemy was destroyed or reference is stale — clean up
        if (enemy == null && _guidToEnemy.ContainsKey(guid))
        {
            _guidToEnemy.Remove(guid);
            Log?.LogInfo($"[EnemyGUID] Cleaned up destroyed enemy for GUID {guid}");
        }

        return null;
    }

    /// <summary>
    /// Get the GUID for an enemy, or null if not registered.
    /// Does NOT auto-assign. Use GetOrAssignGuid for host-side assignment.
    /// </summary>
    public string GetGuid(Enemy enemy)
    {
        if (enemy == null) return null;
        return _enemyToGuid.TryGetValue(enemy, out var guid) ? guid : null;
    }

    /// <summary>
    /// Remove an enemy from the registry.
    /// </summary>
    public void Unregister(Enemy enemy)
    {
        if (enemy == null) return;
        if (_enemyToGuid.TryGetValue(enemy, out var guid))
        {
            _enemyToGuid.Remove(enemy);
            _guidToEnemy.Remove(guid);
            Log?.LogInfo($"[EnemyGUID] Unregistered {guid} ('{enemy.locKey}')");
        }
    }

    /// <summary>
    /// Remove a GUID mapping.
    /// </summary>
    public void Unregister(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return;
        if (_guidToEnemy.TryGetValue(guid, out var enemy))
        {
            _guidToEnemy.Remove(guid);
            if (enemy != null)
                _enemyToGuid.Remove(enemy);
            Log?.LogInfo($"[EnemyGUID] Unregistered {guid}");
        }
    }

    /// <summary>
    /// Clear all mappings. Call on battle end or scene transition.
    /// </summary>
    public void Clear()
    {
        var count = _guidToEnemy.Count;
        _guidToEnemy.Clear();
        _enemyToGuid.Clear();
        Log?.LogInfo($"[EnemyGUID] Cleared {count} entries");
    }

    /// <summary>
    /// Log current state for diagnostics.
    /// </summary>
    public void DumpState(string trigger)
    {
        Log?.LogInfo($"[EnemyGUID] === DUMP ({trigger}) {_guidToEnemy.Count} entries ===");
        foreach (var kvp in _guidToEnemy)
        {
            var e = kvp.Value;
            if (e != null)
            {
                Log?.LogInfo($"[EnemyGUID]   {kvp.Key} → '{e.locKey}' ({e.gameObject.name}) " +
                    $"pos=({e.transform.position.x:F1},{e.transform.position.y:F1}) hp={e.CurrentHealth}");
            }
            else
            {
                Log?.LogInfo($"[EnemyGUID]   {kvp.Key} → DESTROYED");
            }
        }
    }

    /// <summary>Backward-compatible alias for GetOrAssignGuid.</summary>
    public string GetId(Enemy enemy) => GetOrAssignGuid(enemy);
}
