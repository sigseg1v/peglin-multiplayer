using System;
using HarmonyLib;
using Map;
using UnityEngine;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class PegMinigameManagerPatches
{
    // =========================================================================
    // CLIENT: PEG MINIGAME SPECTATING — suppress ball creation and interaction
    // =========================================================================

    [HarmonyPatch(typeof(Peglin.PegMinigame.PegMinigameManager), "CreateOrb")]
    [HarmonyPrefix]
    public static bool PegMinigameManager_CreateOrb_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        if (AllowPegMinigameLogic || PendingClientPegMinigameLoad)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked PegMinigameManager.CreateOrb (spectating)");
        return false;
    }

    [HarmonyPatch(typeof(Peglin.PegMinigame.PegMinigameManager), "Initialize")]
    [HarmonyPostfix]
    public static void PegMinigameManager_Initialize_Postfix(Peglin.PegMinigame.PegMinigameManager __instance)
    {
        if (!ShouldSuppressClientLogic || !AllowPegMinigameLogic)
        {
            return;
        }

        StopLeakedMapCoroutines();
        SnapClientPegMinigameCamera(__instance);

        var ballField = AccessTools.Field(typeof(Peglin.PegMinigame.PegMinigameManager), "_ball");
        if (ballField?.GetValue(__instance) != null)
        {
            return;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] PegMinigame Initialize: retrying CreateOrb after flag arm");
        var createOrb = AccessTools.Method(typeof(Peglin.PegMinigame.PegMinigameManager), "CreateOrb");
        createOrb?.Invoke(__instance, null);
    }

    /// <summary>
    /// MapController.NodeSelected may already be running when PegMinigame loads.
    /// Stop it so a map-floor camera tween cannot override the minigame framing.
    /// </summary>
    private static void StopLeakedMapCoroutines()
    {
        try
        {
            var mc = MapController.instance;
            if (mc == null)
            {
                return;
            }

            mc.StopAllCoroutines();
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Stopped MapController coroutines for PegMinigame");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatch] StopLeakedMapCoroutines failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Snap Camera.main back to the minigame playfield after a map camera pan.
    /// </summary>
    private static void SnapClientPegMinigameCamera(Peglin.PegMinigame.PegMinigameManager mgr)
    {
        try
        {
            var mainCamField = AccessTools.Field(typeof(Peglin.PegMinigame.PegMinigameManager), "_mainCamera");
            var pegMinigameCam = mainCamField?.GetValue(mgr) as Camera;
            if (pegMinigameCam != null)
            {
                pegMinigameCam.enabled = true;
                pegMinigameCam.gameObject.SetActive(true);
            }

            var aimField = AccessTools.Field(typeof(Peglin.PegMinigame.PegMinigameManager), "_aimingShotTransform");
            var aim = aimField?.GetValue(mgr) as Transform;
            var ballStartField = AccessTools.Field(typeof(Peglin.PegMinigame.PegMinigameManager), "_ballStartTransform");
            var ballStart = ballStartField?.GetValue(mgr) as Transform;
            var anchor = aim ?? ballStart;

            var cam = pegMinigameCam != null ? pegMinigameCam : Camera.main;
            if (cam == null || anchor == null)
            {
                return;
            }

            var pos = cam.transform.position;
            cam.transform.position = new Vector3(anchor.position.x, anchor.position.y, pos.z);
            MultiplayerPlugin.Logger?.LogInfo(
                $"[ClientPatch] PegMinigame camera snapped to ({cam.transform.position.x:F1},{cam.transform.position.y:F1})");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatch] SnapClientPegMinigameCamera failed: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(Peglin.PegMinigame.PegMinigameManager), "PrepareNavigationOrbForFiring")]
    [HarmonyPrefix]
    public static bool PegMinigameManager_PrepareNavigationOrbForFiring_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        if (AllowPegMinigameLogic || PendingClientPegMinigameLoad)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked PegMinigameManager.PrepareNavigationOrbForFiring (spectating)");
        return false;
    }

    [HarmonyPatch(typeof(Peglin.PegMinigame.PegMinigameManager), "HandleRewardSlotTriggerActivated")]
    [HarmonyPrefix]
    public static bool PegMinigameManager_HandleRewardSlotTriggerActivated_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        if (AllowPegMinigameLogic || PendingClientPegMinigameLoad)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked PegMinigameManager.HandleRewardSlotTriggerActivated (spectating)");
        return false;
    }

    // Client never navigates in PegMinigame — host controls scene transitions
    [HarmonyPatch(typeof(Peglin.PegMinigame.PegMinigameManager), "HandleNavigationSlotTriggerActivated")]
    [HarmonyPrefix]
    public static bool PegMinigameManager_HandleNavigationSlotTriggerActivated_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        return false;
    }

    // Block FadeAndLoad: on client always (host controls navigation),
    // on host if waiting for clients to finish
    [HarmonyPatch(typeof(Peglin.PegMinigame.PegMinigameManager), "FadeAndLoad")]
    [HarmonyPrefix]
    public static bool PegMinigameManager_FadeAndLoad_Prefix(Peglin.PegMinigame.PegMinigameManager __instance)
    {
        // Client: always block navigation during interactive PegMinigame
        if (ShouldSuppressClientLogic)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked PegMinigameManager.FadeAndLoad on client");
            return false;
        }

        // Host: gate on all clients being done
        if (IsHosting && Events.Handlers.Coop.CoopRewardState.PegMinigamePhaseActive)
        {
            if (!Events.Handlers.Coop.CoopRewardState.AllClientPegMinigameChoicesReceived)
            {
                Events.Handlers.Coop.CoopRewardState.PendingPegMinigameManager = __instance;
                Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
                MultiplayerPlugin.Logger?.LogInfo(
                    "[CoopReward] PegMinigame: host waiting for clients before navigating " +
                    $"({Events.Handlers.Coop.CoopRewardState.ClientPegMinigameChoicesReceived.Count}/{Events.Handlers.Coop.CoopRewardState.TotalPegMinigameClientsExpected})");
                return false;
            }

            MultiplayerPlugin.Logger?.LogInfo("[CoopReward] PegMinigame: all clients done, host proceeding with navigation");
        }

        return true;
    }

    // =========================================================================
    // COOP: PEG MINIGAME — independent play + wait-for-all
    // =========================================================================

    /// <summary>
    /// Prefix captures _indexSelected before HandleUpgradeOptionClicked resets it to -1.
    /// Also marks HostPegMinigameDone on the host so FadeAndLoad can gate.
    /// </summary>
    [HarmonyPatch(typeof(Peglin.PegMinigame.PegMinigameManager), "HandleUpgradeOptionClicked")]
    [HarmonyPrefix]
    public static void PegMinigameManager_HandleUpgradeOptionClicked_Prefix(
        PeglinUI.PostBattle.UpgradeOption.UpgradeType type,
        int ____indexSelected,
        ref int __state)
    {
        __state = ____indexSelected;

        // Mark host as done with the reward phase (before FadeAndLoad is called inside the method)
        if (____indexSelected >= 0
            && type != PeglinUI.PostBattle.UpgradeOption.UpgradeType.INSPECT_ORB_FOR_UPGRADE
            && IsHosting
            && Events.Handlers.Coop.CoopRewardState.PegMinigamePhaseActive)
        {
            Events.Handlers.Coop.CoopRewardState.HostPegMinigameDone = true;
            MultiplayerPlugin.Logger?.LogInfo("[CoopReward] PegMinigame: host finished reward selection");
        }
    }

    /// <summary>
    /// Postfix: on CLIENT, send completion event to host. On HOST, save the active player state.
    /// Each player picks their own reward independently — no sharing.
    /// </summary>
    [HarmonyPatch(typeof(Peglin.PegMinigame.PegMinigameManager), "HandleUpgradeOptionClicked")]
    [HarmonyPostfix]
    public static void PegMinigameManager_HandleUpgradeOptionClicked_Postfix(
        PeglinUI.PostBattle.UpgradeOption.UpgradeType type,
        MapDataPegMinigame ____mapData,
        int __state)
    {
        if (__state < 0)
        {
            return; // no reward was selected
        }

        if (type == PeglinUI.PostBattle.UpgradeOption.UpgradeType.INSPECT_ORB_FOR_UPGRADE)
        {
            return;
        }

        var services = MultiplayerPlugin.Services;
        if (services == null)
        {
            return;
        }

        // CLIENT: send completion event to host
        if (ShouldSuppressClientLogic && AllowPegMinigameLogic)
        {
            try
            {
                var evt = new Events.Network.Scenarios.PegMinigameCompleteEvent();

                if (type == PeglinUI.PostBattle.UpgradeOption.UpgradeType.SKIP)
                {
                    evt.Skipped = true;
                    MultiplayerPlugin.Logger?.LogInfo("[CoopReward] PegMinigame client: skipped reward");
                }
                else if (____mapData?.Rewards != null && __state < ____mapData.Rewards.Count)
                {
                    var reward = ____mapData.Rewards[__state];
                    if (reward is Peglin.PegMinigame.OrbReward orbReward && orbReward.Orb != null)
                    {
                        evt.ChosenOrbPrefabName = orbReward.Orb.name.Replace("(Clone)", string.Empty).Trim();
                        evt.OrbLevel = orbReward.Orb.GetComponent<Battle.Attacks.Attack>()?.Level ?? 0;
                        MultiplayerPlugin.Logger?.LogInfo(
                            $"[CoopReward] PegMinigame client: chose orb '{evt.ChosenOrbPrefabName}' (lvl={evt.OrbLevel})");
                    }
                    else if (reward is Peglin.PegMinigame.RelicReward relicReward && relicReward.Relic != null)
                    {
                        evt.ChosenRelicEffect = (int)relicReward.Relic.effect;
                        MultiplayerPlugin.Logger?.LogInfo(
                            $"[CoopReward] PegMinigame client: chose relic '{relicReward.Relic.locKey}'");
                    }
                }

                if (services.TryResolve<Network.IMessageSender>(out var sender))
                {
                    sender.Send(evt);
                }

                // Disable PegMinigame logic so subsequent CreateOrb/nav calls are blocked
                AllowPegMinigameLogic = false;
                DisarmClientPegMinigameLoad();
                Events.Handlers.Coop.CoopRewardState.ClientPegMinigameChoiceSent = true;
                Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
                // Flag phase so ShowWaiting() picks descriptive text; clear any stale
                // AllChoicesComplete from a prior phase that would hide the overlay.
                Events.Handlers.Coop.CoopRewardState.PegMinigamePhaseActive = true;
                Events.Handlers.Coop.CoopRewardState.AllChoicesComplete = false;
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[CoopReward] PegMinigame client completion failed: {ex.Message}");
            }

            return;
        }

        // HOST: save active player state after reward granted
        if (IsHosting && Events.Handlers.Coop.CoopRewardState.PegMinigamePhaseActive)
        {
            try
            {
                if (services.TryResolve<GameState.CoopStateManager>(out var coopState))
                {
                    coopState.SaveActivePlayerState();
                }
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[CoopReward] PegMinigame host save failed: {ex.Message}");
            }
        }
    }
}
