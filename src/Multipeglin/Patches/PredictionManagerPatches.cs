using System.Collections.Generic;
using Battle;
using Battle.PegBehaviour;
using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class PredictionManagerPatches
{
    // =========================================================================
    // CLIENT: SCRUB DEAD PEGS FROM _allPegs BEFORE UpdateAllPegsStatus
    // =========================================================================

    /// <summary>
    /// PredictionManager._allPegs is a dictionary of (real peg → simulation peg).
    /// On the client, real pegs (especially Bombs added via PegboardApplier
    /// HandlePegAdded) get destroyed/replaced by host snapshots, but the dict
    /// keeps the dangling key references. Iterating those keys via
    /// UpdateAllPegsStatus calls GetPegStatus()→base.gameObject.activeInHierarchy
    /// on a destroyed MonoBehaviour, which throws NullReferenceException.
    ///
    /// PachinkoBall.Arm() calls UpdateAllPegsStatus on every aim — when this
    /// throws, the prediction line renderer is never enabled and the dotted
    /// aimer disappears for the rest of the client's turn.
    ///
    /// Fix: before each iteration, remove any entry whose key is a destroyed
    /// Unity object (the `== null` overload Unity provides catches both real
    /// null and "alive C# ref to destroyed object").
    /// </summary>
    [HarmonyPatch(typeof(PredictionManager), nameof(PredictionManager.UpdateAllPegsStatus))]
    [HarmonyPrefix]
    public static void PredictionManager_UpdateAllPegsStatus_Prefix(PredictionManager __instance)
    {
        try
        {
            var allPegsField = AccessTools.Field(typeof(PredictionManager), "_allPegs");
            if (allPegsField?.GetValue(__instance) is IDictionary<IDummyPeg, IDummyPeg> allPegs && allPegs.Count > 0)
            {
                List<IDummyPeg> toRemove = null;
                foreach (var kvp in allPegs)
                {
                    if (IsDestroyed(kvp.Key) || IsDestroyed(kvp.Value))
                    {
                        (toRemove ??= new List<IDummyPeg>()).Add(kvp.Key);
                    }
                }

                if (toRemove != null)
                {
                    foreach (var k in toRemove)
                    {
                        allPegs.Remove(k);
                    }
                }
            }

            var movingField = AccessTools.Field(typeof(PredictionManager), "_movingPegsEndOfTurn");
            if (movingField?.GetValue(__instance) is IDictionary<IDummiableMovingPegEndOfTurn, IDummiableMovingPegEndOfTurn> movingPegs && movingPegs.Count > 0)
            {
                List<IDummiableMovingPegEndOfTurn> toRemove = null;
                foreach (var kvp in movingPegs)
                {
                    if (IsDestroyedMover(kvp.Key) || IsDestroyedMover(kvp.Value))
                    {
                        (toRemove ??= new List<IDummiableMovingPegEndOfTurn>()).Add(kvp.Key);
                    }
                }

                if (toRemove != null)
                {
                    foreach (var k in toRemove)
                    {
                        movingPegs.Remove(k);
                    }
                }
            }
        }
        catch
        {
            // best-effort scrub — don't block the original method
        }
    }

    private static bool IsDestroyed(IDummyPeg p)
    {
        if (ReferenceEquals(p, null))
        {
            return true;
        }

        // Use Unity's overloaded == against UnityEngine.Object to detect destroyed-but-not-null.
        return p is UnityEngine.Object uo && uo == null;
    }

    private static bool IsDestroyedMover(IDummiableMovingPegEndOfTurn p)
    {
        if (ReferenceEquals(p, null))
        {
            return true;
        }

        return p is UnityEngine.Object uo && uo == null;
    }

    // =========================================================================
    // HOST: SUPPRESS PREDICTION TRAJECTORY DURING CLIENT TURNS
    // =========================================================================

    /// <summary>
    /// When it's a client's turn in coop, prevent PredictionManager from rendering
    /// the host's dotted trajectory. PachinkoBall.Arm() re-enables the line renderer
    /// when the next ball enters AIMING state, and Update() keeps calling Predict().
    /// Block both to keep the host screen clean during client turns.
    /// </summary>
    [HarmonyPatch(typeof(PredictionManager), nameof(PredictionManager.Predict))]
    [HarmonyPrefix]
    public static bool PredictionManager_Predict_Prefix()
    {
        if (!IsHosting)
        {
            return true;
        }

        if (!UI.LobbyUI.GameStartReceived)
        {
            return true;
        }

        if (Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn)
        {
            return true;
        }
        // Allow prediction during non-battle phases (navigation, post-battle, map).
        // The turn system only tracks battle turns — outside active combat the host
        // should always see the aimer (e.g. navigation orb after victory).
        var state = BattleController.CurrentBattleState;
        if (state == BattleController.BattleState.NAVIGATION
            || state == BattleController.BattleState.AWAITING_POST_BATTLE_CONTROLLER
            || state == BattleController.BattleState.NAVIGATION_COMPLETE)
        {
            return true;
        }

        return false; // Not host's turn — suppress prediction
    }

    [HarmonyPatch(typeof(PredictionManager), nameof(PredictionManager.SetLineRendererStatus))]
    [HarmonyPrefix]
    public static bool PredictionManager_SetLineRendererStatus_Prefix(bool status)
    {
        if (!status)
        {
            return true; // Always allow disabling
        }

        if (!IsHosting)
        {
            return true;
        }

        if (!UI.LobbyUI.GameStartReceived)
        {
            return true;
        }

        if (Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn)
        {
            return true;
        }

        var state = BattleController.CurrentBattleState;
        if (state == BattleController.BattleState.NAVIGATION
            || state == BattleController.BattleState.AWAITING_POST_BATTLE_CONTROLLER
            || state == BattleController.BattleState.NAVIGATION_COMPLETE)
        {
            return true;
        }

        return false; // Not host's turn — don't re-enable prediction line
    }
}
