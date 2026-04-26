using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class ShopManagerPatches
{
    // =========================================================================
    // SHOP + TREASURE: Wait-for-all synchronization
    // =========================================================================

    /// <summary>
    /// Replace the client's SetUpRelicOffer with a version that uses the host's
    /// chosen relic effects (synced via MapStateSnapshot.SeededShopRelicEffects).
    /// The original path dequeues from AllCommonRelicsRandomQueue, which is empty
    /// on the client because the shuffle that populates it uses RNG (suppressed).
    /// </summary>
    [HarmonyPatch(typeof(Scenarios.Shop.ShopManager), "SetUpRelicOffer")]
    [HarmonyPrefix]
    public static bool ShopManager_SetUpRelicOffer_Prefix(Scenarios.Shop.ShopManager __instance)
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        try
        {
            ShopRelicSyncState.CurrentShopManager = __instance;
            ShopRelicSyncState.PopulateShopRelics(__instance, MultiplayerPlugin.Logger);
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ShopRelicSync] Prefix failed: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Clear shop relic sync state when the shop closes so stale references from
    /// a prior visit don't get reused on the next shop.
    /// </summary>
    [HarmonyPatch(typeof(Scenarios.Shop.ShopManager), "CloseStore")]
    [HarmonyPostfix]
    public static void ShopManager_CloseStore_Postfix()
    {
        ShopRelicSyncState.CurrentShopManager = null;
        ShopRelicSyncState.LatestRelicEffects = null;
    }

    /// <summary>
    /// Track purchases on the client so we can send them to the host on shop exit.
    /// </summary>
    [HarmonyPatch(typeof(Scenarios.Shop.ShopManager), "PurchaseItem")]
    [HarmonyPostfix]
    public static void ShopManager_PurchaseItem_Postfix(Scenarios.Shop.IPurchasableItem item)
    {
        if (!ShouldSuppressClientLogic)
        {
            return;
        }

        if (!AllowShopLogic)
        {
            return;
        }

        try
        {
            var purchase = new Events.Network.Scenarios.ShopPurchase();
            if (item is Scenarios.Shop.PurchasableOrb orbItem)
            {
                purchase.Type = "orb";
                // Get the orb prefab name from the PurchasableOrb via reflection
                var prefabField = HarmonyLib.AccessTools.Field(typeof(Scenarios.Shop.PurchasableOrb), "_orbPrefab");
                var prefab = prefabField?.GetValue(orbItem) as UnityEngine.GameObject;
                purchase.Name = prefab?.name?.Replace("(Clone)", string.Empty).Trim() ?? "unknown";
                purchase.Cost = item.GetCost();
            }
            else if (item is Scenarios.Shop.PurchasableRelic relicItem)
            {
                purchase.Type = "relic";
                var relicField = HarmonyLib.AccessTools.Field(typeof(Scenarios.Shop.PurchasableRelic), "_relic");
                var relic = relicField?.GetValue(relicItem) as Relics.Relic;
                purchase.Name = relic?.locKey ?? "unknown";
                purchase.RelicEffect = relic != null ? (int)relic.effect : -1;
                purchase.Cost = item.GetCost();
            }

            ClientShopPurchases.Add(purchase);
            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatch] Tracked shop purchase: {purchase.Type} '{purchase.Name}' cost={purchase.Cost}");

            // Send immediately so host deducts gold + applies orb/relic BEFORE the
            // next heartbeat. Without this, the heartbeat syncs the old (stale) gold
            // value back to the client between purchases, masking the deduction.
            try
            {
                var services = MultiplayerPlugin.Services;
                if (services?.TryResolve<Network.IMessageSender>(out var sender) == true)
                {
                    sender.Send(new Events.Network.Scenarios.ShopPurchaseEvent
                    {
                        Type = purchase.Type,
                        Name = purchase.Name,
                        Cost = purchase.Cost,
                        RelicEffect = purchase.RelicEffect,
                    });
                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[ClientPatch] Sent ShopPurchaseEvent: {purchase.Type} '{purchase.Name}' cost={purchase.Cost}");
                }
            }
            catch (System.Exception sendEx)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[ClientPatch] Failed to send ShopPurchaseEvent: {sendEx.Message}");
            }
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatch] Failed to track shop purchase: {ex.Message}");
        }
    }

    /// <summary>
    /// ShopManager.CloseStore — on client: send ShopCompleteEvent, block navigation.
    /// On host: check wait-for-all before allowing navigation to proceed.
    /// </summary>
    [HarmonyPatch(typeof(Scenarios.Shop.ShopManager), "CloseStore")]
    [HarmonyPrefix]
    public static bool ShopManager_CloseStore_Prefix(Scenarios.Shop.ShopManager __instance)
    {
        if (!UI.LobbyUI.GameStartReceived)
        {
            return true; // Not in coop
        }

        if (ShouldSuppressClientLogic)
        {
            // CLIENT: first click sends ShopCompleteEvent and shows waiting overlay.
            // Subsequent clicks are silently ignored — the overlay already covers
            // the screen, but the EventSystem can still route clicks to the
            // button if the overlay canvas somehow doesn't block them.
            if (Events.Handlers.Coop.CoopRewardState.ClientShopChoiceSent)
            {
                return false;
            }

            try
            {
                AllowShopLogic = false;

                var remainingGold = Currency.CurrencyManager.Instance?.GoldAmount ?? 0;
                var goldSpent = ClientShopStartGold - remainingGold;

                var evt = new Events.Network.Scenarios.ShopCompleteEvent
                {
                    Purchases = new System.Collections.Generic.List<Events.Network.Scenarios.ShopPurchase>(ClientShopPurchases),
                    GoldSpent = goldSpent > 0 ? goldSpent : 0,
                    RemainingGold = remainingGold,
                };

                var services = MultiplayerPlugin.Services;
                if (services?.TryResolve<Network.IMessageSender>(out var sender) == true)
                {
                    sender.Send(evt);
                    MultiplayerPlugin.Logger?.LogInfo($"[ClientPatch] Sent ShopCompleteEvent: {evt.Purchases.Count} purchases, gold={evt.RemainingGold}");
                }

                ClientShopPurchases.Clear();
                Events.Handlers.Coop.CoopRewardState.ClientShopChoiceSent = true;
            }
            catch (System.Exception ex)
            {
                MultiplayerPlugin.Logger?.LogError($"[ClientPatch] Failed to send ShopCompleteEvent: {ex.Message}");
            }

            Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
            Events.Handlers.Coop.CoopRewardState.ShopPhaseActive = true;
            // Reset stale AllChoicesComplete from prior phases — otherwise
            // CoopRewardUI.Update will early-return and never show the overlay.
            Events.Handlers.Coop.CoopRewardState.AllChoicesComplete = false;
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Client finished shopping — waiting for other players");
            return false; // Block CloseStore navigation on client
        }

        if (IsHosting && Events.Handlers.Coop.CoopRewardState.ShopPhaseActive)
        {
            // HOST: idempotent — if already done and waiting, just silently block.
            if (Events.Handlers.Coop.CoopRewardState.HostShopDone
                && !Events.Handlers.Coop.CoopRewardState.AllClientShopChoicesReceived)
            {
                return false; // Still waiting — no log spam, overlay already visible.
            }

            // Mark self as done, check if all clients finished
            Events.Handlers.Coop.CoopRewardState.HostShopDone = true;
            // Reset stale AllChoicesComplete from prior phases so the overlay appears.
            Events.Handlers.Coop.CoopRewardState.AllChoicesComplete = false;

            if (Events.Handlers.Coop.CoopRewardState.AllClientShopChoicesReceived)
            {
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Host CloseStore — all clients done, proceeding");
                Events.Handlers.Coop.CoopRewardState.ShopPhaseActive = false;
                Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = false;
                Events.Handlers.Coop.CoopRewardState.ShopCompletionProceeded = true;
                // Dispatch AllChoicesComplete so clients transition to the
                // "Waiting for host to pick next stage..." overlay.
                var services = MultiplayerPlugin.Services;
                if (services?.TryResolve<Events.IGameEventRegistry>(out var reg) == true)
                {
                    reg.Dispatch(new Events.Network.Coop.AllChoicesCompleteEvent { Phase = "shop" });
                }

                return true; // Let CloseStore run normally
            }
            else
            {
                // Not all clients done — store reference, flag waiting, block.
                Events.Handlers.Coop.CoopRewardState.PendingShopManager = __instance;
                Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Host CloseStore — waiting for other players to finish shopping");
                return false; // Block until all clients done
            }
        }

        return true;
    }
}
