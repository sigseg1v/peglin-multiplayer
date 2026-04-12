using System;
using System.Collections.Generic;
using System.Linq;
using Battle.Attacks;
using PeglinMods.Multiplayer.Events.Network.Scenarios;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.Multiplayer;
using UnityEngine;

namespace PeglinMods.Multiplayer.Events.Handlers.Scenarios;

/// <summary>
/// Client handler for MirrorEventCompleteEvent: only the host processes this.
/// Updates the client's CoopPlayerState deck based on their mirror choice.
/// For "remove_all", the host finds the Orboros OrbPool and adds its orbs.
/// </summary>
public sealed class MirrorEventCompleteClientHandler : IClientHandler<MirrorEventCompleteEvent>
{
    public void Handle(MirrorEventCompleteEvent e)
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null) return;

            if (!services.TryResolve<IMultiplayerMode>(out var mode) || !mode.IsHosting) return;

            // Identify the sending client
            var eventRegistry = services.TryResolve<IGameEventRegistry>(out var reg) ? reg : null;
            var senderPeerId = (eventRegistry as GameEventRegistry)?.CurrentSenderPeerId ?? -1;

            if (!services.TryResolve<PlayerRegistry>(out var registry)) return;
            var slot = registry.GetSlotByPeerId(senderPeerId);
            if (slot == null)
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[MirrorEventComplete] From unknown peer {senderPeerId}");
                return;
            }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[MirrorEventComplete] Player '{slot.PlayerName}' (slot {slot.SlotIndex}) chose: action={e.Action}");

            if (!services.TryResolve<CoopStateManager>(out var coopState)) return;
            var playerState = coopState.GetPlayerState(slot.SlotIndex);
            if (playerState == null)
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[MirrorEventComplete] No CoopPlayerState for slot {slot.SlotIndex}");
                return;
            }

            if (e.Action == "remove_one")
            {
                HandleRemoveOne(playerState, e);
            }
            else if (e.Action == "remove_all")
            {
                HandleRemoveAll(playerState);
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[MirrorEventComplete] Handler failed: {ex.Message}");
        }
    }

    private void HandleRemoveOne(CoopPlayerState playerState, MirrorEventCompleteEvent e)
    {
        // Remove the specified orb by GUID first, then by name as fallback
        var removed = false;
        if (!string.IsNullOrEmpty(e.RemovedOrbGuid))
        {
            var idx = playerState.CompleteDeck.FindIndex(o => o.Guid == e.RemovedOrbGuid);
            if (idx >= 0)
            {
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[MirrorEventComplete] Removed orb by GUID: {playerState.CompleteDeck[idx].PrefabName} ({e.RemovedOrbGuid})");
                playerState.CompleteDeck.RemoveAt(idx);
                removed = true;
            }
        }

        if (!removed && !string.IsNullOrEmpty(e.RemovedOrbName))
        {
            var idx = playerState.CompleteDeck.FindIndex(o => o.PrefabName == e.RemovedOrbName);
            if (idx >= 0)
            {
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[MirrorEventComplete] Removed orb by name: {e.RemovedOrbName}");
                playerState.CompleteDeck.RemoveAt(idx);
                removed = true;
            }
        }

        if (!removed)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[MirrorEventComplete] Could not find orb to remove: name='{e.RemovedOrbName}', guid='{e.RemovedOrbGuid}'");
        }

        MultiplayerPlugin.Logger?.LogInfo(
            $"[MirrorEventComplete] Deck now has {playerState.CompleteDeck.Count} orbs");
    }

    private void HandleRemoveAll(CoopPlayerState playerState)
    {
        // Remove all non-CannotBeRemoved orbs
        // We check the CannotBeRemoved component by looking up the prefab
        var removedCount = 0;
        for (int i = playerState.CompleteDeck.Count - 1; i >= 0; i--)
        {
            var orb = playerState.CompleteDeck[i];
            // Check if the orb prefab has CannotBeRemoved component
            if (!IsCannotBeRemoved(orb.PrefabName))
            {
                playerState.CompleteDeck.RemoveAt(i);
                removedCount++;
            }
        }

        MultiplayerPlugin.Logger?.LogInfo(
            $"[MirrorEventComplete] Removed {removedCount} orbs from deck");

        // Find the mirror event's OrbPool and add its orbs
        // The OrbPool is referenced by the dialogue system — we search for OrbPools
        // that look like the Orboros pool (contains orbs not already in common pools)
        AddMirrorReplacementOrbs(playerState);
    }

    private void AddMirrorReplacementOrbs(CoopPlayerState playerState)
    {
        try
        {
            // The mirror event's "Remove All" grants Orboros from an OrbPool.
            // The host's DeckManager was just called with this OrbPool (or will be).
            // We capture the replacement orbs by finding the OrbPool used by the
            // mirror dialogue — typically named something containing "orboros" or "mirror".
            // As a fallback, we look at what the host's DeckManager just added.
            var allPools = Resources.FindObjectsOfTypeAll<OrbPool>();
            OrbPool mirrorPool = null;

            foreach (var pool in allPools)
            {
                if (pool.name != null &&
                    (pool.name.IndexOf("orboros", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     pool.name.IndexOf("mirror", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    mirrorPool = pool;
                    break;
                }
            }

            if (mirrorPool != null && mirrorPool.AvailableOrbs != null)
            {
                foreach (var orbPrefab in mirrorPool.AvailableOrbs)
                {
                    if (orbPrefab == null) continue;
                    var attack = orbPrefab.GetComponent<Attack>();
                    playerState.CompleteDeck.Add(new SerializedOrb
                    {
                        PrefabName = orbPrefab.name,
                        Guid = Guid.NewGuid().ToString(),
                        Level = attack?.Level ?? 1,
                    });
                }
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[MirrorEventComplete] Added {mirrorPool.AvailableOrbs.Length} replacement orbs from pool '{mirrorPool.name}'");
            }
            else
            {
                // Fallback: check what the host's DeckManager currently has after "Remove All"
                // The host's deck was just modified by DialogueRemoveAllOrbsAndAddNew
                var hostDeck = DeckManager.completeDeck;
                if (hostDeck != null)
                {
                    foreach (var orbGo in hostDeck)
                    {
                        if (orbGo == null) continue;
                        var attack = orbGo.GetComponent<Attack>();
                        // Add orbs that aren't already in the client's deck
                        // (the host's deck post-mirror has only the replacement orbs + CannotBeRemoved)
                        var prefabName = orbGo.name.Replace("(Clone)", "").Trim();
                        if (playerState.CompleteDeck.All(o => o.PrefabName != prefabName))
                        {
                            playerState.CompleteDeck.Add(new SerializedOrb
                            {
                                PrefabName = prefabName,
                                Guid = Guid.NewGuid().ToString(),
                                Level = attack?.Level ?? 1,
                            });
                        }
                    }
                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[MirrorEventComplete] Used host's current deck as fallback for replacement orbs");
                }
                else
                {
                    MultiplayerPlugin.Logger?.LogWarning(
                        "[MirrorEventComplete] Could not find mirror OrbPool or host deck for replacement orbs");
                }
            }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[MirrorEventComplete] Final deck has {playerState.CompleteDeck.Count} orbs");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[MirrorEventComplete] AddMirrorReplacementOrbs failed: {ex.Message}");
        }
    }

    private static bool IsCannotBeRemoved(string prefabName)
    {
        try
        {
            // Look up the prefab and check for CannotBeRemoved component
            var allAttacks = Resources.FindObjectsOfTypeAll<Attack>();
            foreach (var attack in allAttacks)
            {
                if (attack.gameObject.name == prefabName && attack.gameObject.scene.name == null)
                {
                    return attack.GetComponent<CannotBeRemoved>() != null;
                }
            }
        }
        catch { }
        return false;
    }
}
