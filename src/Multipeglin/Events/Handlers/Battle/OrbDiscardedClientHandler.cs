using System;
using global::Battle;
using HarmonyLib;
using Multipeglin.Events.Network.Battle;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class OrbDiscardedClientHandler : IClientHandler<OrbDiscardedEvent>
{
    public void Handle(OrbDiscardedEvent networkEvent)
    {
        try
        {
            if (UI.LobbyUI.GameStartReceived)
            {
                // In coop, when it's the client's turn and a discard was processed on the
                // host, the client needs to reset its aiming ball so HandleClientAiming
                // re-creates it with the new orb from the updated shuffled deck.
                if (Coop.TurnChangeClientHandler.IsMyTurn)
                {
                    // Pop the client's local shuffledDeck so the next HandleClientAiming
                    // picks up the correct new orb. The heartbeat will confirm/correct later.
                    var newActiveOrbName = PopClientShuffledDeck();

                    // Increment the client's local discard counter so the UI ("0/1" → "1/1") updates
                    var bc = UnityEngine.Object.FindObjectOfType<global::Battle.BattleController>();
                    if (bc != null)
                    {
                        bc.NumShotsDiscarded++;
                        global::Battle.BattleController.OnOrbDiscarded?.Invoke();
                    }

                    MultiplayerPlugin.Logger?.LogInfo("[OrbDiscarded] Client's discard processed — resetting aiming ball");
                    Multipeglin.Patches.MultiplayerClientPatches.ResetClientAimingBall();

                    // Drive the active-orb preview slot immediately so the player sees the
                    // new orb on the same frame as the discard, not after the next heartbeat
                    // (~2s lag). The heartbeat will still reaffirm with the host's authoritative
                    // CurrentOrb in case our newly-popped name was wrong.
                    if (!string.IsNullOrEmpty(newActiveOrbName))
                    {
                        var services = MultiplayerPlugin.Services;
                        if (services?.TryResolve<GameState.GameStateApplyService>(out var apply) == true)
                        {
                            apply.DeckApplier.RefreshActiveOrbDisplay(newActiveOrbName);
                        }
                    }
                }

                return;
            }

            BattleController.OnOrbDiscarded?.Invoke();
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"OrbDiscarded handler failed: {e.Message}");
        }
    }

    /// <returns>Name of the orb that was popped (the new active orb), or null on failure.</returns>
    private static string PopClientShuffledDeck()
    {
        try
        {
            var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
            if (dms == null || dms.Length == 0)
            {
                return null;
            }

            var dm = dms[0];

            var shuffledField = AccessTools.Field(typeof(DeckManager), "shuffledDeck");
            var shuffled = shuffledField?.GetValue(dm) as System.Collections.Generic.Stack<GameObject>;
            if (shuffled != null && shuffled.Count > 0)
            {
                var popped = shuffled.Pop();
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[OrbDiscarded] Popped '{popped?.name}' from client shuffledDeck (remaining: {shuffled.Count})");
                return popped?.name;
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[OrbDiscarded] Failed to pop shuffledDeck: {ex.Message}");
        }

        return null;
    }
}
