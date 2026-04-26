using System;
using global::Battle.Enemies;
using HarmonyLib;
using Multipeglin.Events.Network.Battle;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class SpiritOfRadiaPhaseTransitionClientHandler : IClientHandler<SpiritOfRadiaPhaseTransitionEvent>
{
    public void Handle(SpiritOfRadiaPhaseTransitionEvent networkEvent)
    {
        try
        {
            switch (networkEvent.Step)
            {
                case 1:
                    MultiplayerPlugin.Logger?.LogInfo("[SpiritOfRadia] Client: firing PreTransitionStarted");
                    SpiritOfRadiaBoss.PreTransitionStarted?.Invoke();
                    break;
                case 2:
                    MultiplayerPlugin.Logger?.LogInfo("[SpiritOfRadia] Client: firing OnSpiritOfRadiaPhaseTransitionStarted");
                    SpiritOfRadiaBoss.OnSpiritOfRadiaPhaseTransitionStarted?.Invoke();
                    HidePhase2VisualLeftovers();
                    break;
                default:
                    MultiplayerPlugin.Logger?.LogWarning($"[SpiritOfRadia] Unknown step {networkEvent.Step}");
                    break;
            }
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[SpiritOfRadia] Client handler failed: {e.Message}");
        }
    }

    /// <summary>
    /// The host's StartPhase2Transition turns off _topCrystals (the purple
    /// rocks above the boss) and _targetingUI as part of the visual swap to
    /// phase 2. We block that whole coroutine on the client to avoid running
    /// gameplay state changes (peg conversion, status effects, ball spawn),
    /// which leaves those visuals lingering. Replicate just the visual hides
    /// here so the client matches the host's phase-2 silhouette.
    /// </summary>
    private static void HidePhase2VisualLeftovers()
    {
        var boss = UnityEngine.Object.FindObjectOfType<SpiritOfRadiaBoss>();
        if (boss == null)
        {
            return;
        }

        TryHideField(boss, "_topCrystals");
        TryHideBehaviourField(boss, "_targetingUI");
    }

    private static void TryHideField(SpiritOfRadiaBoss boss, string fieldName)
    {
        try
        {
            var go = AccessTools.Field(typeof(SpiritOfRadiaBoss), fieldName)?.GetValue(boss) as GameObject;
            if (go != null && go.activeSelf)
            {
                go.SetActive(false);
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[SpiritOfRadia] Failed to hide {fieldName}: {ex.Message}");
        }
    }

    private static void TryHideBehaviourField(SpiritOfRadiaBoss boss, string fieldName)
    {
        try
        {
            // _targetingUI is declared on the Enemy base class (protected).
            var f = AccessTools.Field(typeof(SpiritOfRadiaBoss), fieldName)
                ?? AccessTools.Field(typeof(global::Battle.Enemies.Enemy), fieldName);
            var b = f?.GetValue(boss) as Behaviour;
            if (b != null && b.gameObject.activeSelf)
            {
                b.gameObject.SetActive(false);
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[SpiritOfRadia] Failed to hide behaviour {fieldName}: {ex.Message}");
        }
    }
}
