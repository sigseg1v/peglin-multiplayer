using System;
using System.Collections.Generic;
using System.Linq;
using Battle;
using Battle.Enemies;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeglinMods.Multiplayer.Utility;

/// <summary>
/// Dumps detailed game state for diagnosing host/client sync issues.
/// Call DumpBattleState() at key moments to capture a snapshot of what
/// actually exists in the scene.
/// </summary>
public static class DiagnosticLogger
{
    private static ManualLogSource Log => MultiplayerPlugin.Logger;

    /// <summary>
    /// Log the complete state of the current scene — enemies, pegs, player, StaticGameData.
    /// Call this on both host and client at the same moment to compare.
    /// </summary>
    public static void DumpBattleState(string trigger)
    {
        try
        {
            var scene = SceneManager.GetActiveScene().name;
            Log?.LogInfo($"=== DIAG [{trigger}] scene={scene} ===");

            // StaticGameData
            Log?.LogInfo($"  SGD: seed={StaticGameData.currentSeed}, floor={StaticGameData.totalFloorCount}, " +
                $"class={StaticGameData.chosenClass}, nodeIdx={StaticGameData.chosenNextNodeIndex}, " +
                $"seedSet={StaticGameData.seedSet}, dataToLoad={(StaticGameData.dataToLoad != null ? StaticGameData.dataToLoad.GetType().Name : "NULL")}");

            if (StaticGameData.dataToLoad is Data.MapDataBattle mdb)
            {
                Log?.LogInfo($"  MapDataBattle: name={mdb.name}, pegLayout={(mdb.pegLayout != null ? mdb.pegLayout.name : "NULL")}, " +
                    $"slots={mdb.NumberOfSlots}, starterSpawns={mdb.starterSpawns?.Count ?? -1}, waves={mdb.waveGroups?.Length ?? -1}");
            }

            if (scene != "Battle")
            {
                Log?.LogInfo($"=== END DIAG [{trigger}] (not Battle) ===");
                return;
            }

            // Enemies
            var em = UnityEngine.Object.FindObjectOfType<EnemyManager>();
            if (em != null)
            {
                var enemies = em.Enemies;
                Log?.LogInfo($"  Enemies ({enemies?.Count ?? 0}):");
                if (enemies != null)
                {
                    for (int i = 0; i < enemies.Count; i++)
                    {
                        var e = enemies[i];
                        if (e == null) { Log?.LogInfo($"    [{i}] NULL"); continue; }
                        Log?.LogInfo($"    [{i}] {e.locKey} name={e.gameObject.name} hp={e.CurrentHealth}/{GetMaxHp(e):F0} " +
                            $"pos=({e.transform.position.x:F2},{e.transform.position.y:F2}) flying={e.IsFlying}");
                    }
                }
            }
            else
            {
                Log?.LogInfo("  EnemyManager: NOT FOUND");
            }

            // Pegs
            var pegs = UnityEngine.Object.FindObjectsOfType<Peg>(true);
            var activePegs = pegs.Where(p => p != null && p.gameObject.activeSelf).ToArray();
            var pegTypes = activePegs.GroupBy(p => p.pegType).OrderByDescending(g => g.Count());
            Log?.LogInfo($"  Pegs: {activePegs.Length} active / {pegs.Length} total");
            foreach (var g in pegTypes)
                Log?.LogInfo($"    {g.Key}: {g.Count()}");

            // First 5 peg positions for comparison
            var sorted = activePegs.OrderBy(p => p.transform.position.y).ThenBy(p => p.transform.position.x).Take(10);
            Log?.LogInfo($"  First 10 pegs by pos:");
            foreach (var p in sorted)
                Log?.LogInfo($"    ({p.transform.position.x:F3},{p.transform.position.y:F3}) type={p.pegType}");

            // BattleController state
            Log?.LogInfo($"  BattleState: {BattleController.CurrentBattleState}");

            // Ball
            var balls = UnityEngine.Object.FindObjectsOfType<PachinkoBall>();
            Log?.LogInfo($"  Balls: {balls.Length} (dummy={balls.Count(b => b.IsDummy)}, real={balls.Count(b => !b.IsDummy)})");

            Log?.LogInfo($"=== END DIAG [{trigger}] ===");
        }
        catch (Exception ex)
        {
            Log?.LogError($"DiagnosticLogger.DumpBattleState failed: {ex.Message}");
        }
    }

    private static float GetMaxHp(Enemy e)
    {
        try
        {
            var f = AccessTools.Field(typeof(Enemy), "_maxHealth");
            return f != null ? (float)f.GetValue(e) : -1;
        }
        catch { return -1; }
    }
}
