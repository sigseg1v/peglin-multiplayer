using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;
using PeglinMods.Multiplayer.Multiplayer;
using Relics;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState;

/// <summary>
/// Host-side manager that maintains per-player state (deck, relics, health, gold)
/// and swaps it in/out of the game's singletons when changing active players.
/// </summary>
public class CoopStateManager
{
    private readonly ManualLogSource _log;
    private readonly PlayerRegistry _playerRegistry;

    public Dictionary<int, CoopPlayerState> PlayerStates { get; } = new Dictionary<int, CoopPlayerState>();
    public int ActivePlayerSlot { get; internal set; } = -1;
    public int TotalPlayerCount => PlayerStates.Count;

    public CoopStateManager(ManualLogSource log, PlayerRegistry playerRegistry)
    {
        _log = log;
        _playerRegistry = playerRegistry;
    }

    /// <summary>
    /// Initialize a player's starting state from their chosen class.
    /// Called after lobby game start, before the first battle.
    /// </summary>
    public void InitializePlayer(int slotIndex, int chosenClass, string playerName)
    {
        var state = new CoopPlayerState
        {
            SlotIndex = slotIndex,
            PlayerName = playerName,
            ChosenClass = chosenClass,
            CurrentHealth = 0, // Will be set from game state after GameInit
            MaxHealth = 0,
            Gold = 0,
            IsInitialized = false,
        };

        PlayerStates[slotIndex] = state;
        _log.LogInfo($"[CoopState] Initialized player slot {slotIndex}: {playerName}, class={chosenClass}");
    }

    /// <summary>
    /// After GameInit.Start() runs for a player, capture their initial state
    /// (health, starting deck, starting relics) from the singletons.
    /// </summary>
    public void CaptureInitialState(int slotIndex)
    {
        if (!PlayerStates.TryGetValue(slotIndex, out var state)) return;

        SaveDeckState(state);
        SaveRelicState(state);
        SaveHealthState(state);
        SaveGoldState(state);
        state.IsInitialized = true;

        _log.LogInfo($"[CoopState] Captured initial state for slot {slotIndex}: " +
            $"hp={state.CurrentHealth}/{state.MaxHealth}, deck={state.CompleteDeck.Count}, relics={state.OwnedRelics.Count}");
    }

    /// <summary>Save the currently active player's state from game singletons.</summary>
    public void SaveActivePlayerState()
    {
        if (ActivePlayerSlot < 0) return;
        if (!PlayerStates.TryGetValue(ActivePlayerSlot, out var state)) return;

        SaveDeckState(state);
        SaveRelicState(state);
        SaveHealthState(state);
        SaveGoldState(state);

        _log.LogInfo($"[CoopState] Saved slot {ActivePlayerSlot}: " +
            $"hp={state.CurrentHealth}/{state.MaxHealth}, deck={state.CompleteDeck.Count}, relics={state.OwnedRelics.Count}");
    }

    /// <summary>Load a player's state into the game singletons.</summary>
    public void LoadPlayerState(int slotIndex)
    {
        if (!PlayerStates.TryGetValue(slotIndex, out var state)) return;

        LoadDeckState(state);
        LoadRelicState(state);
        LoadHealthState(state);
        LoadGoldState(state);

        ActivePlayerSlot = slotIndex;

        _log.LogInfo($"[CoopState] Loaded slot {slotIndex}: " +
            $"hp={state.CurrentHealth}/{state.MaxHealth}, deck={state.CompleteDeck.Count}, relics={state.OwnedRelics.Count}");
    }

    /// <summary>Save current player, load next player.</summary>
    public void SwapToPlayer(int slotIndex)
    {
        if (slotIndex == ActivePlayerSlot) return;

        if (ActivePlayerSlot >= 0)
            SaveActivePlayerState();

        LoadPlayerState(slotIndex);
    }

    /// <summary>Apply gold to all players (shared gold gain).</summary>
    public void AddGoldToAll(int amount)
    {
        foreach (var state in PlayerStates.Values)
            state.Gold += amount;
    }

    /// <summary>Apply damage to all players (enemy attacks).</summary>
    public void ApplyDamageToAll(float damage)
    {
        foreach (var state in PlayerStates.Values)
        {
            state.CurrentHealth = Mathf.Max(0, state.CurrentHealth - damage);
        }
    }

    /// <summary>Check if any player is dead.</summary>
    public bool AnyPlayerDead => PlayerStates.Values.Any(s => s.IsInitialized && s.CurrentHealth <= 0);

    public CoopPlayerState GetPlayerState(int slotIndex)
        => PlayerStates.TryGetValue(slotIndex, out var s) ? s : null;

    public void Reset()
    {
        PlayerStates.Clear();
        ActivePlayerSlot = -1;
    }

    // --- Deck save/load ---

    private void SaveDeckState(CoopPlayerState state)
    {
        try
        {
            var deckMgr = Resources.FindObjectsOfTypeAll<DeckManager>()?.FirstOrDefault();
            if (deckMgr == null) return;

            state.CompleteDeck.Clear();
            if (DeckManager.completeDeck != null)
            {
                foreach (var orb in DeckManager.completeDeck)
                {
                    if (orb == null) continue;
                    var attack = orb.GetComponent<Battle.Attacks.Attack>();
                    state.CompleteDeck.Add(new SerializedOrb
                    {
                        PrefabName = orb.name.Replace("(Clone)", "").Trim(),
                        Level = attack?.Level ?? 0,
                    });
                }
            }

            state.BattleDeck.Clear();
            if (deckMgr.battleDeck != null)
            {
                foreach (var orb in deckMgr.battleDeck)
                {
                    if (orb == null) continue;
                    var attack = orb.GetComponent<Battle.Attacks.Attack>();
                    state.BattleDeck.Add(new SerializedOrb
                    {
                        PrefabName = orb.name.Replace("(Clone)", "").Trim(),
                        Level = attack?.Level ?? 0,
                    });
                }
            }

            state.ShuffledOrder.Clear();
            if (deckMgr.shuffledDeck != null)
            {
                foreach (var orb in deckMgr.shuffledDeck)
                {
                    if (orb == null) continue;
                    state.ShuffledOrder.Add(orb.name.Replace("(Clone)", "").Trim());
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopState] SaveDeckState failed: {ex.Message}");
        }
    }

    private void LoadDeckState(CoopPlayerState state)
    {
        try
        {
            var deckMgr = Resources.FindObjectsOfTypeAll<DeckManager>()?.FirstOrDefault();
            if (deckMgr == null) return;

            // Rebuild completeDeck from serialized orb names
            if (DeckManager.completeDeck == null)
                DeckManager.completeDeck = new List<GameObject>();

            // Destroy existing orb instances
            foreach (var go in DeckManager.completeDeck)
                if (go != null) UnityEngine.Object.Destroy(go);
            DeckManager.completeDeck.Clear();

            foreach (var orb in state.CompleteDeck)
            {
                var prefab = Loading.AssetLoading.Instance?.GetOrbPrefab(orb.PrefabName);
                if (prefab != null)
                {
                    var instance = UnityEngine.Object.Instantiate(prefab);
                    instance.name = orb.PrefabName;
                    instance.SetActive(false);
                    DeckManager.completeDeck.Add(instance);
                }
                else
                {
                    _log.LogWarning($"[CoopState] Orb prefab not found: '{orb.PrefabName}'");
                }
            }

            // Rebuild battleDeck
            if (deckMgr.battleDeck == null)
                deckMgr.battleDeck = new List<GameObject>();

            foreach (var go in deckMgr.battleDeck)
                if (go != null) UnityEngine.Object.Destroy(go);
            deckMgr.battleDeck.Clear();

            foreach (var orb in state.BattleDeck)
            {
                var prefab = Loading.AssetLoading.Instance?.GetOrbPrefab(orb.PrefabName);
                if (prefab != null)
                {
                    var instance = UnityEngine.Object.Instantiate(prefab);
                    instance.name = orb.PrefabName;
                    instance.SetActive(false);
                    deckMgr.battleDeck.Add(instance);
                }
            }

            // Rebuild shuffledDeck from shuffled order
            deckMgr.shuffledDeck.Clear();
            if (state.ShuffledOrder != null)
            {
                // Push in reverse — stack is LIFO, index 0 = top = first draw
                for (int i = state.ShuffledOrder.Count - 1; i >= 0; i--)
                {
                    var orbName = state.ShuffledOrder[i];
                    GameObject match = null;
                    foreach (var go in deckMgr.battleDeck)
                    {
                        if (go != null && go.name == orbName)
                        {
                            match = go;
                            break;
                        }
                    }
                    if (match != null)
                        deckMgr.shuffledDeck.Push(match);
                }
            }

            _log.LogInfo($"[CoopState] LoadDeckState for slot {state.SlotIndex}: " +
                $"complete={DeckManager.completeDeck.Count}, battle={deckMgr.battleDeck.Count}, " +
                $"shuffled={deckMgr.shuffledDeck.Count}");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopState] LoadDeckState failed: {ex.Message}");
        }
    }

    // --- Relic save/load ---

    private void SaveRelicState(CoopPlayerState state)
    {
        try
        {
            var relicMgr = Resources.FindObjectsOfTypeAll<RelicManager>()?.FirstOrDefault();
            if (relicMgr == null) return;

            var ownedField = AccessTools.Field(typeof(RelicManager), "_ownedRelics");
            var countdownField = AccessTools.Field(typeof(RelicManager), "_relicRemainingCountdowns");
            if (ownedField == null) return;

            var owned = ownedField.GetValue(relicMgr) as Dictionary<RelicEffect, Relic>;
            if (owned == null) return;

            state.OwnedRelics.Clear();
            foreach (var kvp in owned)
            {
                state.OwnedRelics.Add(new SerializedRelic
                {
                    Effect = (int)kvp.Key,
                    LocKey = kvp.Value?.locKey ?? "",
                    Rarity = (int)(kvp.Value?.globalRarity ?? 0),
                });
            }

            state.RelicCountdowns.Clear();
            var countdowns = countdownField?.GetValue(relicMgr) as Dictionary<RelicEffect, int>;
            if (countdowns != null)
            {
                foreach (var kvp in countdowns)
                    state.RelicCountdowns[(int)kvp.Key] = kvp.Value;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopState] SaveRelicState failed: {ex.Message}");
        }
    }

    private void LoadRelicState(CoopPlayerState state)
    {
        try
        {
            var relicMgr = Resources.FindObjectsOfTypeAll<RelicManager>()?.FirstOrDefault();
            if (relicMgr == null) return;

            // Clear owned relics directly via reflection instead of calling Reset().
            // Reset() fires OnRelicsReset, clears disabled relics, order tracking, usage
            // counters, and relic pools — side effects that can break the game state.
            var ownedField = AccessTools.Field(typeof(RelicManager), "_ownedRelics");
            if (ownedField != null)
            {
                var owned = ownedField.GetValue(relicMgr) as Dictionary<RelicEffect, Relic>;
                owned?.Clear();
            }

            // Find all available Relic ScriptableObject assets
            var allRelicAssets = Resources.FindObjectsOfTypeAll<Relic>();

            int added = 0;
            foreach (var entry in state.OwnedRelics)
            {
                var effect = (RelicEffect)entry.Effect;
                if (effect == RelicEffect.NONE) continue;

                // Find the relic asset by effect or locKey
                var relicAsset = allRelicAssets.FirstOrDefault(r => r.effect == effect)
                    ?? allRelicAssets.FirstOrDefault(r => r.locKey == entry.LocKey);

                if (relicAsset != null)
                {
                    try
                    {
                        relicMgr.AddRelic(relicAsset);
                        added++;
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning($"[CoopState] AddRelic failed for {effect}: {ex.Message}");
                    }
                }
                else
                {
                    _log.LogWarning($"[CoopState] Relic asset not found: effect={effect}, locKey={entry.LocKey}");
                }
            }

            // Restore countdowns
            if (state.RelicCountdowns.Count > 0)
            {
                var countdownField = AccessTools.Field(typeof(RelicManager), "_relicRemainingCountdowns");
                var countdowns = countdownField?.GetValue(relicMgr) as Dictionary<RelicEffect, int>;
                if (countdowns != null)
                {
                    foreach (var kvp in state.RelicCountdowns)
                        countdowns[(RelicEffect)kvp.Key] = kvp.Value;
                }
            }

            _log.LogInfo($"[CoopState] LoadRelicState for slot {state.SlotIndex}: {added}/{state.OwnedRelics.Count} relics loaded");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopState] LoadRelicState failed: {ex.Message}");
        }
    }

    // --- Health save/load ---

    private void SaveHealthState(CoopPlayerState state)
    {
        try
        {
            // Try the battle-scene MonoBehaviour first
            var phc = UnityEngine.Object.FindObjectOfType<Battle.PlayerHealthController>();
            if (phc != null)
            {
                state.CurrentHealth = phc.CurrentHealth;

                var maxField = AccessTools.Field(typeof(Battle.PlayerHealthController), "_maxPlayerHealth");
                var maxVar = maxField?.GetValue(phc);
                if (maxVar != null)
                {
                    var valProp = maxVar.GetType().GetProperty("Value");
                    state.MaxHealth = valProp != null ? (float)valProp.GetValue(maxVar) : 0;
                }
                return;
            }

            // Fallback: read directly from FloatVariable ScriptableObject assets.
            // PlayerHealthController may not exist on non-battle scenes (e.g., PostMainMenu
            // where GameInit runs). The FloatVariable SOs are the authoritative source of
            // health data and persist across scenes.
            var floatVars = Resources.FindObjectsOfTypeAll<FloatVariable>();
            foreach (var fv in floatVars)
            {
                if (fv.name == "PlayerHealth" || fv.name == "playerHealth")
                    state.CurrentHealth = fv.Value;
                else if (fv.name == "MaxPlayerHealth" || fv.name == "maxPlayerHealth")
                    state.MaxHealth = fv.Value;
            }

            if (state.CurrentHealth > 0 || state.MaxHealth > 0)
                _log.LogInfo($"[CoopState] SaveHealthState via FloatVariable fallback: hp={state.CurrentHealth}/{state.MaxHealth}");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopState] SaveHealthState failed: {ex.Message}");
        }
    }

    private void LoadHealthState(CoopPlayerState state)
    {
        try
        {
            var phc = UnityEngine.Object.FindObjectOfType<Battle.PlayerHealthController>();
            if (phc != null)
            {
                var healthField = AccessTools.Field(typeof(Battle.PlayerHealthController), "_playerHealth");
                var maxField = AccessTools.Field(typeof(Battle.PlayerHealthController), "_maxPlayerHealth");

                var healthVar = healthField?.GetValue(phc);
                var maxVar = maxField?.GetValue(phc);

                if (healthVar != null)
                {
                    // FloatVariable.Value is read-only; use the Set(float) method instead
                    var setMethod = healthVar.GetType().GetMethod("Set", new[] { typeof(float) });
                    setMethod?.Invoke(healthVar, new object[] { state.CurrentHealth });
                }
                if (maxVar != null)
                {
                    var setMethod = maxVar.GetType().GetMethod("Set", new[] { typeof(float) });
                    setMethod?.Invoke(maxVar, new object[] { state.MaxHealth });
                }
                return;
            }

            // Fallback: write directly to FloatVariable ScriptableObject assets
            var floatVars = Resources.FindObjectsOfTypeAll<FloatVariable>();
            foreach (var fv in floatVars)
            {
                if (fv.name == "PlayerHealth" || fv.name == "playerHealth")
                    fv.Set(state.CurrentHealth);
                else if (fv.name == "MaxPlayerHealth" || fv.name == "maxPlayerHealth")
                    fv.Set(state.MaxHealth);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopState] LoadHealthState failed: {ex.Message}");
        }
    }

    // --- Gold save/load ---

    private void SaveGoldState(CoopPlayerState state)
    {
        try
        {
            var currMgr = Currency.CurrencyManager.Instance;
            if (currMgr != null)
                state.Gold = currMgr.GoldAmount;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopState] SaveGoldState failed: {ex.Message}");
        }
    }

    private void LoadGoldState(CoopPlayerState state)
    {
        try
        {
            var currMgr = Currency.CurrencyManager.Instance;
            if (currMgr == null) return;

            var diff = state.Gold - currMgr.GoldAmount;
            if (diff > 0)
                currMgr.AddGold(diff, true);
            else if (diff < 0)
                currMgr.RemoveGold(-diff, true);
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopState] LoadGoldState failed: {ex.Message}");
        }
    }
}
