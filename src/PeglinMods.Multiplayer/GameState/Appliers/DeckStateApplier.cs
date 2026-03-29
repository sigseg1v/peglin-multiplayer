using System;
using System.Collections.Generic;
using Battle.Attacks;
using BepInEx.Logging;
using Loading;
using PeglinMods.Multiplayer.GameState.Snapshots;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeglinMods.Multiplayer.GameState.Appliers;

public class DeckStateApplier : IGameStateApplier<DeckStateSnapshot>
{
    private readonly ManualLogSource _log;

    public DeckStateApplier(ManualLogSource log) => _log = log;

    public void Apply(DeckStateSnapshot snapshot)
    {
        try
        {
            _log.LogInfo($"[DeckApplier] Syncing deck: {snapshot.DeckSize} complete, {snapshot.BattleDeck?.Count ?? 0} battle orbs");

            // Find DeckManager (ScriptableObject — not in scene hierarchy)
            var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
            var dm = dms.Length > 0 ? dms[0] : null;
            if (dm == null)
            {
                _log.LogWarning("[DeckApplier] DeckManager not found");
                return;
            }

            bool deckChanged = false;

            // Sync complete deck
            if (snapshot.CompleteDeck != null && snapshot.CompleteDeck.Count > 0)
            {
                deckChanged |= SyncCompleteDeck(dm, snapshot.CompleteDeck);
            }

            // Sync battle deck
            if (snapshot.BattleDeck != null && snapshot.BattleDeck.Count > 0)
            {
                deckChanged |= SyncBattleDeck(dm, snapshot.BattleDeck);
            }

            _log.LogInfo($"[DeckApplier] Deck sync complete: completeDeck={DeckManager.completeDeck?.Count ?? 0}, " +
                $"battleDeck={dm.battleDeck?.Count ?? 0}, shuffledDeck={dm.shuffledDeck?.Count ?? 0}");

            // Build shuffledDeck in the host's exact order and trigger visual display.
            // ShuffleCompleteDeck is blocked on client, so we build it manually.
            if (SceneManager.GetActiveScene().name == "Battle" &&
                dm.battleDeck != null && dm.battleDeck.Count > 0)
            {
                try
                {
                    bool needsRebuild = dm.shuffledDeck.Count == 0 || deckChanged;

                    // Use host's shuffled order if available
                    if (needsRebuild && snapshot.ShuffledOrder != null && snapshot.ShuffledOrder.Count > 0)
                    {
                        dm.shuffledDeck.Clear();
                        // Push in reverse order — stack is LIFO, index 0 = top = first draw
                        for (int i = snapshot.ShuffledOrder.Count - 1; i >= 0; i--)
                        {
                            var orbName = snapshot.ShuffledOrder[i];
                            // Find matching orb in battleDeck by name
                            GameObject match = null;
                            for (int j = 0; j < dm.battleDeck.Count; j++)
                            {
                                if (dm.battleDeck[j] != null && dm.battleDeck[j].name == orbName)
                                {
                                    match = dm.battleDeck[j];
                                    break;
                                }
                            }
                            if (match != null)
                                dm.shuffledDeck.Push(match);
                        }

                        DeckManager.onDeckShuffled(dm.shuffledDeck.Count);
                        _log.LogInfo($"[DeckApplier] Built shuffledDeck in host order: {dm.shuffledDeck.Count} orbs");
                    }
                    else if (needsRebuild)
                    {
                        // Fallback: no shuffled order from host, use battleDeck order
                        dm.shuffledDeck.Clear();
                        for (int i = dm.battleDeck.Count - 1; i >= 0; i--)
                        {
                            if (dm.battleDeck[i] != null)
                                dm.shuffledDeck.Push(dm.battleDeck[i]);
                        }

                        DeckManager.onDeckShuffled(dm.shuffledDeck.Count);
                        _log.LogInfo($"[DeckApplier] Built shuffledDeck (fallback order): {dm.shuffledDeck.Count} orbs");
                    }
                }
                catch (Exception shuffleEx)
                {
                    _log.LogWarning($"[DeckApplier] Deck display trigger failed: {shuffleEx.Message}\n{shuffleEx.StackTrace}");
                }
            }

            // Diagnostic: log what the game actually sees
            LogActualDeckState(dm);
        }
        catch (Exception ex)
        {
            _log.LogError($"[DeckApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <returns>true if deck changed</returns>
    private bool SyncCompleteDeck(DeckManager dm, List<OrbEntry> hostDeck)
    {
        var completeDeck = DeckManager.completeDeck;
        if (completeDeck == null)
        {
            DeckManager.completeDeck = new List<GameObject>();
            completeDeck = DeckManager.completeDeck;
        }

        // Check if deck already matches (same count and names)
        if (completeDeck.Count == hostDeck.Count)
        {
            bool match = true;
            for (int i = 0; i < hostDeck.Count; i++)
            {
                if (i >= completeDeck.Count || completeDeck[i] == null) { match = false; break; }
                var name = completeDeck[i].GetComponent<Attack>()?.locNameString ?? completeDeck[i].name;
                if (name != hostDeck[i].LocName && completeDeck[i].name != hostDeck[i].Name) { match = false; break; }
            }
            if (match)
            {
                _log.LogInfo("[DeckApplier] Complete deck already matches host");
                return false;
            }
        }

        // Rebuild complete deck from host data
        int loaded = 0;
        var newDeck = new List<GameObject>();
        foreach (var entry in hostDeck)
        {
            try
            {
                // Find orb prefab by name from AssetLoading cache
                var orbGo = AssetLoading.Instance?.GetOrbPrefab(entry.Name);
                if (orbGo != null)
                {
                    var instance = UnityEngine.Object.Instantiate(orbGo);
                    instance.name = entry.Name;
                    instance.SetActive(false);
                    newDeck.Add(instance);
                    loaded++;
                }
                else
                {
                    _log.LogWarning($"[DeckApplier] Orb prefab not found: '{entry.Name}'");
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning($"[DeckApplier] Failed to load orb '{entry.Name}': {ex.Message}");
            }
        }

        // Replace the deck
        foreach (var go in completeDeck)
        {
            if (go != null) UnityEngine.Object.Destroy(go);
        }
        completeDeck.Clear();
        completeDeck.AddRange(newDeck);

        _log.LogInfo($"[DeckApplier] Rebuilt complete deck: {loaded}/{hostDeck.Count} orbs loaded");
        return true;
    }

    /// <returns>true if deck changed</returns>
    private bool SyncBattleDeck(DeckManager dm, List<OrbEntry> hostBattleDeck)
    {
        if (dm.battleDeck == null)
        {
            dm.battleDeck = new List<GameObject>();
        }

        // Only rebuild if counts differ
        if (dm.battleDeck.Count == hostBattleDeck.Count) return false;

        int loaded = 0;
        var newBattleDeck = new List<GameObject>();
        foreach (var entry in hostBattleDeck)
        {
            try
            {
                var orbGo = AssetLoading.Instance?.GetOrbPrefab(entry.Name);
                if (orbGo != null)
                {
                    var instance = UnityEngine.Object.Instantiate(orbGo);
                    instance.name = entry.Name;
                    instance.SetActive(false);
                    newBattleDeck.Add(instance);
                    loaded++;
                }
            }
            catch { }
        }

        foreach (var go in dm.battleDeck)
        {
            if (go != null) UnityEngine.Object.Destroy(go);
        }
        dm.battleDeck.Clear();
        dm.battleDeck.AddRange(newBattleDeck);

        _log.LogInfo($"[DeckApplier] Rebuilt battle deck: {loaded}/{hostBattleDeck.Count} orbs");
        return true;
    }

    /// <summary>Log what the game actually has in its deck state.</summary>
    private void LogActualDeckState(DeckManager dm)
    {
        try
        {
            var completeDeck = DeckManager.completeDeck;
            var battleDeck = dm.battleDeck;
            var shuffledDeck = dm.shuffledDeck;

            _log.LogInfo($"[DeckApplier] CLIENT ACTUAL: complete={completeDeck?.Count ?? 0}, " +
                $"battle={battleDeck?.Count ?? 0}, shuffled={shuffledDeck?.Count ?? 0}");

            if (completeDeck != null)
            {
                for (int i = 0; i < completeDeck.Count; i++)
                {
                    var go = completeDeck[i];
                    var atk = go?.GetComponent<Attack>();
                    _log.LogInfo($"[DeckApplier]   complete[{i}]: {go?.name ?? "NULL"} loc={atk?.locNameString ?? "?"}");
                }
            }

            if (shuffledDeck != null && shuffledDeck.Count > 0)
            {
                var peek = shuffledDeck.Peek();
                var atk = peek?.GetComponent<Attack>();
                _log.LogInfo($"[DeckApplier]   next draw: {peek?.name ?? "NULL"} loc={atk?.locNameString ?? "?"}");
            }
        }
        catch { }
    }
}
