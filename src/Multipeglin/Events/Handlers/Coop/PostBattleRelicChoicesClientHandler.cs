using System;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Client handler for PostBattleRelicChoicesEvent: stores the host's boss/rare
/// relic choices so the client can display them. If the relic panel is already
/// visible, updates the RelicIcons immediately.
/// </summary>
public sealed class PostBattleRelicChoicesClientHandler : IClientHandler<PostBattleRelicChoicesEvent>
{
    public void Handle(PostBattleRelicChoicesEvent e)
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null) return;

            // Only process on client
            if (services.TryResolve<IMultiplayerMode>(out var mode) && mode.IsHosting)
                return;

            MultiplayerPlugin.Logger?.LogInfo(
                $"[PostBattleRelicChoices] Received {e.Choices?.Count ?? 0} relic choices from host");

            CoopRewardState.PendingPostBattleRelicChoices = e.Choices;

            // If the relic panel is already visible, apply now
            ApplyRelicChoicesToUI(e);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[PostBattleRelicChoices] Handler failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    internal static void ApplyRelicChoicesToUI(PostBattleRelicChoicesEvent e)
    {
        if (e?.Choices == null || e.Choices.Count == 0) return;

        var canvases = Resources.FindObjectsOfTypeAll<PeglinUI.PostBattle.BattleUpgradeCanvas>();
        if (canvases == null || canvases.Length == 0) return;

        PeglinUI.PostBattle.BattleUpgradeCanvas activeCanvas = null;
        foreach (var c in canvases)
        {
            if (c.gameObject.activeInHierarchy)
            {
                activeCanvas = c;
                break;
            }
        }
        if (activeCanvas == null) return;

        // Find the _relicPanel via reflection
        var relicPanelField = HarmonyLib.AccessTools.Field(
            typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "_relicPanel");
        var relicPanel = relicPanelField?.GetValue(activeCanvas) as GameObject;
        if (relicPanel == null || !relicPanel.activeInHierarchy) return;

        // Get all RelicIcons in the panel
        var icons = relicPanel.GetComponentsInChildren<RelicIcon>(true);
        if (icons == null || icons.Length == 0) return;

        // Find all loaded Relic assets
        var allRelics = Resources.FindObjectsOfTypeAll<Relics.Relic>();

        int applied = 0;
        for (int i = 0; i < icons.Length; i++)
        {
            if (i < e.Choices.Count)
            {
                var choice = e.Choices[i];
                var relic = FindRelicByEffect(allRelics, choice.Effect);
                if (relic != null)
                {
                    icons[i].SetRelic(relic);
                    icons[i].shouldShowTooltip = false;
                    icons[i].transform.parent.gameObject.SetActive(true);
                    applied++;
                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[PostBattleRelicChoices] Set icon {i} to '{relic.locKey}' (effect={choice.Effect})");
                }
                else
                {
                    MultiplayerPlugin.Logger?.LogWarning(
                        $"[PostBattleRelicChoices] Could not find Relic asset for effect={choice.Effect}");
                }
            }
            else
            {
                // Disable extra slots
                icons[i].transform.parent.gameObject.SetActive(false);
            }
        }

        MultiplayerPlugin.Logger?.LogInfo(
            $"[PostBattleRelicChoices] Applied {applied}/{e.Choices.Count} host relic choices to UI");
    }

    private static Relics.Relic FindRelicByEffect(Relics.Relic[] allRelics, int effect)
    {
        foreach (var relic in allRelics)
        {
            if ((int)relic.effect == effect)
                return relic;
        }
        return null;
    }
}
