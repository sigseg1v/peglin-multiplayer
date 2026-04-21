namespace Multipeglin.UI;

using System.Collections.Generic;
using HarmonyLib;
using Multipeglin.Events.Handlers.Coop;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;
using UnityEngine;

/// <summary>
/// Polls each frame while the client is in the post-battle native reward phase
/// and applies the host-provided relic choices to the active BattleUpgradeCanvas
/// relic panel as soon as both the UI and the synced choices are available.
///
/// This fixes the race where the host's PostBattleRelicChoicesEvent arrives
/// either before the client's _relicPanel becomes active (handler's immediate
/// apply finds no panel) or after SetupRelicGrant already populated the panel
/// with bogus Coal relics (client's RNG queue is empty). A single authoritative
/// polling loop converges correctly regardless of order.
/// </summary>
public sealed class ClientRelicChoiceApplier : MonoBehaviour
{
    private static readonly System.Reflection.FieldInfo _relicPanelField =
        AccessTools.Field(typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "_relicPanel");

    // Effect ints of the choices we most recently applied, so we stop trying
    // once the panel matches. Reset when PendingPostBattleRelicChoices changes
    // or the reward phase ends.
    private List<int> _lastAppliedEffects;

    void Update()
    {
        if (MultiplayerPlugin.Services == null) return;
        if (!MultiplayerPlugin.Services.TryResolve<IMultiplayerMode>(out var mode)) return;
        if (!mode.IsSpectating) { _lastAppliedEffects = null; return; }

        var choices = CoopRewardState.PendingPostBattleRelicChoices;
        if (choices == null || choices.Count == 0) { _lastAppliedEffects = null; return; }

        if (!CoopRewardState.ClientInNativeRewardPhase) return;

        if (ChoicesAlreadyApplied(choices)) return;

        TryApply(choices);
    }

    private bool ChoicesAlreadyApplied(List<RelicChoiceEntry> choices)
    {
        if (_lastAppliedEffects == null || _lastAppliedEffects.Count != choices.Count) return false;
        for (int i = 0; i < choices.Count; i++)
            if (_lastAppliedEffects[i] != choices[i].Effect) return false;
        return true;
    }

    private void TryApply(List<RelicChoiceEntry> choices)
    {
        var activeCanvas = FindActiveCanvas();
        if (activeCanvas == null) return;

        var relicPanel = _relicPanelField?.GetValue(activeCanvas) as GameObject;
        if (relicPanel == null || !relicPanel.activeInHierarchy) return;

        var icons = relicPanel.GetComponentsInChildren<RelicIcon>(true);
        if (icons == null || icons.Length == 0) return;

        var allRelics = Resources.FindObjectsOfTypeAll<Relics.Relic>();

        int applied = 0;
        for (int i = 0; i < icons.Length; i++)
        {
            if (i < choices.Count)
            {
                var relic = FindByEffect(allRelics, choices[i].Effect);
                if (relic == null) continue;
                icons[i].SetRelic(relic);
                icons[i].shouldShowTooltip = false;
                icons[i].transform.parent.gameObject.SetActive(true);
                applied++;
            }
            else
            {
                icons[i].transform.parent.gameObject.SetActive(false);
            }
        }

        if (applied == 0) return;

        _lastAppliedEffects = new List<int>(choices.Count);
        for (int i = 0; i < choices.Count; i++) _lastAppliedEffects.Add(choices[i].Effect);

        MultiplayerPlugin.Logger?.LogInfo(
            $"[ClientRelicApplier] Applied {applied}/{choices.Count} host-provided relic choices to panel " +
            $"(icons={icons.Length}, effects=[{string.Join(",", _lastAppliedEffects)}])");
    }

    private static PeglinUI.PostBattle.BattleUpgradeCanvas FindActiveCanvas()
    {
        var canvases = Resources.FindObjectsOfTypeAll<PeglinUI.PostBattle.BattleUpgradeCanvas>();
        if (canvases == null) return null;
        foreach (var c in canvases)
            if (c != null && c.gameObject.activeInHierarchy) return c;
        return null;
    }

    private static Relics.Relic FindByEffect(Relics.Relic[] all, int effect)
    {
        foreach (var r in all)
            if ((int)r.effect == effect) return r;
        return null;
    }
}
