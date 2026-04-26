using Data;
using HarmonyLib;
using UnityEngine;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class BattleUpgradeCanvasPatches
{
    /// <summary>
    /// BattleUpgradeCanvas.AcceptRelic — on client during treasure, send completion event.
    /// Also tracks chosen relic during post-battle boss/rare relic selection.
    /// </summary>
    [HarmonyPatch(typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "AcceptRelic")]
    [HarmonyPostfix]
    public static void BattleUpgradeCanvas_AcceptRelic_Postfix(Relics.Relic relic)
    {
        if (!ShouldSuppressClientLogic)
        {
            return;
        }

        // Track boss/rare relic choice during post-battle native reward phase
        if (AllowNativeRewardLogic)
        {
            ClientChosenPostBattleRelicEffect = (int)relic.effect;
            ClientChosenPostBattleRelicName = relic.locKey;
            MultiplayerPlugin.Logger?.LogInfo(
                $"[ClientPatch] Client chose post-battle relic: '{relic.locKey}' (effect={relic.effect})");
        }

        // Treasure-specific flow
        if (!AllowTreasureLogic)
        {
            return;
        }

        if (Events.Handlers.Coop.CoopRewardState.ClientTreasureChoiceSent)
        {
            return;
        }

        try
        {
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<Network.IMessageSender>(out var sender) == true)
            {
                sender.Send(new Events.Network.Scenarios.TreasureCompleteEvent
                {
                    ChosenRelicEffect = (int)relic.effect,
                    ChosenRelicName = relic.locKey,
                });
                MultiplayerPlugin.Logger?.LogInfo($"[ClientPatch] Client accepted treasure relic '{relic.locKey}' — sent TreasureCompleteEvent");
            }

            Events.Handlers.Coop.CoopRewardState.ClientTreasureChoiceSent = true;
            // Show the waiting overlay once this player is done. AcceptRelic only
            // closes the in-scene relic panel; the chest scene stays until every
            // client finishes, so without this the client sits on the scene with
            // no feedback.
            Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
            Events.Handlers.Coop.CoopRewardState.TreasurePhaseActive = true;
            Events.Handlers.Coop.CoopRewardState.AllChoicesComplete = false;
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogError($"[ClientPatch] Failed to send TreasureCompleteEvent: {ex.Message}");
        }
    }

    /// <summary>
    /// CLIENT-ONLY prefix: when the native Treasure UI asks BattleUpgradeCanvas
    /// to set up a relic grant, roll a single local random relic and display only
    /// that one icon. Prevents the "5 bugged Blastic Powder" artifact from broken
    /// client-side relic queues.
    /// </summary>
    [HarmonyPatch(typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "SetupRelicGrant")]
    [HarmonyPrefix]
    public static bool BattleUpgradeCanvas_SetupRelicGrant_ClientOverride_Prefix(
        PeglinUI.PostBattle.BattleUpgradeCanvas __instance,
        Relics.RelicRarity rarity,
        bool isTreasure)
    {
        // Only intercept on client. Two cases:
        //   1) Treasure (?) room: AllowTreasureLogic gate, single relic, host-broadcast first
        //      then slot-keyed fallback.
        //   2) Post-battle (boss/rare/common) grant: AllowNativeRewardLogic gate, N relics,
        //      slot-keyed local roll. Native code uses _relicManager.GetMultipleRelicsOffOfQueue
        //      which produces identical first-N relics on every client because the seeded
        //      queue never advances on clients (host runs all battles).
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        if (!AllowTreasureLogic && !AllowNativeRewardLogic)
        {
            return true;
        }

        try
        {
            var mainOptionsField = AccessTools.Field(typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "_mainOptionsPanel");
            var relicPanelField = AccessTools.Field(typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "_relicPanel");
            var stateField = AccessTools.Field(typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "_state");
            var rarityField = AccessTools.Field(typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "_relicGrantRarity");
            var relicManagerField = AccessTools.Field(typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "_relicManager");
            var skipGoldField = AccessTools.Field(typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "_relicSkipButtonGoldContainer");

            var mainOptions = mainOptionsField?.GetValue(__instance) as GameObject;
            var relicPanel = relicPanelField?.GetValue(__instance) as GameObject;
            var relicManager = relicManagerField?.GetValue(__instance) as Relics.RelicManager;
            if (relicPanel == null)
            {
                return true; // fall through
            }

            mainOptions?.SetActive(false);
            relicPanel.SetActive(true);

            if (stateField != null && stateField.FieldType.IsEnum)
            {
                try
                { stateField.SetValue(__instance, System.Enum.Parse(stateField.FieldType, "RELIC")); }
                catch { }
            }

            rarityField?.SetValue(__instance, rarity);

            var mySlot = Events.Handlers.Coop.CoopSlotHelper.GetLocalSlotIndex(MultiplayerPlugin.Services);
            var icons = relicPanel.GetComponentsInChildren<RelicIcon>(true);

            if (isTreasure)
            {
                // Prefer the host-rolled per-slot relic so each client sees a different,
                // host-authoritative pick. Fall back to slot-keyed local roll if the
                // event hasn't arrived yet (race when client clicks chest before broadcast).
                Relics.Relic relic = null;
                var source = "host";
                if (mySlot >= 0
                    && Events.Handlers.Coop.CoopRewardState.PerSlotTreasureRelics.TryGetValue(mySlot, out var hostRelicName))
                {
                    relic = FindRelicByName(hostRelicName);
                    if (relic == null)
                    {
                        source = "host(missing prefab, fallback)";
                    }
                }

                if (relic == null)
                {
                    relic = PickRandomLocalRelic(rarity, relicManager, mySlot);
                    if (source == "host")
                    {
                        source = "local-slot-keyed";
                    }
                }

                for (var i = 0; i < icons.Length; i++)
                {
                    if (i == 0 && relic != null)
                    {
                        icons[i].SetRelic(relic);
                        icons[i].shouldShowTooltip = false;
                        icons[i].transform.parent.gameObject.SetActive(true);
                    }
                    else
                    {
                        icons[i].transform.parent.gameObject.SetActive(false);
                    }
                }

                MultiplayerPlugin.Logger?.LogInfo(
                    $"[ClientPatch] Treasure relic grant ({source}) slot={mySlot} relic=" +
                    $"'{relic?.name ?? "<none>"}' rarity={rarity}");
                return false;
            }

            // Non-treasure (post-battle) path: replicate native count logic and do a
            // slot-keyed multi-pick.
            var num = 1;
            if (rarity == Relics.RelicRarity.BOSS)
            {
                num = 3;
            }
            else if (rarity == Relics.RelicRarity.RARE)
            {
                num = 2;
            }

            if (rarity == Relics.RelicRarity.BOSS)
            {
                var skipGold = skipGoldField?.GetValue(__instance) as GameObject;
                skipGold?.SetActive(true);
            }

            var mapDataBattle = StaticGameData.dataToLoad as MapDataBattle;
            if (mapDataBattle != null && mapDataBattle.name == "MimicMinibossMapData")
            {
                num++;
            }
            // Side-effect-bearing relic checks must run so usage counters tick correctly.
            if (relicManager != null && relicManager.AttemptUseRelic(Relics.RelicEffect.ADDITIONAL_ORB_RELIC_OPTIONS))
            {
                num++;
            }

            if (relicManager != null && relicManager.AttemptUseRelic(Relics.RelicEffect.ADDITIONAL_PEGLIN_CHOICES))
            {
                num++;
            }

            var slotForRng = mySlot >= 0 ? mySlot : 0;
            var picks = PickMultipleLocalRelics(rarity, num, relicManager, slotForRng);
            for (var i = 0; i < icons.Length; i++)
            {
                if (i < picks.Count && picks[i] != null)
                {
                    icons[i].SetRelic(picks[i]);
                    icons[i].shouldShowTooltip = false;
                    icons[i].transform.parent.gameObject.SetActive(true);
                }
                else
                {
                    icons[i].transform.parent.gameObject.SetActive(false);
                }
            }

            var pickNames = new System.Collections.Generic.List<string>();
            foreach (var p in picks)
            {
                pickNames.Add(p?.name ?? "<null>");
            }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[ClientPatch] Post-battle relic grant slot={mySlot} rarity={rarity} count={picks.Count} relics=[{string.Join(",", pickNames)}]");
            return false;
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[ClientPatch] SetupRelicGrant client override failed, falling through: {ex.Message}");
            return true;
        }
    }
}
