using System;
using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class RelicManagerPatches
{
    /// <summary>
    /// When the host gains a relic during a TextScenario (forge, etc.),
    /// add the same relic to each non-host coop player's state.
    /// </summary>
    [HarmonyPatch(typeof(Relics.RelicManager), "AddRelic")]
    [HarmonyPostfix]
    public static void RelicManager_AddRelic_SharePostfix(Relics.Relic relic)
    {
        if (!IsHosting)
        {
            return;
        }

        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "TextScenario")
        {
            return;
        }

        if (relic == null)
        {
            return;
        }
        // When TextScenarioPhaseActive, clients handle their own dialogue — don't double-apply
        if (Events.Handlers.Coop.CoopRewardState.TextScenarioPhaseActive)
        {
            return;
        }

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<GameState.CoopStateManager>(out var coopState) != true)
        {
            return;
        }

        if (coopState.TotalPlayerCount < 2)
        {
            return;
        }

        try
        {
            // Save host state first so the relic is captured
            coopState.SaveActivePlayerState();

            foreach (var kvp in coopState.PlayerStates)
            {
                if (kvp.Key == coopState.ActivePlayerSlot)
                {
                    continue;
                }

                // Check if player already has this relic
                var alreadyOwns = false;
                foreach (var r in kvp.Value.OwnedRelics)
                {
                    if (r.Effect == (int)relic.effect)
                    {
                        alreadyOwns = true;
                        break;
                    }
                }

                if (alreadyOwns)
                {
                    continue;
                }

                kvp.Value.OwnedRelics.Add(new GameState.SerializedRelic
                {
                    Effect = (int)relic.effect,
                    LocKey = relic.locKey ?? string.Empty,
                    Rarity = (int)relic.globalRarity,
                });
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[CoopReward] TextScenario: added relic '{relic.locKey}' (effect={relic.effect}) to slot {kvp.Key}");
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopReward] TextScenario relic sharing failed: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(Relics.RelicManager), "AddRelic")]
    [HarmonyPrefix]
    public static bool RelicManager_AddRelic_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        if (AllowRelicSync)
        {
            return true;
        }

        if (AllowNativeRewardLogic)
        {
            return true;
        }

        if (AllowShopLogic)
        {
            return true;
        }

        if (AllowTreasureLogic)
        {
            return true;
        }

        if (AllowPegMinigameLogic)
        {
            return true;
        }

        if (AllowTextScenarioLogic)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked RelicManager.AddRelic on client");
        return false;
    }

    [HarmonyPatch(typeof(Relics.RelicManager), "RemoveRelic", new[] { typeof(Relics.Relic) })]
    [HarmonyPrefix]
    public static bool RelicManager_RemoveRelic_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        if (AllowRelicSync)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked RelicManager.RemoveRelic on client");
        return false;
    }

    [HarmonyPatch(typeof(Relics.RelicManager), "RemoveRelic", new[] { typeof(Relics.RelicEffect) })]
    [HarmonyPrefix]
    public static bool RelicManager_RemoveRelicByEffect_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        if (AllowRelicSync)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked RelicManager.RemoveRelic(RelicEffect) on client");
        return false;
    }
}
