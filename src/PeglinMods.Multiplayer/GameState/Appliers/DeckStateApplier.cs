using System;
using System.Collections.Generic;
using Battle.Attacks;
using BepInEx.Logging;
using DG.Tweening;
using HarmonyLib;
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

                    // Silently adjust shuffledDeck count to match host (no onBallUsed here —
                    // real-time BallUsedClientHandler drives the visual updates via onBallUsed)
                    if (snapshot.ShuffledOrder != null)
                    {
                        int hostCount = snapshot.ShuffledOrder.Count;
                        while (dm.shuffledDeck.Count > hostCount && dm.shuffledDeck.Count > 0)
                            dm.shuffledDeck.Pop();
                    }
                }
                catch (Exception shuffleEx)
                {
                    _log.LogWarning($"[DeckApplier] Deck display trigger failed: {shuffleEx.Message}\n{shuffleEx.StackTrace}");
                }
            }

            // === DECK UI: ensure the active orb is shown ===
            // The host sends CurrentOrb (the orb being aimed/fired). On every heartbeat,
            // we check if the deck UI shows it. If not, we set it directly.
            // This is the "dumb canvas" approach — no dependency on BallUsedEvent timing.
            var scene = SceneManager.GetActiveScene().name;
            _log.LogInfo($"[DeckApplier] ActiveOrb check: host='{snapshot.CurrentOrb ?? "NULL"}' scene='{scene}'");
            if (!string.IsNullOrEmpty(snapshot.CurrentOrb) && scene == "Battle")
            {
                EnsureDeckUIShowsActiveOrb(dm, snapshot.CurrentOrb);
                EnsureAimerOrbShown(snapshot.CurrentOrb);
            }

            // Diagnostic: log what the game actually sees
            LogActualDeckState(dm);
        }
        catch (Exception ex)
        {
            _log.LogError($"[DeckApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Ensure DeckInfoManager shows the correct active orb at the top of the deck UI.
    /// Called every heartbeat. If _currentOrb is already set, does nothing.
    /// If _currentOrb is null (initial draw was missed), pops from _displayOrbs and sets it.
    /// </summary>
    private void EnsureDeckUIShowsActiveOrb(DeckManager dm, string activeOrbName)
    {
        try
        {
            var dim = UnityEngine.Object.FindObjectOfType<DeckInfoManager>();
            if (dim == null) return;

            // Check if the deck UI already has a current orb displayed
            var currentOrbField = AccessTools.Field(typeof(DeckInfoManager), "_currentOrb");
            var currentOrb = currentOrbField?.GetValue(dim) as GameObject;
            if (currentOrb != null)
            {
                _log.LogInfo($"[DeckApplier] Deck UI already has currentOrb: '{currentOrb.name}' — skipping");
                return;
            }
            _log.LogInfo($"[DeckApplier] Deck UI has NO currentOrb — setting up active orb display");

            // Force-complete shuffle animation if still running
            if (DeckInfoManager.animating)
            {
                var plungerField = AccessTools.Field(typeof(DeckInfoManager), "_plungerParent");
                var plunger = plungerField?.GetValue(dim) as Transform;
                if (plunger != null)
                {
                    int safety = 0;
                    while (DeckInfoManager.animating && safety++ < 5)
                        DOTween.Complete(plunger, true);
                }
            }

            if (dim.displayOrbs == null || dim.displayOrbs.Count == 0)
            {
                _log.LogWarning("[DeckApplier] displayOrbs empty — cannot set active orb in deck UI");
                return;
            }

            // Pop the top display orb and set it as active (same logic as BallUsedClientHandler)
            var nextOrb = dim.displayOrbs.Pop();

            // Kill any ongoing tweens
            var plungerParentField = AccessTools.Field(typeof(DeckInfoManager), "_plungerParent");
            var plungerParent = plungerParentField?.GetValue(dim) as Transform;
            if (plungerParent != null)
                DOTween.Kill(plungerParent);
            DOTween.Kill(nextOrb.transform);

            // Clear stale _nextOrb
            var nextOrbField = AccessTools.Field(typeof(DeckInfoManager), "_nextOrb");
            nextOrbField?.SetValue(dim, null);

            // Get orb height before unparenting
            var spriteRenderer = nextOrb.GetComponent<SpriteRenderer>();
            float orbHeight = spriteRenderer != null ? spriteRenderer.bounds.size.y : 0f;

            // Unparent from plunger so world position is independent
            nextOrb.transform.SetParent(null);

            // Move to the active orb display position
            var displayPosField = AccessTools.Field(typeof(DeckInfoManager), "_currentOrbDisplayPos");
            var displayPos = displayPosField?.GetValue(dim) as Transform;
            if (displayPos != null)
                nextOrb.transform.position = displayPos.position;
            nextOrb.transform.localScale = Vector3.one * 0.85f; // ACTIVE_ORB_DISPLAY_HEIGHT

            // Move plunger up so remaining orbs shift up to fill the gap
            if (plungerParent != null && orbHeight > 0f)
                plungerParent.position += Vector3.up * orbHeight;

            // Set level ring
            var uod = nextOrb.GetComponentInChildren<PeglinUI.OrbDisplay.UpcomingOrbDisplay>();
            int levelIdx = 0;
            if (uod?.attack != null)
                levelIdx = Mathf.Clamp(uod.attack.Level - 1, 0, 2);

            var levelRingField = AccessTools.Field(typeof(DeckInfoManager), "_currentOrbLevelRingRenderer");
            var levelSpritesField = AccessTools.Field(typeof(DeckInfoManager), "_orbLevelDisplaySprites");
            var levelRing = levelRingField?.GetValue(dim) as SpriteRenderer;
            var levelSprites = levelSpritesField?.GetValue(dim) as Sprite[];
            if (levelRing != null && levelSprites != null && levelIdx < levelSprites.Length)
                levelRing.sprite = levelSprites[levelIdx];

            // Activate frame mask
            if (uod?.mainOrbLevelFrameMask != null)
                uod.mainOrbLevelFrameMask.SetActive(true);

            // Set as current orb
            currentOrbField?.SetValue(dim, nextOrb);

            var animator = nextOrb.GetComponentInChildren<Animator>();
            if (animator != null) animator.speed = 1f;

            DeckInfoManager.onActiveOrbScaleStarted?.Invoke(nextOrb);
            DeckInfoManager.onActiveOrbScaleCompleted?.Invoke();
            DeckInfoManager.populatingDisplayOrb = false;
            DeckInfoManager.animating = false;

            _log.LogInfo($"[DeckApplier] Set active orb in deck UI: '{activeOrbName}' at ({displayPos?.position.x:F1},{displayPos?.position.y:F1}), displayOrbs remaining: {dim.displayOrbs.Count}");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[DeckApplier] EnsureDeckUIShowsActiveOrb failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensure ClientBallRenderer shows the orb at the aimer position.
    /// Called every heartbeat. If already showing, does nothing.
    /// </summary>
    private void EnsureAimerOrbShown(string activeOrbName)
    {
        try
        {
            var cbr = ClientBallRenderer.Instance;
            if (cbr == null) return;

            // Check if already showing via the _isAiming or _isActive flags
            var aimingField = AccessTools.Field(typeof(ClientBallRenderer), "_isAiming");
            var activeField = AccessTools.Field(typeof(ClientBallRenderer), "_isActive");
            bool isAiming = (bool)(aimingField?.GetValue(cbr) ?? false);
            bool isActive = (bool)(activeField?.GetValue(cbr) ?? false);

            if (isAiming || isActive) return; // Already showing

            cbr.OnOrbDrawn(activeOrbName);
            _log.LogInfo($"[DeckApplier] Triggered aimer orb display for '{activeOrbName}'");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[DeckApplier] EnsureAimerOrbShown failed: {ex.Message}");
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
