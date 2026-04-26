using HarmonyLib;
using UnityEngine;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class TextScenarioInteractionsPatches
{
    /// <summary>
    /// CLIENT-ONLY prefix: when TextScenarioInteractions.OfferRelic is called,
    /// replace the seeded-queue logic with a single local random relic. Same
    /// reason as above — the client's queues are broken.
    /// </summary>
    [HarmonyPatch(typeof(Scenarios.TextScenarioInteractions), "OfferRelic")]
    [HarmonyPrefix]
    public static bool TextScenarioInteractions_OfferRelic_ClientOverride_Prefix(
        Scenarios.TextScenarioInteractions __instance,
        Relics.RelicRarity rarity)
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        if (!AllowTextScenarioLogic)
        {
            return true;
        }

        try
        {
            var relicPanelField = AccessTools.Field(typeof(Scenarios.TextScenarioInteractions), "relicPanel");
            var skipButtonField = AccessTools.Field(typeof(Scenarios.TextScenarioInteractions), "skipRelicButton");
            var rmField = AccessTools.Field(typeof(Scenarios.TextScenarioInteractions), "relicManager");

            var relicPanel = relicPanelField?.GetValue(__instance) as GameObject;
            var skipButton = skipButtonField?.GetValue(__instance) as UnityEngine.UI.Button;
            var relicManager = rmField?.GetValue(__instance) as Relics.RelicManager;
            if (relicPanel == null)
            {
                return true;
            }

            relicPanel.SetActive(true);

            // Slot-keyed RNG so each player picks a different relic. UnityEngine.Random
            // shares seed across all clients, which would otherwise pick the same relic.
            var mySlot = Events.Handlers.Coop.CoopSlotHelper.GetLocalSlotIndex(MultiplayerPlugin.Services);
            var relic = PickRandomLocalRelic(rarity, relicManager, mySlot);
            var icons = relicPanel.GetComponentsInChildren<RelicIcon>(true);
            for (var i = 0; i < icons.Length; i++)
            {
                if (i == 0 && relic != null)
                {
                    icons[i].SetRelic(relic);
                    icons[i].transform.parent.gameObject.SetActive(true);
                }
                else
                {
                    icons[i].transform.parent.gameObject.SetActive(false);
                }
            }

            skipButton?.gameObject.SetActive(true);

            MultiplayerPlugin.Logger?.LogInfo(
                $"[ClientPatch] TextScenario OfferRelic slot={mySlot} relic=" +
                $"'{relic?.name ?? "<none>"}' rarity={rarity}");
            return false;
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[ClientPatch] OfferRelic client override failed, falling through: {ex.Message}");
            return true;
        }
    }
}
