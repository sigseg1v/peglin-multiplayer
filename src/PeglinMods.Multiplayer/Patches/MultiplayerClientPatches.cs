using System;
using Battle;
using HarmonyLib;
using Loading;
using PeglinMods.Multiplayer.Multiplayer;
using UnityEngine.SceneManagement;

namespace PeglinMods.Multiplayer.Patches;

[HarmonyPatch]
public static class MultiplayerClientPatches
{
    /// <summary>
    /// When true, the next PeglinSceneLoader.LoadScene call is allowed through
    /// even on a suppressed client. MapStateApplier sets this before triggering
    /// a host-directed scene change.
    /// </summary>
    internal static bool AllowNextSceneLoad;

    /// <summary>
    /// Returns true when the client should NOT run its own game logic.
    /// Only true when actively connected as a spectating client.
    /// </summary>
    private static bool ShouldSuppressClientLogic
    {
        get
        {
            if (MultiplayerPlugin.Services == null) return false;
            if (!MultiplayerPlugin.Services.TryResolve<IMultiplayerMode>(out var mode)) return false;
            return mode.IsSpectating;
        }
    }

    // --- Battle & save suppression ---

    [HarmonyPatch(typeof(BattleController), "Update")]
    [HarmonyPrefix]
    public static bool BattleController_Update_Prefix() => !ShouldSuppressClientLogic;

    [HarmonyPatch(typeof(SaveManager), "SaveRun")]
    [HarmonyPrefix]
    public static bool SaveManager_SaveRun_Prefix() => !ShouldSuppressClientLogic;

    [HarmonyPatch(typeof(SaveManager), "SaveBase")]
    [HarmonyPrefix]
    public static bool SaveManager_SaveBase_Prefix() => !ShouldSuppressClientLogic;

    [HarmonyPatch(typeof(GameInit), "Start")]
    [HarmonyPrefix]
    public static bool GameInit_Start_Prefix() => !ShouldSuppressClientLogic;

    // --- Scene transition blocking ---
    // Block ALL client-initiated scene loads. Only MapStateApplier can trigger
    // scene changes on the client by setting AllowNextSceneLoad = true first.

    [HarmonyPatch(typeof(PeglinSceneLoader), "LoadScene",
        new Type[] { typeof(PeglinSceneLoader.Scene), typeof(LoadSceneMode), typeof(bool), typeof(float) })]
    [HarmonyPrefix]
    public static bool PeglinSceneLoader_LoadScene_Prefix(PeglinSceneLoader.Scene scene)
    {
        if (!ShouldSuppressClientLogic) return true;
        if (AllowNextSceneLoad)
        {
            AllowNextSceneLoad = false;
            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Allowing host-directed scene load: {scene}");
            return true;
        }
        MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Blocked client scene load: {scene}");
        return false;
    }

    [HarmonyPatch(typeof(PeglinSceneLoader), "LoadScene",
        new Type[] { typeof(int), typeof(LoadSceneMode) })]
    [HarmonyPrefix]
    public static bool PeglinSceneLoader_LoadSceneInt_Prefix(int sceneId)
    {
        if (!ShouldSuppressClientLogic) return true;
        if (AllowNextSceneLoad)
        {
            AllowNextSceneLoad = false;
            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Allowing host-directed scene load (int): {sceneId}");
            return true;
        }
        MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Blocked client scene load (int): {sceneId}");
        return false;
    }
}
