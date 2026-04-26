using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class SpeedupIndicatorPatches
{
    /// <summary>
    /// Hide the key binding label ("F") on the speedup indicator for client.
    /// Keeps the arrow icon and speed text (e.g., "x2") visible.
    /// </summary>
    [HarmonyPatch(typeof(PeglinUI.SpeedupIndicator), "Start")]
    [HarmonyPostfix]
    public static void SpeedupIndicator_Start_Postfix(PeglinUI.SpeedupIndicator __instance)
    {
        if (!ShouldSuppressClientLogic)
        {
            return;
        }

        // The SpeedupIndicator Image shows the arrow icon — keep it.
        // Find and hide the key prompt child (the "F" label).
        // The key prompt is typically a child with a text or image showing the keybind.
        foreach (var img in __instance.GetComponentsInChildren<UnityEngine.UI.Image>(true))
        {
            // Skip the main indicator image (the arrow)
            if (img.gameObject == __instance.gameObject)
            {
                continue;
            }
            // Skip the speed text's parent
            if (img.GetComponentInChildren<TMPro.TextMeshProUGUI>() == __instance.Text)
            {
                continue;
            }
            // Disable other child images (key prompt icon)
            img.gameObject.SetActive(false);
        }
    }
}
