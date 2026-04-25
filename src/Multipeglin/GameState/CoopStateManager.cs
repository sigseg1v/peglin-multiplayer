using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;
using Multipeglin.Multiplayer;
using Multipeglin.Utility;
using Relics;
using UnityEngine;

namespace Multipeglin.GameState;

/// <summary>
/// Host-side manager that maintains per-player state (deck, relics, health, gold)
/// and swaps it in/out of the game's singletons when changing active players.
/// </summary>
public class CoopStateManager
{
    private readonly ManualLogSource _log;
    private readonly PlayerRegistry _playerRegistry;
    private OrbIdentifier _orbId;

    public Dictionary<int, CoopPlayerState> PlayerStates { get; } = new Dictionary<int, CoopPlayerState>();
    public int ActivePlayerSlot { get; internal set; } = -1;
    public int TotalPlayerCount => PlayerStates.Count;

    /// <summary>
    /// When true, suppress status effect UI updates on the host machine.
    /// Set to true when a non-host player's state is loaded into the singletons,
    /// so the host's screen doesn't display the other player's status effects
    /// (e.g. Ballusion from client's Flaunty Gauntlets during client's turn).
    /// </summary>
    public static bool SuppressStatusEffectUI { get; set; }

    public CoopStateManager(ManualLogSource log, PlayerRegistry playerRegistry)
    {
        _log = log;
        _playerRegistry = playerRegistry;
    }

    /// <summary>
    /// Late-inject OrbIdentifier (avoids circular DI dependency since CoopStateManager
    /// is created before OrbIdentifier in the registration order).
    /// </summary>
    public void SetOrbIdentifier(OrbIdentifier orbId) => _orbId = orbId;

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
        SaveStatusEffects(state);
        state.IsInitialized = true;

        _log.LogInfo($"[CoopState] Captured initial state for slot {slotIndex}: " +
            $"hp={state.CurrentHealth}/{state.MaxHealth}, deck={state.CompleteDeck.Count}, " +
            $"relics={state.OwnedRelics.Count}, statusEffects={state.StatusEffects.Count}");
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
        SaveStatusEffects(state);

        _log.LogInfo($"[CoopState] Saved slot {ActivePlayerSlot}: " +
            $"hp={state.CurrentHealth}/{state.MaxHealth}, deck={state.CompleteDeck.Count}, " +
            $"relics={state.OwnedRelics.Count}, statusEffects={state.StatusEffects.Count}");
    }

    /// <summary>Load a player's state into the game singletons.</summary>
    public void LoadPlayerState(int slotIndex)
    {
        if (!PlayerStates.TryGetValue(slotIndex, out var state)) return;

        LoadDeckState(state);
        LoadRelicState(state);
        LoadHealthState(state);
        LoadGoldState(state);
        LoadStatusEffects(state);

        ActivePlayerSlot = slotIndex;

        _log.LogInfo($"[CoopState] Loaded slot {slotIndex}: " +
            $"hp={state.CurrentHealth}/{state.MaxHealth}, deck={state.CompleteDeck.Count}, " +
            $"relics={state.OwnedRelics.Count}, statusEffects={state.StatusEffects.Count}");
    }

    /// <summary>Save current player, load next player.</summary>
    public void SwapToPlayer(int slotIndex)
    {
        if (slotIndex == ActivePlayerSlot) return;

        if (ActivePlayerSlot >= 0)
            SaveActivePlayerState();

        LoadPlayerState(slotIndex);
    }

    /// <summary>
    /// Generate treasure relic choices for each non-host player and send
    /// RelicChoicesEvent so they see CoopRewardUI. The host picks natively
    /// from the chest. Called when the host enters the Treasure scene.
    /// </summary>
    public void SendTreasureRelicChoicesToClients()
    {
        if (TotalPlayerCount < 2) return;
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<Events.IGameEventRegistry>(out var registry) != true) return;

            var relicMgr = Resources.FindObjectsOfTypeAll<Relics.RelicManager>()?.FirstOrDefault();
            if (relicMgr == null) { _log.LogWarning("[CoopState] TreasureRelics: RelicManager null"); return; }

            // Set up CoopRewardState so RelicChoiceClientHandler knows this is treasure
            Events.Handlers.Coop.CoopRewardState.HostRelicSelectionActive = true;
            Events.Handlers.Coop.CoopRewardState.HostHasChosenRelic = true; // Host picks natively
            Events.Handlers.Coop.CoopRewardState.ClientRelicChoicesReceived.Clear();

            int clientCount = 0;
            foreach (var kvp in PlayerStates)
            {
                if (kvp.Key == 0) continue; // Host picks natively from chest
                clientCount++;

                // Generate 3 common relic choices for this client
                var relics = relicMgr.GetMultipleRelicsOffOfQueue(3, Relics.RelicRarity.COMMON);
                var choices = new System.Collections.Generic.List<Snapshots.RelicEntry>();

                foreach (var relic in relics)
                {
                    string displayName = relic.locKey ?? "Unknown";
                    try
                    {
                        var translated = I2.Loc.LocalizationManager.GetTranslation("Relics/" + relic.locKey);
                        if (!string.IsNullOrEmpty(translated)) displayName = translated;
                    }
                    catch { }

                    string description = relic.locKey ?? "";
                    try
                    {
                        var translated = I2.Loc.LocalizationManager.GetTranslation("Relics/" + relic.locKey + "_desc");
                        if (!string.IsNullOrEmpty(translated)) description = translated;
                    }
                    catch { }

                    choices.Add(new Snapshots.RelicEntry
                    {
                        Effect = (int)relic.effect,
                        EffectName = displayName,
                        LocKey = relic.locKey,
                        Rarity = (int)relic.globalRarity,
                        IsEnabled = true,
                    });
                }

                registry.Dispatch(new Events.Network.Coop.RelicChoicesEvent
                {
                    TargetSlotIndex = kvp.Key,
                    Choices = choices,
                });

                _log.LogInfo($"[CoopState] Treasure: sent {choices.Count} relic choices to slot {kvp.Key}");
            }

            Events.Handlers.Coop.CoopRewardState.TotalClientsExpected = clientCount;
            Events.Handlers.Coop.CoopRewardState.PendingGameInitInstance = null; // Not GameInit → no LoadMapScene on completion
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopState] SendTreasureRelicChoices failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Distribute gold earned during battle to all NON-ACTIVE players.
    /// The active player already receives gold via the CurrencyManager singleton.
    /// Call this from CurrencySubscriptions.OnGoldAdded so peg-hit gold is shared.
    /// </summary>
    public void DistributeGoldToInactivePlayers(int amount)
    {
        if (amount <= 0) return;
        foreach (var kvp in PlayerStates)
        {
            if (kvp.Key == ActivePlayerSlot) continue; // Active player gets it via singleton
            kvp.Value.Gold += amount;
        }
        _log.LogInfo($"[CoopState] Distributed +{amount} gold to {PlayerStates.Count - 1} inactive player(s)");
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

    /// <summary>Check if ALL initialized players are dead.</summary>
    public bool AllPlayersDead => PlayerStates.Values.Where(s => s.IsInitialized).All(s => s.CurrentHealth <= 0);

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

            // Guard: if all completeDeck objects are destroyed (Unity-null from scene unload),
            // don't overwrite CoopPlayerState with empty data.
            if (DeckManager.completeDeck != null && DeckManager.completeDeck.Count > 0)
            {
                bool anyValid = false;
                foreach (var orb in DeckManager.completeDeck)
                    if (orb != null) { anyValid = true; break; }
                if (!anyValid && state.CompleteDeck.Count > 0)
                {
                    _log.LogInfo($"[CoopState] SaveDeckState: singleton orbs are destroyed, preserving {state.CompleteDeck.Count} existing orbs");
                    return;
                }
            }

            int prevComplete = state.CompleteDeck.Count;
            int prevBattle = state.BattleDeck.Count;
            int prevShuffled = state.ShuffledOrder.Count;

            state.CompleteDeck.Clear();
            if (DeckManager.completeDeck != null)
            {
                foreach (var orb in DeckManager.completeDeck)
                {
                    if (orb == null) continue;
                    var attack = orb.GetComponent<Battle.Attacks.Attack>();
                    var persist = orb.GetComponent<Battle.Pachinko.PersistentOrb>();
                    state.CompleteDeck.Add(new SerializedOrb
                    {
                        PrefabName = orb.name.Replace("(Clone)", "").Trim(),
                        Guid = _orbId?.GetGuid(orb),
                        Level = attack?.Level ?? 0,
                        RemainingPersistence = persist != null ? persist.remainingPersistence : -1,
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
                    var persist = orb.GetComponent<Battle.Pachinko.PersistentOrb>();
                    state.BattleDeck.Add(new SerializedOrb
                    {
                        PrefabName = orb.name.Replace("(Clone)", "").Trim(),
                        Guid = _orbId?.GetGuid(orb),
                        Level = attack?.Level ?? 0,
                        RemainingPersistence = persist != null ? persist.remainingPersistence : -1,
                    });
                }
            }

            state.ShuffledOrder.Clear();
            if (deckMgr.shuffledDeck != null)
            {
                foreach (var orb in deckMgr.shuffledDeck)
                {
                    if (orb == null) continue;
                    // Save GUID if available, otherwise fall back to prefab name
                    var guid = _orbId?.GetGuid(orb);
                    state.ShuffledOrder.Add(guid ?? orb.name.Replace("(Clone)", "").Trim());
                }
            }

            // CurrentOrb represents the orb that has been drawn from the deck and is
            // actively being aimed/fired.  For non-active players no orb has been drawn,
            // so leave it empty.  The active player's CurrentOrb is captured live from
            // BattleController.activePachinkoBall by DeckStateProvider.
            state.CurrentOrb = "";

            _log.LogInfo($"[CoopState] SaveDeckState slot {state.SlotIndex}: " +
                $"complete {prevComplete}->{state.CompleteDeck.Count}, " +
                $"battle {prevBattle}->{state.BattleDeck.Count}, " +
                $"shuffled {prevShuffled}->{state.ShuffledOrder.Count}");
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
                    UnityEngine.Object.DontDestroyOnLoad(instance);
                    // Each orb clone needs a unique instanceID so DeckManager.IsAttackUnique()
                    // can distinguish duplicates. Without this, all clones from the same prefab
                    // share the same instanceID, making IsAttackUnique always return true
                    // (breaks Spinventoriginality / UNIQUE_ORBS_BUFF).
                    var attack = instance.GetComponent<Battle.Attacks.Attack>();
                    if (attack != null) attack.SetInstanceId();
                    if (!string.IsNullOrEmpty(orb.Guid) && _orbId != null)
                        _orbId.Register(instance, orb.Guid);
                    if (orb.RemainingPersistence >= 0)
                    {
                        var persist = instance.GetComponent<Battle.Pachinko.PersistentOrb>();
                        if (persist != null) persist.remainingPersistence = orb.RemainingPersistence;
                    }
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
                    UnityEngine.Object.DontDestroyOnLoad(instance);
                    var attack = instance.GetComponent<Battle.Attacks.Attack>();
                    if (attack != null) attack.SetInstanceId();
                    if (!string.IsNullOrEmpty(orb.Guid) && _orbId != null)
                        _orbId.Register(instance, orb.Guid);
                    if (orb.RemainingPersistence >= 0)
                    {
                        var persist = instance.GetComponent<Battle.Pachinko.PersistentOrb>();
                        if (persist != null) persist.remainingPersistence = orb.RemainingPersistence;
                    }
                    deckMgr.battleDeck.Add(instance);
                }
            }

            // Rebuild shuffledDeck from shuffled order (GUIDs or prefab names)
            deckMgr.shuffledDeck.Clear();
            if (state.ShuffledOrder != null)
            {
                // Push in reverse — stack is LIFO, index 0 = top = first draw
                for (int i = state.ShuffledOrder.Count - 1; i >= 0; i--)
                {
                    var entry = state.ShuffledOrder[i];
                    GameObject match = null;

                    // Try GUID lookup first
                    if (_orbId != null)
                        match = _orbId.Find(entry);

                    // Fall back to name matching — should rarely be needed if SaveDeckState always saves GUIDs
                    if (match == null)
                    {
                        _log.LogWarning($"[CoopState] LoadDeckState: GUID lookup failed for shuffledDeck entry '{entry}', falling back to name matching");
                        var name = entry.Replace("(Clone)", "").Trim();
                        foreach (var go in deckMgr.battleDeck)
                        {
                            if (go != null && go.name.Replace("(Clone)", "").Trim() == name)
                            {
                                match = go;
                                _log.LogWarning($"[CoopState] LoadDeckState: name fallback matched '{name}' for entry '{entry}'");
                                break;
                            }
                        }
                    }

                    if (match != null)
                        deckMgr.shuffledDeck.Push(match);
                }
            }

            // NOTE: state.CurrentOrb is intentionally NOT loaded here. CurrentOrb is a
            // display-only field used by DeckStateProvider for client snapshots (the orb
            // name shown in the aimer). It is derived from shuffledDeck.Peek() in
            // SaveDeckState and consumed by the snapshot/applier pipeline — it does not
            // correspond to any game singleton state that needs restoring.

            _log.LogInfo($"[CoopState] LoadDeckState for slot {state.SlotIndex}: " +
                $"complete={DeckManager.completeDeck.Count}, battle={deckMgr.battleDeck.Count}, " +
                $"shuffled={deckMgr.shuffledDeck.Count}");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopState] LoadDeckState failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Rebuild DeckInfoManager._displayOrbs to match the current shuffledDeck.
    /// The game's PlungerPlungeComplete does this after a shuffle animation,
    /// but we need it immediately after a deck swap.
    /// Call AFTER all deck modifications (LoadDeckState + EnsureBattleDeckPopulated)
    /// are complete, right before DrawBall. Must be called at the last moment because
    /// EnsureBattleDeckPopulated may reshuffle and invalidate the display.
    /// </summary>
    public void RebuildDeckInfoDisplay(DeckManager deckMgr = null)
    {
        try
        {
            if (deckMgr == null)
                deckMgr = Resources.FindObjectsOfTypeAll<DeckManager>()?.FirstOrDefault();
            if (deckMgr == null) { _log.LogWarning("[CoopState] RebuildDeckInfoDisplay: DeckManager null"); return; }

            var dim = UnityEngine.Object.FindObjectOfType<DeckInfoManager>();
            if (dim == null) { _log.LogWarning("[CoopState] RebuildDeckInfoDisplay: DeckInfoManager null"); return; }

            // Clear existing display orbs
            var displayOrbsField = AccessTools.Field(typeof(DeckInfoManager), "_displayOrbs");
            var displayOrbs = displayOrbsField?.GetValue(dim) as System.Collections.Generic.Stack<GameObject>;
            if (displayOrbs == null) { _log.LogWarning("[CoopState] RebuildDeckInfoDisplay: _displayOrbs null"); return; }

            foreach (var go in displayOrbs)
                if (go != null) UnityEngine.Object.Destroy(go);
            displayOrbs.Clear();

            // Recreate display orbs from shuffledDeck, replicating the positioning
            // logic from PlungerPlungeComplete so orbs are vertically stacked correctly.
            var createMethod = AccessTools.Method(typeof(DeckInfoManager), "CreatePreviewSprite",
                new[] { typeof(GameObject), typeof(float) });
            if (createMethod == null) { _log.LogWarning("[CoopState] RebuildDeckInfoDisplay: CreatePreviewSprite method null"); return; }

            var plungerParent = AccessTools.Field(typeof(DeckInfoManager), "_plungerParent")?.GetValue(dim) as Transform;
            if (plungerParent == null) { _log.LogWarning("[CoopState] RebuildDeckInfoDisplay: _plungerParent null"); return; }

            var plungerGraphic = AccessTools.Field(typeof(DeckInfoManager), "_plungerGraphic")?.GetValue(dim) as Transform;
            var startPosField = AccessTools.Field(typeof(DeckInfoManager), "_startingPlungerGraphicPosition");
            var startPos = startPosField != null ? (UnityEngine.Vector3)startPosField.GetValue(dim) : UnityEngine.Vector3.zero;
            var topTransform = AccessTools.Field(typeof(DeckInfoManager), "_topTransform")?.GetValue(dim) as Transform;

            float orbSpriteOffset = 0.875f; // DeckInfoManager.ORB_SPRITE_OFFSET
            float fudge = dim.upcomingDisplayOrbFudgeFactor;

            var arr = deckMgr.shuffledDeck.ToArray();
            float yAccum = (float)arr.Length * -orbSpriteOffset;

            // Position the plunger graphic to the starting offset
            if (plungerGraphic != null)
                plungerGraphic.localPosition = new UnityEngine.Vector3(startPos.x, yAccum + startPos.y);

            int created = 0;
            for (int i = arr.Length - 1; i >= 0; i--)
            {
                if (arr[i] == null) continue;
                // Log source orb info for debugging visual issues
                var pb = arr[i].GetComponent<PachinkoBall>();
                var srcSR = arr[i].GetComponentInChildren<UnityEngine.SpriteRenderer>();
                _log.LogInfo($"[CoopState] RebuildDeck orb[{i}]: name={arr[i].name} active={arr[i].activeSelf} " +
                    $"pbSprite={(pb != null ? (pb.sprite != null ? pb.sprite.name : "NULL") : "noPB")} " +
                    $"srcSR={(srcSR != null ? (srcSR.sprite != null ? srcSR.sprite.name : "NULL_sprite") : "NULL_sr")}");

                // Temporarily activate inactive orbs so PachinkoBall.sprite is populated
                // (UpcomingOrbDisplay.Initialize reads it, which is null on inactive clones)
                bool wasActive = arr[i].activeSelf;
                if (!wasActive) arr[i].SetActive(true);
                var previewGO = createMethod.Invoke(dim, new object[] { arr[i], (float)i * 0.01f }) as GameObject;
                if (!wasActive) arr[i].SetActive(false);
                if (previewGO != null)
                {
                    previewGO.transform.parent = plungerParent;
                    var sr = previewGO.GetComponent<UnityEngine.SpriteRenderer>();
                    float spriteHeight = sr != null ? sr.bounds.size.y : 0f;
                    _log.LogInfo($"[CoopState] RebuildDeck preview[{i}]: srNull={sr == null} sprite={(sr?.sprite != null ? sr.sprite.name : "NULL")} " +
                        $"height={spriteHeight:F3} yAccum={yAccum:F3} active={previewGO.activeSelf}");
                    previewGO.transform.localPosition = UnityEngine.Vector3.up * (yAccum + fudge + spriteHeight * 0.5f);
                    yAccum += spriteHeight;
                    displayOrbs.Push(previewGO);
                    created++;
                }
            }

            // Move plunger parent to the top position (skip animation, just snap)
            if (topTransform != null)
                plungerParent.position = new UnityEngine.Vector3(
                    plungerParent.position.x,
                    topTransform.position.y - yAccum,
                    plungerParent.position.z);

            _log.LogInfo($"[CoopState] RebuildDeckInfoDisplay: {created}/{arr.Length} display orbs from shuffledDeck yAccum={yAccum:F3}");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopState] RebuildDeckInfoDisplay failed: {ex.Message}");
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

            // Per-shot / per-battle / per-run counters live in separate dicts —
            // each player's shot tally must be saved/restored on slot swap or
            // the next shooter will inherit the previous shooter's "X uses left".
            CopyDictToState(relicMgr, "_relicRemainingUsesPerShot", state.RelicUsesPerShot);
            CopyDictToState(relicMgr, "_relicRemainingUsesPerBattle", state.RelicUsesPerBattle);
            CopyDictToState(relicMgr, "_relicRemainingUsesPerRun", state.RelicUsesPerRun);
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopState] SaveRelicState failed: {ex.Message}");
        }
    }

    private void CopyDictToState(RelicManager rm, string fieldName, Dictionary<int, int> target)
    {
        target.Clear();
        var f = AccessTools.Field(typeof(RelicManager), fieldName);
        var src = f?.GetValue(rm) as Dictionary<RelicEffect, int>;
        if (src == null) return;
        foreach (var kvp in src)
            target[(int)kvp.Key] = kvp.Value;
    }

    private void CopyStateToDict(RelicManager rm, string fieldName, Dictionary<int, int> source)
    {
        var f = AccessTools.Field(typeof(RelicManager), fieldName);
        var dst = f?.GetValue(rm) as Dictionary<RelicEffect, int>;
        if (dst == null) return;
        foreach (var kvp in source)
            dst[(RelicEffect)kvp.Key] = kvp.Value;
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

            // Add relics directly to the _ownedRelics dictionary via reflection
            // instead of calling AddRelic(). AddRelic fires OnRelicAdded which triggers
            // StateSyncSubscriptions.SyncRelics, broadcasting the swapped-in player's
            // relics to the client and overwriting the client's own relics. It also
            // modifies PersistentPlayerData (save corruption), removes from relic pools,
            // and applies effects (already applied when originally chosen).
            int added = 0;
            foreach (var entry in state.OwnedRelics)
            {
                var effect = (RelicEffect)entry.Effect;
                if (effect == RelicEffect.NONE) continue;

                var relicAsset = allRelicAssets.FirstOrDefault(r => r.effect == effect)
                    ?? allRelicAssets.FirstOrDefault(r => r.locKey == entry.LocKey);

                if (relicAsset != null)
                {
                    try
                    {
                        var owned = ownedField.GetValue(relicMgr) as Dictionary<RelicEffect, Relic>;
                        if (owned != null && !owned.ContainsKey(effect))
                        {
                            owned[effect] = relicAsset;
                            added++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning($"[CoopState] Direct relic add failed for {effect}: {ex.Message}");
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

            // Restore the three usage-counter dicts. Without this, swapping to a
            // player resets per-shot/per-battle/per-run counters to whatever the
            // previous active player had — Pocket Sand uses, Reload Strength
            // stacks, etc. all leak between players.
            CopyStateToDict(relicMgr, "_relicRemainingUsesPerShot", state.RelicUsesPerShot);
            CopyStateToDict(relicMgr, "_relicRemainingUsesPerBattle", state.RelicUsesPerBattle);
            CopyStateToDict(relicMgr, "_relicRemainingUsesPerRun", state.RelicUsesPerRun);

            _log.LogInfo($"[CoopState] LoadRelicState for slot {state.SlotIndex}: {added}/{state.OwnedRelics.Count} relics loaded");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopState] LoadRelicState failed: {ex.Message}");
        }
    }

    // --- Board relic merging for battle init ---

    /// <summary>
    /// Relic effects that modify the shared pegboard at battle start.
    /// These are checked during BattleController.Start() (before OnBattleStarted)
    /// and should reflect ALL players' relics, not just the active player.
    /// </summary>
    private static readonly int[] BoardRelicEffects = new[]
    {
        // Battle start: bombs/coins
        64,  // ADDITIONAL_STARTING_BOMBS (+3 bombs)
        63,  // DOUBLE_BOMBS_ON_MAP (2x bombs)
        98,  // ADDITIONAL_BATTLE_GOLD (+10 coins)
        104, // DOUBLE_COINS_AND_PRICES (3x gold)
        // Peg count modifiers (checked in PegManager.GetPegCount)
        36,  // ADDITIONAL_CRIT1 (+1 crit)
        17,  // ADDITIONAL_CRIT2 (+2 crit)
        18,  // ADDITIONAL_CRIT3 (+3 crit)
        69,  // CRIT_PIT (+2 crit)
        82,  // REDUCE_CRIT (-1 crit)
        37,  // ADDITIONAL_REFRESH1 (+1 refresh)
        20,  // ADDITIONAL_REFRESH2 (+2 refresh)
        21,  // ADDITIONAL_REFRESH3 (+3 refresh)
        81,  // REDUCE_REFRESH (-1 refresh)
        140, // ONLY_REFRESH_X_PEGS (+10 refresh)
        105, // ALL_ORBS_MORBID (halves refresh)
        101, // DUPLICATE_SPECIAL_PEGS (no reset on shuffle)
    };

    /// <summary>
    /// Temporarily merge all players' board-affecting relics into _ownedRelics.
    /// Call this BEFORE BattleController.Start() runs (e.g. in Awake postfix)
    /// so the board setup reflects every player's contributions.
    /// The normal LoadRelicState for the active player restores the correct
    /// state after board init completes.
    /// </summary>
    public void MergeBoardRelics()
    {
        if (TotalPlayerCount < 2) return;
        try
        {
            var relicMgr = Resources.FindObjectsOfTypeAll<RelicManager>()?.FirstOrDefault();
            if (relicMgr == null) return;

            var ownedField = AccessTools.Field(typeof(RelicManager), "_ownedRelics");
            var owned = ownedField?.GetValue(relicMgr) as Dictionary<RelicEffect, Relic>;
            if (owned == null) return;

            var allRelicAssets = Resources.FindObjectsOfTypeAll<Relic>();
            var boardEffectSet = new HashSet<int>(BoardRelicEffects);
            int merged = 0;

            foreach (var kvp in PlayerStates)
            {
                // Skip the active player — their relics are already loaded
                if (kvp.Key == ActivePlayerSlot) continue;

                foreach (var entry in kvp.Value.OwnedRelics)
                {
                    if (!boardEffectSet.Contains(entry.Effect)) continue;

                    var effect = (RelicEffect)entry.Effect;
                    if (owned.ContainsKey(effect)) continue; // Active player already has it

                    var relicAsset = allRelicAssets.FirstOrDefault(r => r.effect == effect);
                    if (relicAsset != null)
                    {
                        owned[effect] = relicAsset;
                        merged++;
                        _log.LogInfo($"[CoopState] MergeBoardRelics: added {effect} from slot {kvp.Key}");
                    }
                }
            }

            if (merged > 0)
                _log.LogInfo($"[CoopState] MergeBoardRelics: merged {merged} board relics from non-active players");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopState] MergeBoardRelics failed: {ex.Message}");
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
                // Clamp negative HP to 0. Enemy attacks exceeding remaining HP set the
                // FloatVariable to a negative value; without clamping the saved state
                // shows -5/100 on the client and TurnManager races vs game-over flow.
                state.CurrentHealth = Mathf.Max(0f, phc.CurrentHealth);

                var maxField = AccessTools.Field(typeof(Battle.PlayerHealthController), "_maxPlayerHealth");
                var maxVar = maxField?.GetValue(phc);
                if (maxVar != null)
                {
                    var valProp = maxVar.GetType().GetProperty("Value");
                    state.MaxHealth = valProp != null ? (float)valProp.GetValue(maxVar) : 0;
                }
                _log.LogInfo($"[CoopState] SaveHealthState slot {state.SlotIndex}: hp={state.CurrentHealth}/{state.MaxHealth}");
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

                // Set max FIRST so that current doesn't get clamped to the old max.
                // FloatVariable.Set may clamp current <= max, so order matters.
                if (maxVar != null)
                {
                    var setMethod = maxVar.GetType().GetMethod("Set", new[] { typeof(float) });
                    setMethod?.Invoke(maxVar, new object[] { state.MaxHealth });
                }
                if (healthVar != null)
                {
                    var setMethod = healthVar.GetType().GetMethod("Set", new[] { typeof(float) });
                    setMethod?.Invoke(healthVar, new object[] { state.CurrentHealth });
                }
                return;
            }

            // Fallback: write directly to FloatVariable ScriptableObject assets
            // Set max first, then current (same reason: avoid clamping)
            var floatVars = Resources.FindObjectsOfTypeAll<FloatVariable>();
            foreach (var fv in floatVars)
            {
                if (fv.name == "MaxPlayerHealth" || fv.name == "maxPlayerHealth")
                    fv.Set(state.MaxHealth);
            }
            foreach (var fv in floatVars)
            {
                if (fv.name == "PlayerHealth" || fv.name == "playerHealth")
                    fv.Set(state.CurrentHealth);
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
            {
                var prevGold = state.Gold;
                state.Gold = currMgr.GoldAmount;
                _log.LogInfo($"[CoopState] SaveGoldState slot {state.SlotIndex}: gold {prevGold}->{state.Gold}");
            }
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

            var beforeGold = currMgr.GoldAmount;
            var diff = state.Gold - beforeGold;
            Patches.MultiplayerClientPatches.AllowCurrencySync = true;
            try
            {
                if (diff > 0)
                    currMgr.AddGold(diff, true);
                else if (diff < 0)
                    currMgr.RemoveGold(-diff, true);
            }
            finally
            {
                Patches.MultiplayerClientPatches.AllowCurrencySync = false;
            }
            _log.LogInfo($"[CoopState] LoadGoldState slot {state.SlotIndex}: gold {beforeGold}->{state.Gold} (diff={diff})");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopState] LoadGoldState failed: {ex.Message}");
        }
    }

    // --- Status effect save/load ---

    private void SaveStatusEffects(CoopPlayerState state)
    {
        try
        {
            var statusCtrl = UnityEngine.Object.FindObjectOfType<Battle.StatusEffects.PlayerStatusEffectController>();
            if (statusCtrl == null)
            {
                // Not in a battle scene — preserve any previously saved effects
                _log.LogInfo($"[CoopState] SaveStatusEffects: PlayerStatusEffectController not found (non-battle scene), " +
                    $"preserving {state.StatusEffects.Count} existing effects for slot {state.SlotIndex}");
                return;
            }

            var effectsList = AccessTools.Field(typeof(Battle.StatusEffects.PlayerStatusEffectController), "_statusEffects")
                ?.GetValue(statusCtrl) as System.Collections.IList;
            if (effectsList == null)
            {
                _log.LogInfo($"[CoopState] SaveStatusEffects: _statusEffects list is null for slot {state.SlotIndex}");
                state.StatusEffects.Clear();
                return;
            }

            state.StatusEffects.Clear();
            foreach (var effect in effectsList)
            {
                var typeField = AccessTools.Field(effect.GetType(), "EffectType");
                var intensityField = AccessTools.Field(effect.GetType(), "Intensity");
                if (typeField == null) continue;

                var effectType = typeField.GetValue(effect);
                var intensity = (int)(intensityField?.GetValue(effect) ?? 0);
                if (intensity <= 0) continue;

                state.StatusEffects.Add(new SerializedStatusEffect
                {
                    EffectType = (int)effectType,
                    Intensity = intensity,
                });
            }

            if (state.StatusEffects.Count > 0)
            {
                var names = string.Join(", ", state.StatusEffects.ConvertAll(
                    e => $"{(Battle.StatusEffects.StatusEffectType)e.EffectType}={e.Intensity}"));
                _log.LogInfo($"[CoopState] SaveStatusEffects slot {state.SlotIndex}: [{names}]");
            }
            else
            {
                _log.LogInfo($"[CoopState] SaveStatusEffects slot {state.SlotIndex}: no active effects");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopState] SaveStatusEffects failed: {ex.Message}");
        }
    }

    private void LoadStatusEffects(CoopPlayerState state)
    {
        try
        {
            // Suppress status effect UI on host when loading a non-host player's state.
            // This prevents the host's screen from showing the other player's effects
            // (e.g. Ballusion from client's Flaunty Gauntlets during the client's turn).
            SuppressStatusEffectUI = state.SlotIndex != 0;

            var statusCtrl = UnityEngine.Object.FindObjectOfType<Battle.StatusEffects.PlayerStatusEffectController>();
            if (statusCtrl == null)
            {
                _log.LogInfo($"[CoopState] LoadStatusEffects: PlayerStatusEffectController not found (non-battle scene), " +
                    $"skipping load for slot {state.SlotIndex} ({state.StatusEffects.Count} saved effects)");
                return;
            }

            var effectsField = AccessTools.Field(typeof(Battle.StatusEffects.PlayerStatusEffectController), "_statusEffects");
            var effects = effectsField?.GetValue(statusCtrl) as List<Battle.StatusEffects.StatusEffect>;
            if (effects == null)
            {
                _log.LogWarning($"[CoopState] LoadStatusEffects: _statusEffects list is null for slot {state.SlotIndex}");
                return;
            }

            // Clear existing effects
            effects.Clear();

            // Restore saved effects directly into the list (no game logic — just raw state)
            foreach (var saved in state.StatusEffects)
            {
                var effectType = (Battle.StatusEffects.StatusEffectType)saved.EffectType;
                if (effectType == Battle.StatusEffects.StatusEffectType.None) continue;
                if (saved.Intensity <= 0) continue;

                effects.Add(new Battle.StatusEffects.StatusEffect(effectType, saved.Intensity));
            }

            // Only update the status effect UI when loading the host's own state (slot 0).
            // For non-host players, the effects are loaded into the list for correct gameplay
            // calculations, but the UI stays showing the host's effects (or cleared).
            if (state.SlotIndex == 0)
            {
                var uiField = AccessTools.Field(typeof(Battle.StatusEffects.PlayerStatusEffectController), "_statusEffectUI");
                var ui = uiField?.GetValue(statusCtrl) as Battle.StatusEffects.StatusEffectIconManager;
                if (ui != null)
                {
                    try
                    {
                        ui.UpdateStatusEffects(effects.ToArray());
                    }
                    catch (Exception uiEx)
                    {
                        _log.LogWarning($"[CoopState] LoadStatusEffects: UI update failed: {uiEx.Message}");
                    }
                }
            }

            if (state.StatusEffects.Count > 0)
            {
                var names = string.Join(", ", state.StatusEffects.ConvertAll(
                    e => $"{(Battle.StatusEffects.StatusEffectType)e.EffectType}={e.Intensity}"));
                _log.LogInfo($"[CoopState] LoadStatusEffects slot {state.SlotIndex}: [{names}]");
            }
            else
            {
                _log.LogInfo($"[CoopState] LoadStatusEffects slot {state.SlotIndex}: no effects to restore");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopState] LoadStatusEffects failed: {ex.Message}");
        }
    }
}
