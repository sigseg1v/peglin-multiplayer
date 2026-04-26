using Battle;
using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class PredictionManagerPatches
{
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
