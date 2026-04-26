using HarmonyLib;
using Multipeglin.GameState;
using PixelCrushers.DialogueSystem;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class StandardUIResponseButtonPatches
{
    /// <summary>
    /// Block response button clicks on client during TextScenario unless
    /// AllowTextScenarioLogic is set (client making its own dialogue choices).
    /// </summary>
    [HarmonyPatch(typeof(StandardUIResponseButton), "OnClick")]
    [HarmonyPrefix]
    public static bool StandardUIResponseButton_OnClick_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        if (AllowTextScenarioLogic)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked StandardUIResponseButton.OnClick (spectating)");
        return false;
    }

    // =========================================================================
    // HOST: TRACK DIALOGUE HOVER (which response button host is highlighting)
    // =========================================================================

    [HarmonyPatch(typeof(StandardUIResponseButton), "OnSelect")]
    [HarmonyPostfix]
    public static void StandardUIResponseButton_OnSelect_Postfix(StandardUIResponseButton __instance)
    {
        if (!IsHosting)
        {
            return;
        }

        try
        {
            // Find this button's index in its parent menu panel
            var dialogueUI = UnityEngine.Object.FindObjectOfType<StandardDialogueUI>();
            if (dialogueUI == null)
            {
                return;
            }

            var menuPanel = dialogueUI.conversationUIElements?.defaultMenuPanel;
            if (menuPanel?.buttons == null)
            {
                return;
            }

            for (var i = 0; i < menuPanel.buttons.Length; i++)
            {
                if (menuPanel.buttons[i] == __instance)
                {
                    TextScenarioHoverTracker.CurrentHoveredIndex = i;
                    return;
                }
            }
        }
        catch { }
    }
}
