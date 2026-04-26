using HarmonyLib;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class SteamManagerPatches
{
    // --- SteamManager: skip Steam init in dev-multi to allow multiple instances ---

    [HarmonyPatch(typeof(SteamManager), "Awake")]
    [HarmonyPrefix]
    public static bool SteamManager_Awake_Prefix(SteamManager __instance)
    {
        if (System.Environment.GetEnvironmentVariable("SKIP_STEAM_INIT") != "1")
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] SKIP_STEAM_INIT set, skipping SteamManager.Awake");

        // Set the singleton so Initialized returns false without creating new GameObjects
        var field = typeof(SteamManager).GetField("s_instance",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        field?.SetValue(null, __instance);

        UnityEngine.Object.DontDestroyOnLoad(__instance.gameObject);
        return false;
    }
}
