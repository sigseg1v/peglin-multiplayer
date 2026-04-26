using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class DeckInfoManagerPatches
{
    /// <summary>
    /// Block StartShuffleAnimation on client — prevents the plunger reload animation
    /// from playing every time onDeckShuffled fires. RebuildDeckInfoDisplay handles
    /// the deck tube visuals directly without animation.
    /// </summary>
    [HarmonyPatch(typeof(DeckInfoManager), "StartShuffleAnimation")]
    [HarmonyPrefix]
    public static bool DeckInfoManager_StartShuffleAnimation_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked StartShuffleAnimation on client — host controls deck visuals");
        return false;
    }
}
