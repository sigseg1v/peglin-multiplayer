using System;
using Battle.Attacks;
using Multipeglin.Events.Network.Scenarios;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Scenarios;

/// <summary>
/// Runs on the host when a client sends a ShopPurchaseEvent. Immediately
/// deducts gold and adds the orb/relic to the sending client's CoopPlayerState
/// so the next heartbeat reflects the purchase (preventing gold bounce-back).
/// </summary>
public sealed class ShopPurchaseClientHandler : IClientHandler<ShopPurchaseEvent>
{
    public void Handle(ShopPurchaseEvent e)
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
                MultiplayerPlugin.Logger?.LogWarning($"[ShopPurchase] From unknown peer {senderPeerId}");
                return;
            }

            if (!services.TryResolve<CoopStateManager>(out var coopState)) return;
            var playerState = coopState.GetPlayerState(slot.SlotIndex);
            if (playerState == null)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[ShopPurchase] No CoopPlayerState for slot {slot.SlotIndex}");
                return;
            }

            int oldGold = playerState.Gold;
            playerState.Gold = Math.Max(0, playerState.Gold - e.Cost);

            if (e.Type == "orb")
                ApplyOrbPurchase(playerState, e);
            else if (e.Type == "relic")
                ApplyRelicPurchase(playerState, e);

            MultiplayerPlugin.Logger?.LogInfo(
                $"[ShopPurchase] Slot {slot.SlotIndex} ({slot.PlayerName}) bought {e.Type} '{e.Name}' " +
                $"for {e.Cost} gold ({oldGold} -> {playerState.Gold}), " +
                $"deck={playerState.CompleteDeck.Count}, relics={playerState.OwnedRelics.Count}");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ShopPurchase] Handler failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void ApplyOrbPurchase(CoopPlayerState playerState, ShopPurchaseEvent purchase)
    {
        try
        {
            // Find the orb prefab by name to capture its Level
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
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ShopPurchase] ApplyOrbPurchase failed: {ex.Message}");
        }
    }

    private void ApplyRelicPurchase(CoopPlayerState playerState, ShopPurchaseEvent purchase)
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
                    return;
                }
            }

            MultiplayerPlugin.Logger?.LogWarning(
                $"[ShopPurchase] Relic with effect={purchase.RelicEffect} not found");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ShopPurchase] ApplyRelicPurchase failed: {ex.Message}");
        }
    }
}
