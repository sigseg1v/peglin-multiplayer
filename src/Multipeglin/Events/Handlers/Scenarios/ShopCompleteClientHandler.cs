using System;
using System.Linq;
using Battle.Attacks;
using Multipeglin.Events.Handlers.Coop;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Events.Network.Scenarios;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;
using Relics;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Scenarios;

/// <summary>
/// Client handler for ShopCompleteEvent: only the host processes this.
/// Updates the client's CoopPlayerState with purchased orbs/relics and gold.
/// </summary>
public sealed class ShopCompleteClientHandler : IClientHandler<ShopCompleteEvent>
{
    public void Handle(ShopCompleteEvent e)
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null) return;
            if (!services.TryResolve<IMultiplayerMode>(out var mode) || !mode.IsHosting) return;

            var eventRegistry = services.TryResolve<IGameEventRegistry>(out var reg) ? reg : null;
            var senderPeerId = (eventRegistry as GameEventRegistry)?.CurrentSenderPeerId ?? -1;

            if (!services.TryResolve<PlayerRegistry>(out var registry)) return;
            var slot = registry.GetSlotByPeerId(senderPeerId);
            if (slot == null)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[ShopComplete] From unknown peer {senderPeerId}");
                return;
            }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[ShopComplete] Player '{slot.PlayerName}' (slot {slot.SlotIndex}): " +
                $"{e.Purchases?.Count ?? 0} purchases, goldSpent={e.GoldSpent}, remaining={e.RemainingGold}");

            if (!services.TryResolve<CoopStateManager>(out var coopState)) return;
            var playerState = coopState.GetPlayerState(slot.SlotIndex);
            if (playerState == null)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[ShopComplete] No CoopPlayerState for slot {slot.SlotIndex}");
                return;
            }

            // NOTE: purchases have already been applied individually via per-purchase
            // ShopPurchaseEvents. This event is just the "done shopping" signal — used
            // only to reconcile the final gold value (in case of drift) and to trigger
            // the wait-for-all completion transition.
            if (e.RemainingGold > 0 || playerState.Gold != e.RemainingGold)
            {
                playerState.Gold = e.RemainingGold;
            }
            MultiplayerPlugin.Logger?.LogInfo(
                $"[ShopComplete] Finalized slot {slot.SlotIndex}: deck={playerState.CompleteDeck.Count}, " +
                $"relics={playerState.OwnedRelics.Count}, gold={playerState.Gold} (e.RemainingGold={e.RemainingGold})");

            // Track completion (HashSet — duplicate add is a no-op)
            CoopRewardState.ClientShopChoicesReceived.Add(slot.SlotIndex);

            // Check if all done — idempotent: only proceed once even if the
            // client spams ShopCompleteEvents (happens when the client's Exit
            // Store button is clicked repeatedly before the scene changes).
            if (CoopRewardState.HostShopDone
                && CoopRewardState.AllClientShopChoicesReceived
                && !CoopRewardState.ShopCompletionProceeded)
            {
                CoopRewardState.ShopCompletionProceeded = true;
                MultiplayerPlugin.Logger?.LogInfo("[ShopComplete] All players finished shopping — proceeding");
                CoopRewardState.WaitingForOtherPlayers = false;
                CoopRewardState.ShopPhaseActive = false;

                if (services.TryResolve<IGameEventRegistry>(out var evtReg))
                    evtReg.Dispatch(new AllChoicesCompleteEvent { Phase = "shop" });

                // Resume host's blocked CloseStore
                if (CoopRewardState.PendingShopManager is global::Scenarios.Shop.ShopManager shopMgr)
                {
                    CoopRewardState.PendingShopManager = null;
                    MultiplayerPlugin.Logger?.LogInfo("[ShopComplete] Resuming host CloseStore");
                    shopMgr.CloseStore();
                }
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ShopComplete] Handler failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void ApplyOrbPurchase(CoopPlayerState playerState, ShopPurchase purchase)
    {
        try
        {
            // Find the orb prefab by name
            var allAttacks = Resources.FindObjectsOfTypeAll<Attack>();
            foreach (var attack in allAttacks)
            {
                var go = attack.gameObject;
                if (go.scene.name == null && go.name == purchase.Name)
                {
                    playerState.CompleteDeck.Add(new SerializedOrb
                    {
                        PrefabName = purchase.Name,
                        Guid = Guid.NewGuid().ToString(),
                        Level = attack.Level,
                    });
                    MultiplayerPlugin.Logger?.LogInfo($"[ShopComplete] Added orb '{purchase.Name}' to deck");
                    return;
                }
            }

            // Fallback: add with default level
            playerState.CompleteDeck.Add(new SerializedOrb
            {
                PrefabName = purchase.Name,
                Guid = Guid.NewGuid().ToString(),
                Level = 1,
            });
            MultiplayerPlugin.Logger?.LogInfo($"[ShopComplete] Added orb '{purchase.Name}' (fallback, level=1)");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ShopComplete] ApplyOrbPurchase failed: {ex.Message}");
        }
    }

    private void ApplyRelicPurchase(CoopPlayerState playerState, ShopPurchase purchase)
    {
        try
        {
            if (purchase.RelicEffect < 0) return;

            var allRelics = Resources.FindObjectsOfTypeAll<Relics.Relic>();
            foreach (var relic in allRelics)
            {
                if ((int)relic.effect == purchase.RelicEffect)
                {
                    playerState.OwnedRelics.Add(new SerializedRelic
                    {
                        Effect = purchase.RelicEffect,
                        LocKey = relic.locKey,
                        Rarity = (int)relic.globalRarity,
                    });
                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[ShopComplete] Added relic '{relic.locKey}' (effect={purchase.RelicEffect}) to player state");
                    return;
                }
            }

            MultiplayerPlugin.Logger?.LogWarning(
                $"[ShopComplete] Relic with effect={purchase.RelicEffect} not found");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ShopComplete] ApplyRelicPurchase failed: {ex.Message}");
        }
    }
}
