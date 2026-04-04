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
    public int ActivePlayerSlot { get; private set; } = -1;
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
        // Deck loading is complex — for now, we store/restore the state but actual
        // deck manipulation requires resolving orb prefabs back to GameObjects.
        // This will be fully implemented when the turn system needs it.
        _log.LogInfo($"[CoopState] LoadDeckState for slot {state.SlotIndex} (deck={state.CompleteDeck.Count} orbs)");
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
        _log.LogInfo($"[CoopState] LoadRelicState for slot {state.SlotIndex} ({state.OwnedRelics.Count} relics)");
    }

    // --- Health save/load ---

    private void SaveHealthState(CoopPlayerState state)
    {
        try
        {
            var phc = UnityEngine.Object.FindObjectOfType<Battle.PlayerHealthController>();
            if (phc == null) return;

            state.CurrentHealth = phc.CurrentHealth;

            var maxField = AccessTools.Field(typeof(Battle.PlayerHealthController), "_maxPlayerHealth");
            var maxVar = maxField?.GetValue(phc);
            if (maxVar != null)
            {
                var valProp = maxVar.GetType().GetProperty("Value");
                state.MaxHealth = valProp != null ? (float)valProp.GetValue(maxVar) : 0;
            }
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
            if (phc == null) return;

            var healthField = AccessTools.Field(typeof(Battle.PlayerHealthController), "_playerHealth");
            var maxField = AccessTools.Field(typeof(Battle.PlayerHealthController), "_maxPlayerHealth");

            var healthVar = healthField?.GetValue(phc);
            var maxVar = maxField?.GetValue(phc);

            if (healthVar != null)
            {
                var valProp = healthVar.GetType().GetProperty("Value");
                valProp?.SetValue(healthVar, state.CurrentHealth);
            }
            if (maxVar != null)
            {
                var valProp = maxVar.GetType().GetProperty("Value");
                valProp?.SetValue(maxVar, state.MaxHealth);
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
