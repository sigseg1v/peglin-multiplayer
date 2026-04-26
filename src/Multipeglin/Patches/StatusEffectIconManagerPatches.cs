using HarmonyLib;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class StatusEffectIconManagerPatches
{
    // =========================================================================
    // POST-BATTLE RELIC GRANT — each player picks INDEPENDENTLY
    //
    // Both host and client run BattleUpgradeCanvas.SetupRelicGrant against
    // their own RelicManager. Reward count and choices reflect each player's
    // own state (Eye of Turtle, Peglin Eye, etc). No host->client replication
    // here — that produced shared choices that ignored the client's own relics.
    // =========================================================================

    // =========================================================================
    // Status effect UI suppression during non-host turns in coop
    // =========================================================================

    /// <summary>
    /// Suppress status effect UI updates on the host when a non-host player's
    /// state is active. This prevents the host's Peglin from visually gaining
    /// Ballusion (or other status effects) from another player's relics during
    /// their turn.
    /// </summary>
    [HarmonyPatch(typeof(Battle.StatusEffects.StatusEffectIconManager), nameof(Battle.StatusEffects.StatusEffectIconManager.UpdateStatusEffects))]
    [HarmonyPrefix]
    public static bool StatusEffectIconManager_UpdateStatusEffects_Prefix()
    {
        if (!GameState.CoopStateManager.SuppressStatusEffectUI)
        {
            return true;
        }

        return false; // Skip the UI update — effects are still in the list for gameplay
    }
}
