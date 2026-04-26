using System;
using Multipeglin.Events.Network.Coop;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Client handler for RelicChoiceEvent: only the host processes this
/// (a client's relic selection arriving at the host).
/// Applies the relic to the player's CoopPlayerState and checks if all
/// players have finished choosing so the game can proceed to the map.
/// </summary>
public sealed class RelicChoiceClientHandler : IClientHandler<RelicChoiceEvent>
{
    public void Handle(RelicChoiceEvent networkEvent)
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null)
            {
                return;
            }

            if (!services.TryResolve<IMultiplayerMode>(out var mode) || !mode.IsHosting)
            {
                return;
            }

            var eventRegistry = services.TryResolve<IGameEventRegistry>(out var reg) ? reg : null;
            var senderPeerId = (eventRegistry as GameEventRegistry)?.CurrentSenderPeerId ?? -1;

            if (!services.TryResolve<PlayerRegistry>(out var registry))
            {
                return;
            }

            var slot = registry.GetSlotByPeerId(senderPeerId);
            if (slot == null)
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[CoopReward] RelicChoice from unknown peer {senderPeerId}");
                return;
            }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[CoopReward] Player '{slot.PlayerName}' (slot {slot.SlotIndex}) chose relic effect {networkEvent.ChosenRelicEffect}");

            // Apply the relic to the player's CoopPlayerState
            if (services.TryResolve<CoopStateManager>(out var coopState))
            {
                var playerState = coopState.GetPlayerState(slot.SlotIndex);
                if (playerState != null)
                {
                    // Find the relic data to get display info — can't use CommonRelicPool
                    // because GetMultipleRelicsOffOfQueue already dequeued relics from the pool.
                    var locKey = string.Empty;
                    var rarity = 0;
                    var allRelics = Resources.FindObjectsOfTypeAll<Relics.Relic>();
                    foreach (var r in allRelics)
                    {
                        if ((int)r.effect == networkEvent.ChosenRelicEffect)
                        {
                            locKey = r.locKey;
                            rarity = (int)r.globalRarity;
                            break;
                        }
                    }

                    playerState.OwnedRelics.Add(new SerializedRelic
                    {
                        Effect = networkEvent.ChosenRelicEffect,
                        LocKey = locKey,
                        Rarity = rarity,
                    });
                    MultiplayerPlugin.Logger?.LogInfo($"[CoopReward] Added relic {networkEvent.ChosenRelicEffect} to slot {slot.SlotIndex} state");

                    // Apply stat effects for health-modifying relics.
                    // The game's native RelicManager.AddRelic only modifies singletons
                    // (which belong to the active player), so non-active players need
                    // their CoopPlayerState updated directly.
                    ApplyRelicStatEffects(playerState, networkEvent.ChosenRelicEffect);
                }
            }

            // Track this client's choice
            CoopRewardState.ClientRelicChoicesReceived.Add(slot.SlotIndex);

            // Check if all players have now chosen
            if (CoopRewardState.HostHasChosenRelic && CoopRewardState.AllClientRelicChoicesReceived)
            {
                CoopRewardState.HostRelicSelectionActive = false;
                CoopRewardState.WaitingForOtherPlayers = false;

                var gameInit = CoopRewardState.PendingGameInitInstance as GameInit;
                var phase = gameInit != null ? "starting_relic" : "treasure";

                MultiplayerPlugin.Logger?.LogInfo($"[CoopReward] All relic choices received (phase={phase})");

                // Dispatch AllChoicesCompleteEvent to clients
                if (services.TryResolve<IGameEventRegistry>(out var reg2))
                {
                    reg2.Dispatch(new AllChoicesCompleteEvent { Phase = phase });
                }

                // For starting_relic: call LoadMapScene to proceed from GameInit
                // For treasure: host's native chest flow handles scene transition
                if (gameInit != null)
                {
                    var loadMapMethod = typeof(GameInit).GetMethod("LoadMapScene",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    loadMapMethod?.Invoke(gameInit, null);
                }
            }
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopReward] RelicChoice handler failed: {e.Message}");
        }
    }

    /// <summary>
    /// Public entry point for applying relic stat effects from other classes
    /// (e.g., CoopStateManager.AssignTreasureRelicsToNonHostPlayers).
    /// </summary>
    public static void ApplyRelicStatEffectsStatic(
        CoopPlayerState playerState,
        int relicEffect,
        BepInEx.Logging.ManualLogSource log = null)
    {
        ApplyRelicStatEffects(playerState, relicEffect);
    }

    /// <summary>
    /// Apply on-add stat modifications for relics. Mirrors the switch in
    /// RelicManager.AddRelic — only the 4 relics that mutate state at
    /// acquisition time need handling here; all other relics are passive
    /// and work automatically via AttemptUseRelic at runtime.
    /// </summary>
    private static void ApplyRelicStatEffects(CoopPlayerState playerState, int relicEffect)
    {
        // Check if the player has the INCREASE_MAX_HP_GAIN relic (effect 106)
        var hasHpGainBoost = false;
        foreach (var r in playerState.OwnedRelics)
        {
            if (r.Effect == 106) // INCREASE_MAX_HP_GAIN
            {
                hasHpGainBoost = true;
                break;
            }
        }

        var hpBonus = 0f;
        switch (relicEffect)
        {
            case 23: // MAX_HEALTH_SMALL (+15)
                hpBonus = 15f;
                break;
            case 24: // MAX_HEALTH_MEDIUM (+25)
                hpBonus = 25f;
                break;
            case 74: // MAX_HEALTH_LARGE (+50)
                hpBonus = 50f;
                break;
            case 88: // ADD_ORBS_AND_UPGRADE (Haglin's Satchel: +orbs, +upgrades, +100g)
                ApplyHaglinsSatchel(playerState);
                return;
        }

        if (hpBonus <= 0f)
        {
            return;
        }

        if (hasHpGainBoost)
        {
            hpBonus += 1f;
        }

        var beforeMax = playerState.MaxHealth;
        var beforeCur = playerState.CurrentHealth;
        playerState.MaxHealth += hpBonus;
        playerState.CurrentHealth += hpBonus;
        MultiplayerPlugin.Logger?.LogInfo(
            $"[CoopReward] Relic HP effect={relicEffect}: slot {playerState.SlotIndex} " +
            $"MaxHP {beforeMax}->{playerState.MaxHealth}, HP {beforeCur}->{playerState.CurrentHealth}" +
            (hasHpGainBoost ? " (+1 from INCREASE_MAX_HP_GAIN)" : string.Empty));
    }

    /// <summary>
    /// Apply Haglin's Satchel: add 2 random orbs, upgrade 3 random orbs, +100 gold.
    /// We swap to the player's state so DeckManager operates on their deck,
    /// call the native RelicModifyDeck, then save back.
    /// </summary>
    private static void ApplyHaglinsSatchel(CoopPlayerState playerState)
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null || !services.TryResolve<CoopStateManager>(out var coopState))
            {
                return;
            }

            var relic = FindRelicByEffect(88);
            if (relic == null)
            {
                MultiplayerPlugin.Logger?.LogWarning("[CoopReward] Haglin's Satchel: could not find Relic asset");
                playerState.Gold += 100;
                return;
            }

            // Swap to this player so DeckManager singletons point at their deck
            var previousSlot = coopState.ActivePlayerSlot;
            coopState.SwapToPlayer(playerState.SlotIndex);

            // Run the native deck modification (adds orbs + upgrades)
            var dm = Resources.FindObjectsOfTypeAll<DeckManager>();
            if (dm != null && dm.Length > 0)
            {
                dm[0].RelicModifyDeck(relic);
            }

            // +100 gold
            playerState.Gold += 100;

            // Save the modified deck back to CoopPlayerState
            coopState.SaveActivePlayerState();

            // Swap back to whoever was active before
            if (previousSlot != playerState.SlotIndex)
            {
                coopState.SwapToPlayer(previousSlot);
            }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[CoopReward] Haglin's Satchel applied to slot {playerState.SlotIndex}: " +
                $"deck={playerState.CompleteDeck?.Count ?? 0} orbs, gold +100 (now {playerState.Gold})");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopReward] Haglin's Satchel failed: {ex.Message}");
            // At minimum give them the gold
            playerState.Gold += 100;
        }
    }

    private static Relics.Relic FindRelicByEffect(int effect)
    {
        var allRelics = Resources.FindObjectsOfTypeAll<Relics.Relic>();
        foreach (var r in allRelics)
        {
            if ((int)r.effect == effect)
            {
                return r;
            }
        }

        return null;
    }
}
