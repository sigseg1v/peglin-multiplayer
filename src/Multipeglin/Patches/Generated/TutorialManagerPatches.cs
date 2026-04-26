using HarmonyLib;
using Multipeglin.Multiplayer;
using Tutorial;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class TutorialManagerPatches
{
    // =========================================================================
    // DISABLE TUTORIAL IN MULTIPLAYER — both host and client
    // =========================================================================

    /// <summary>
    /// Disable tutorial popups for both host and client in multiplayer.
    /// Tutorials block gameplay and don't make sense in a multiplayer context.
    /// </summary>
    [HarmonyPatch(typeof(TutorialManager), "ShouldPopupTutorial")]
    [HarmonyPrefix]
    public static bool TutorialManager_ShouldPopupTutorial_Prefix(ref bool __result)
    {
        if (MultiplayerPlugin.Services == null)
        {
            return true;
        }

        if (!MultiplayerPlugin.Services.TryResolve<IMultiplayerMode>(out var mode))
        {
            return true;
        }

        if (!mode.IsHosting && !mode.IsSpectating)
        {
            return true;
        }

        __result = false;
        return false;
    }
}
