using HarmonyLib;
using Multipeglin.Multiplayer;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class PlayButtonPatches
{
    // =========================================================================
    // SKIP CHARACTER SELECT IN MULTIPLAYER — class already chosen in lobby
    // =========================================================================

    /// <summary>
    /// When in a multiplayer session, skip the character select screen entirely.
    /// The host calls PlayButton.MovetoCharacterSelect() which eventually shows
    /// the class select UI. We intercept this to call ConfirmRunConfigAndStartGame()
    /// directly, since the class was already chosen in the lobby.
    /// </summary>
    [HarmonyPatch(typeof(PeglinUI.MainMenu.PlayButton), "SwitchToRunConfigCanvas")]
    [HarmonyPrefix]
    public static bool PlayButton_SwitchToRunConfigCanvas_Prefix(PeglinUI.MainMenu.PlayButton __instance)
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

        // In multiplayer, skip character select and go straight to game start
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Skipping character select — class chosen in lobby");

        // Set StartingOrbs and StartingRelics from the chosen class's ClassLoadoutData.
        // Normally LoadoutManager.SetupDataForNewGame() does this, but we skip that UI entirely.
        SetStartingLoadoutFromClass(StaticGameData.chosenClass);

        __instance.ConfirmRunConfigAndStartGame();
        return false; // Skip the normal SwitchToRunConfigCanvas
    }
}
