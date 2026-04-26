using System;
using HarmonyLib;
using UnityEngine;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class DeckManagerPatches
{
    /// <summary>
    /// Block DrawBall on client — it NREs on subsequent calls because
    /// BattleController's state machine isn't advancing (Update blocked).
    /// BallUsedClientHandler manually handles deck pop and UI animation.
    /// In coop mode, allow deck operations during the client's own turn.
    /// </summary>
    [HarmonyPatch(typeof(DeckManager), "DrawBall")]
    [HarmonyPrefix]
    public static bool DeckManager_DrawBall_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }
        // In coop, allow deck operations during client's turn
        if (UI.LobbyUI.GameStartReceived && Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Block the battle deck reshuffle on client — prevents reload animation spam.
    /// The initial ShuffleCompleteDeck (at battle start) is allowed for UI setup.
    /// ShuffleBattleDeck fires during reload (deck empty) and triggers the plunger
    /// animation loop. The host sends the correct deck order via SyncDeck.
    /// </summary>
    [HarmonyPatch(typeof(DeckManager), "ShuffleBattleDeck")]
    [HarmonyPrefix]
    public static bool DeckManager_ShuffleBattleDeck_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }
        // In coop, allow deck operations during client's turn
        if (UI.LobbyUI.GameStartReceived && Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn)
        {
            return true;
        }

        return false;
    }

    // =========================================================================
    // HOST: TEXT SCENARIO REWARD SHARING — share host rewards to all coop players
    // =========================================================================

    /// <summary>
    /// When the host upgrades an orb during a TextScenario (forge, etc.),
    /// upgrade a random upgradeable orb for each non-host coop player.
    /// </summary>
    [HarmonyPatch(typeof(DeckManager), "UpgradeSpecificOrb")]
    [HarmonyPostfix]
    public static void DeckManager_UpgradeSpecificOrb_SharePostfix(GameObject toUpgrade, GameObject __result)
    {
        if (!IsHosting)
        {
            return;
        }

        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "TextScenario")
        {
            return;
        }

        if (__result == null)
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
            // Save host state first so the upgrade is captured
            coopState.SaveActivePlayerState();

            foreach (var kvp in coopState.PlayerStates)
            {
                if (kvp.Key == coopState.ActivePlayerSlot)
                {
                    continue;
                }

                // Find upgradeable orbs in this player's deck
                var upgradeableIndices = new System.Collections.Generic.List<int>();
                for (var i = 0; i < kvp.Value.CompleteDeck.Count; i++)
                {
                    var orb = kvp.Value.CompleteDeck[i];
                    try
                    {
                        var prefab = Loading.AssetLoading.Instance?.GetOrbPrefab(orb.PrefabName);
                        if (prefab != null)
                        {
                            var attack = prefab.GetComponent<Battle.Attacks.Attack>();
                            if (attack?.NextLevelPrefab != null)
                            {
                                upgradeableIndices.Add(i);
                            }
                        }
                    }
                    catch { }
                }

                if (upgradeableIndices.Count == 0)
                {
                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[CoopReward] TextScenario: slot {kvp.Key} has no upgradeable orbs");
                    continue;
                }

                var idx = upgradeableIndices[UnityEngine.Random.Range(0, upgradeableIndices.Count)];
                var orbToUpgrade = kvp.Value.CompleteDeck[idx];
                try
                {
                    var orbPrefab = Loading.AssetLoading.Instance?.GetOrbPrefab(orbToUpgrade.PrefabName);
                    var nextLevel = orbPrefab?.GetComponent<Battle.Attacks.Attack>()?.NextLevelPrefab;
                    if (nextLevel != null)
                    {
                        var newName = nextLevel.name.Replace("(Clone)", string.Empty).Trim();
                        var newLevel = nextLevel.GetComponent<Battle.Attacks.Attack>()?.Level ?? (orbToUpgrade.Level + 1);
                        kvp.Value.CompleteDeck[idx] = new GameState.SerializedOrb
                        {
                            PrefabName = newName,
                            Guid = orbToUpgrade.Guid,
                            Level = newLevel,
                        };
                        MultiplayerPlugin.Logger?.LogInfo(
                            $"[CoopReward] TextScenario: upgraded orb '{orbToUpgrade.PrefabName}' → '{newName}' for slot {kvp.Key}");
                    }
                }
                catch (Exception ex)
                {
                    MultiplayerPlugin.Logger?.LogWarning(
                        $"[CoopReward] TextScenario: orb upgrade failed for slot {kvp.Key}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopReward] TextScenario orb upgrade sharing failed: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(DeckManager), "ShuffleCompleteDeck")]
    [HarmonyPrefix]
    public static bool DeckManager_ShuffleCompleteDeck_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        if (UI.LobbyUI.GameStartReceived && Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn)
        {
            return true;
        }
        // TextScenario dialogues (Mirror Duplicate, Helpful Spirits, etc.) call
        // DeckManager.DialoguePopulate* helpers that internally call ShuffleCompleteDeck
        // to pick which orbs to offer. Blocking it here leaves shuffledDeck empty,
        // the dialogue's randomOrbN variables never populate, and the UI softlocks
        // because no orb-choice buttons appear.
        if (AllowTextScenarioLogic)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked DeckManager.ShuffleCompleteDeck on client");
        return false;
    }

    [HarmonyPatch(typeof(DeckManager), "AddOrbToDeck")]
    [HarmonyPrefix]
    public static bool DeckManager_AddOrbToDeck_Prefix()
    {
        if (!ShouldSuppressClientLogic)
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

        if (AllowPegMinigameLogic)
        {
            return true;
        }

        if (AllowTextScenarioLogic)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked DeckManager.AddOrbToDeck on client");
        return false;
    }

    [HarmonyPatch(typeof(DeckManager), "RemoveOrbFromBattleDeck")]
    [HarmonyPrefix]
    public static bool DeckManager_RemoveOrbFromBattleDeck_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked DeckManager.RemoveOrbFromBattleDeck on client");
        return false;
    }
}
