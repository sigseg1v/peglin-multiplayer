using HarmonyLib;
using PixelCrushers.DialogueSystem;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class DialogueManagerPatches
{
    /// <summary>
    /// Allow StartConversation on client so the native Dialogue System UI renders
    /// with proper fonts, sizing, and layout. The client sees the same dialogue as
    /// the host. Response button clicks are blocked separately.
    /// </summary>
    [HarmonyPatch(typeof(DialogueManager), "StartConversation", typeof(string), typeof(UnityEngine.Transform), typeof(UnityEngine.Transform), typeof(int))]
    [HarmonyPrefix]
    public static bool DialogueManager_StartConversation_Prefix()
    {
        if (ShouldSuppressClientLogic)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] ALLOWING StartConversation on client (native dialogue UI)");
        }

        return true;
    }
}
