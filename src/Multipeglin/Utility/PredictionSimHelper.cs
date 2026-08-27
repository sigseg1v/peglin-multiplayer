using System;
using BepInEx.Logging;
using HarmonyLib;

namespace Multipeglin.Utility;

/// <summary>
/// Safe wrapper around <see cref="PredictionManager.CopyAllPegs"/>.
///
/// The game only ever calls CopyAllPegs once per board load, and the method is
/// not idempotent: <c>KillAllPegs()</c> destroys the previous clone's dummy
/// GameObjects and clears <c>_dummyPegs</c> / <c>_movingPegs</c> /
/// <c>_layerChangers</c> / <c>_movingPegsEndOfTurn</c>, but never
/// <c>_allPegs</c>. <c>CopyChildren</c> then registers a peg only when
/// <c>!_allPegs.ContainsKey(realPeg)</c>, so on a second call nothing is
/// registered and <c>_allPegs</c> keeps mapping the live pegs to the *previous*
/// clone's destroyed dummies.
///
/// Everything that reads that map afterwards — <c>UpdateAllPegsStatus</c>,
/// <c>DestroyPegInSimulation</c>, <c>CopyPegToSimulation</c> — then operates on
/// destroyed components, which is where the client's
/// "Prediction refresh failed: Object reference not set" warnings come from and
/// why the aimer stops bending after the first rebuild.
///
/// The client rebuilds the map more than once (board sync, nav arm,
/// post-battle), so clear the map first.
/// </summary>
public static class PredictionSimHelper
{
    private static readonly System.Reflection.FieldInfo AllPegsField
        = AccessTools.Field(typeof(PredictionManager), "_allPegs");

    /// <summary>
    /// Clear <c>_allPegs</c> and run a full <see cref="PredictionManager.CopyAllPegs"/>.
    /// Expensive — a deep Instantiate of the whole pegboard, five whole-scene
    /// interface scans and ~6 O(N²) id-matching loops (40-60 ms at 69 pegs,
    /// several hundred at 400+). Call it only when the peg *set* changed.
    /// </summary>
    public static void RebuildSimPegMap(PredictionManager prediction, ManualLogSource log = null)
    {
        if (prediction == null)
        {
            return;
        }

        try
        {
            if (AllPegsField?.GetValue(prediction) is System.Collections.IDictionary allPegs)
            {
                allPegs.Clear();
            }
        }
        catch (Exception ex)
        {
            log?.LogWarning($"[PredictionSim] Could not clear PredictionManager._allPegs: {ex.Message}");
        }

        prediction.CopyAllPegs();
    }
}
