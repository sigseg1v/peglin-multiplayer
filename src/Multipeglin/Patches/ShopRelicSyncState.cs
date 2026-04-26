using System.Collections.Generic;
using System.Linq;
using Battle;
using BepInEx.Logging;
using HarmonyLib;
using Relics;
using Rewired.Integration.UnityUI;
using Scenarios.Shop;
using UnityEngine;

namespace Multipeglin.Patches;

/// <summary>
/// Client-side holder for the host's chosen shop relic effects. The host's
/// ShopManager.SetUpRelicOffer dequeues from AllCommonRelicsRandomQueue (RNG),
/// which is empty on the client (shuffles are blocked). We instead sync the
/// chosen RelicEffects from the host and rebuild the shop UI on the client.
/// </summary>
public static class ShopRelicSyncState
{
    /// <summary>
    /// Latest list of RelicEffect ints chosen by the host's shop. Updated every
    /// heartbeat while the host is on ShopScenario.
    /// </summary>
    public static List<int> LatestRelicEffects { get; set; }

    /// <summary>The ShopManager currently initialized on the client, if any.</summary>
    public static ShopManager CurrentShopManager { get; set; }

    /// <summary>
    /// Build the shop's relic items from <see cref="LatestRelicEffects"/>. Mirrors
    /// the instantiation code in ShopManager.SetUpRelicOffer (minus the queue
    /// dequeueing) so the displayed relics match the host exactly.
    /// </summary>
    public static void PopulateShopRelics(ShopManager sm, ManualLogSource log)
    {
        if (sm == null)
        {
            return;
        }

        var effects = LatestRelicEffects;
        if (effects == null || effects.Count == 0)
        {
            log?.LogInfo("[ShopRelicSync] No synced relic effects yet — deferring shop relic display");
            return;
        }

        try
        {
            var rmField = AccessTools.Field(typeof(ShopManager), "relicManager");
            var rm = rmField?.GetValue(sm) as RelicManager;
            if (rm == null)
            {
                log?.LogWarning("[ShopRelicSync] relicManager null on ShopManager");
                return;
            }

            var prefabField = AccessTools.Field(typeof(ShopManager), "purchasablePrefab");
            var prefab = prefabField?.GetValue(sm) as GameObject;
            if (prefab == null)
            {
                log?.LogWarning("[ShopRelicSync] purchasablePrefab null");
                return;
            }

            var containerField = AccessTools.Field(typeof(ShopManager), "relicContainer");
            var container = containerField?.GetValue(sm) as GameObject;
            if (container == null)
            {
                log?.LogWarning("[ShopRelicSync] relicContainer null");
                return;
            }

            var purchasableField = AccessTools.Field(typeof(ShopManager), "_purchasableRelics");
            var purchasable = purchasableField?.GetValue(sm) as System.Array;
            var relicItemsField = AccessTools.Field(typeof(ShopManager), "relicItems");
            var relicItems = relicItemsField?.GetValue(sm) as IList<ShopItem>;

            var phcField = AccessTools.Field(typeof(ShopManager), "_playerHealthController");
            var phc = phcField?.GetValue(sm) as PlayerHealthController;

            var rewiredField = AccessTools.Field(typeof(ShopManager), "_rewiredEventSystem");
            var rewired = rewiredField?.GetValue(sm) as RewiredEventSystem;

            DestroyExistingRelicItems(sm, purchasable, relicItems);

            var allRelicAssets = Resources.FindObjectsOfTypeAll<Relic>();
            var mult = rm.WandOfGreedEffectActive() ? 2 : 1;
            if (Map.MapController.instance != null && Map.MapController.instance.Act == 4)
            {
                mult *= 2;
            }

            var slot = 0;
            for (var i = 0; i < effects.Count; i++)
            {
                var effect = (RelicEffect)effects[i];
                var relicAsset = allRelicAssets.FirstOrDefault(r => r.effect == effect);
                if (relicAsset == null)
                {
                    log?.LogWarning($"[ShopRelicSync] Relic asset not found for effect {effect}");
                    continue;
                }

                var go = Object.Instantiate(prefab, container.transform);
                var item = go.GetComponent<ShopItem>();
                if (item == null)
                {
                    continue;
                }

                var purchasableRelic = new PurchasableRelic(relicAsset, rm, mult);
                item.Initialize(purchasableRelic, sm, phc);

                if (rewired != null)
                {
                    var arrow = item.GetComponentInChildren<ArrowSelection>();
                    arrow?.rewiredEventSystem = rewired;
                }

                if (purchasable != null && slot < purchasable.Length)
                {
                    purchasable.SetValue(purchasableRelic, slot);
                }

                relicItems?.Add(item);
                slot++;
            }

            log?.LogInfo($"[ShopRelicSync] Populated {slot} shop relics from synced effects");
        }
        catch (System.Exception ex)
        {
            log?.LogWarning($"[ShopRelicSync] PopulateShopRelics failed: {ex.Message}");
        }
    }

    private static void DestroyExistingRelicItems(ShopManager sm, System.Array purchasable, IList<ShopItem> relicItems)
    {
        if (relicItems != null)
        {
            for (var i = relicItems.Count - 1; i >= 0; i--)
            {
                var item = relicItems[i];
                if (item != null && item.gameObject != null)
                {
                    Object.Destroy(item.gameObject);
                }
            }

            relicItems.Clear();
        }

        if (purchasable != null)
        {
            for (var i = 0; i < purchasable.Length; i++)
            {
                purchasable.SetValue(null, i);
            }
        }
    }

    /// <summary>
    /// Re-run the relic population against the live ShopManager so the client's
    /// displayed relics match the host's latest choice. Called when the snapshot
    /// brings in new relic effects.
    /// </summary>
    public static void RefreshDisplayedShopRelics(ManualLogSource log)
    {
        var sm = CurrentShopManager;
        if (sm == null)
        {
            return;
        }

        PopulateShopRelics(sm, log);
    }
}
