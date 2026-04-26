using HarmonyLib;
using Multipeglin.Multiplayer;

namespace Multipeglin.Patches;

/// <summary>
/// In coop multiplayer, treat Act 4 ("The Core") as a mandatory continuation of Act 3.
/// After defeating the Act 3 boss, skip the "go to core / end run" choice UI and the
/// fake mines win cinematic, and proceed directly into the post-battle reward flow with
/// the Core scene queued. Once rewards are picked, all players continue to The Core.
/// </summary>
[HarmonyPatch]
internal static class CoopAct4ContinuePatch
{
    private static bool IsMultiplayer
    {
        get
        {
            if (MultiplayerPlugin.Services == null)
            {
                return false;
            }

            if (!MultiplayerPlugin.Services.TryResolve<IMultiplayerMode>(out var mode))
            {
                return false;
            }

            return mode.IsHosting || mode.IsSpectating;
        }
    }

    // Force Act 4 to be considered unlocked in multiplayer regardless of the local
    // client's per-account challenge progress. Without this, BattleController.CompleteVictory
    // skips the core branch entirely for any client that hasn't unlocked Act 4 yet.
    [HarmonyPatch(typeof(Challenges.ChallengeManager), nameof(Challenges.ChallengeManager.IsAct4Unlocked))]
    [HarmonyPostfix]
    private static void ForceAct4Unlocked(ref bool __result)
    {
        if (IsMultiplayer)
        {
            __result = true;
        }
    }

    // The vanilla Act 3 boss flow shows a "fake mines win" cinematic and a choice UI
    // (continue to Core vs end run) when showGoToCoreOption is true. In multiplayer
    // we cancel that flow and directly press the "Go To Core" button so all players
    // proceed into rewards with the Core scene queued.
    [HarmonyPatch(typeof(Battle.PostBattleController), "OnEnable")]
    [HarmonyPostfix]
    private static void AutoContinueToCore(Battle.PostBattleController __instance)
    {
        if (!IsMultiplayer)
        {
            return;
        }

        if (!__instance.showGoToCoreOption)
        {
            return;
        }

        try
        {
            __instance.StopAllCoroutines();
            __instance.showGoToCoreOption = false;
            __instance.GoToCoreButtonPressed();
            MultiplayerPlugin.Logger?.LogInfo("[CoopAct4] Skipped Act 3 -> Core choice; auto-continuing to The Core");
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopAct4] Auto-continue failed: {ex.Message}");
        }
    }
}
