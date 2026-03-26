using Battle;
using HarmonyLib;
using PeglinMods.Spectator.Spectator;

namespace PeglinMods.Spectator.Patches;

/// <summary>
/// Harmony patches applied on spectator clients to suppress game logic
/// that should only run on the host. Prevents the spectator from running
/// their own battle simulation, saving data, or processing input.
/// </summary>
public static class SpectatorClientPatches
{
    private static ISpectatorMode _spectatorMode;

    public static void Initialize(ISpectatorMode spectatorMode)
    {
        _spectatorMode = spectatorMode;
    }

    private static bool IsSpectating => _spectatorMode?.IsSpectating == true;

    /// <summary>
    /// Suppress BattleController.Update on spectator clients.
    /// The battle state machine should not advance locally; all state
    /// comes from the host via network events.
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "Update")]
    public static class SuppressBattleUpdate
    {
        public static bool Prefix()
        {
            return !IsSpectating;
        }
    }

    /// <summary>
    /// Suppress SaveRun on spectator clients.
    /// Spectators should not overwrite the player's local save data.
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), "SaveRun")]
    public static class SuppressSaveRun
    {
        public static bool Prefix()
        {
            return !IsSpectating;
        }
    }

    /// <summary>
    /// Suppress SaveBase on spectator clients.
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), "SaveBase")]
    public static class SuppressSaveBase
    {
        public static bool Prefix()
        {
            return !IsSpectating;
        }
    }

    /// <summary>
    /// Suppress GameInit.Start on spectator clients.
    /// Prevents the spectator from initializing a new run locally.
    /// </summary>
    [HarmonyPatch(typeof(GameInit), "Start")]
    public static class SuppressGameInit
    {
        public static bool Prefix()
        {
            return !IsSpectating;
        }
    }
}
