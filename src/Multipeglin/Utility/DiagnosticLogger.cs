using System;
using System.Collections.Generic;
using System.Linq;
using Battle;
using Battle.Enemies;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace Multipeglin.Utility;

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

            // Relics
            try
            {
                var rms = UnityEngine.Resources.FindObjectsOfTypeAll<Relics.RelicManager>();
                var rm = rms.Length > 0 ? rms[0] : null;
                if (rm != null)
                {
                    var relicDict = HarmonyLib.AccessTools.Field(typeof(Relics.RelicManager), "_ownedRelics")
                        ?.GetValue(rm) as System.Collections.IDictionary;
                    Log?.LogInfo($"  Relics: {relicDict?.Count ?? 0} owned");
                }
            }
            catch { }

            // Deck
            try
            {
                var completeDeck = DeckManager.completeDeck;
                Log?.LogInfo($"  CompleteDeck: {completeDeck?.Count ?? 0} orbs");
            }
            catch { }

            // Asset loading cache
            try
            {
                var cache = Loading.AssetLoading.Instance?.EnemyPrefabs;
                Log?.LogInfo($"  EnemyPrefabCache: {cache?.Count ?? -1} entries");
            }
            catch { }

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
                    for (var i = 0; i < enemies.Count; i++)
                    {
                        var e = enemies[i];
                        if (e == null)
                        {
                            Log?.LogInfo($"    [{i}] NULL");
                            continue;
                        }

                        Log?.LogInfo($"    [{i}] {e.locKey} name={e.gameObject.name} hp={e.CurrentHealth}/{GetMaxHp(e):F0} " +
                            $"pos=({e.transform.position.x:F2},{e.transform.position.y:F2}) flying={e.IsFlying}");
                    }
                }
            }
            else
            {
                Log?.LogInfo("  EnemyManager: NOT FOUND");
            }

            // Pegs — use PegManager lists (not FindObjectsOfType which finds pre-instanced duplicates)
            var bc2 = UnityEngine.Object.FindObjectOfType<BattleController>();
            var pm = bc2?.pegManager;
            if (pm != null && pm.allPegs != null)
            {
                var bombsF = AccessTools.Field(typeof(BattleController).Assembly.GetType("Battle.PegManager"), "_bombs");
                var bombs = bombsF?.GetValue(pm) as System.Collections.Generic.List<Bomb>;

                var allPegObjects = new List<Peg>(pm.allPegs);
                if (bombs != null)
                {
                    foreach (var b in bombs)
                    {
                        allPegObjects.Add(b);
                    }
                }

                var activePegs = allPegObjects.Where(p => p != null && p.gameObject.activeSelf).ToArray();
                var pegTypes = activePegs.GroupBy(p => p.pegType).OrderByDescending(g => g.Count());
                Log?.LogInfo($"  Pegs: {activePegs.Length} active / {allPegObjects.Count} total (allPegs={pm.allPegs.Count}, bombs={bombs?.Count ?? 0})");
                foreach (var g in pegTypes)
                {
                    Log?.LogInfo($"    {g.Key}: {g.Count()}");
                }

                var sorted = activePegs.OrderBy(p => p.transform.position.y).ThenBy(p => p.transform.position.x).Take(10);
                Log?.LogInfo($"  First 10 pegs by pos:");
                foreach (var p in sorted)
                {
                    Log?.LogInfo($"    ({p.transform.position.x:F3},{p.transform.position.y:F3}) type={p.pegType}");
                }
            }
            else
            {
                Log?.LogInfo("  PegManager: NOT FOUND");
            }

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
