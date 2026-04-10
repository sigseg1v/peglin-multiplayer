using System;
using System.Collections.Generic;
using System.Linq;
using Battle.Attacks;
using BepInEx.Logging;
using DG.Tweening;
using HarmonyLib;
using Loading;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Utility;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeglinMods.Multiplayer.GameState.Appliers;

public class DeckStateApplier : IGameStateApplier<DeckStateSnapshot>
{
    private readonly ManualLogSource _log;
    private readonly OrbIdentifier _orbId;

    public DeckStateApplier(ManualLogSource log, OrbIdentifier orbId)
    {
        _log = log;
        _orbId = orbId;
    }

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
                    // Always rebuild shuffledDeck from host data on every heartbeat.
                    // The client is a dumb canvas — the host's ShuffledOrder is authoritative.
                    // Previous logic (count==0 || deckChanged) broke after trim logic reduced
                    // the count during a round and it never grew back on reshuffle.
                    bool needsRebuild = true;

                    // Use host's shuffled order if available
                    if (needsRebuild && snapshot.ShuffledOrder != null && snapshot.ShuffledOrder.Count > 0)
                    {
                        dm.shuffledDeck.Clear();
                        // Push in reverse order — stack is LIFO, index 0 = top = first draw
                        for (int i = snapshot.ShuffledOrder.Count - 1; i >= 0; i--)
                        {
                            var entry = snapshot.ShuffledOrder[i];
                            GameObject match = null;

                            // Try GUID lookup first (active player path sends 12-char hex GUIDs)
                            bool isGuid = entry.Length == 12 && IsHexString(entry);
                            if (isGuid)
                                match = _orbId.Find(entry);

                            // Fallback to name matching (coop non-active players send prefab names)
                            if (match == null)
                            {
                                var orbName = entry.Replace("(Clone)", "").Trim();
                                for (int j = 0; j < dm.battleDeck.Count; j++)
                                {
                                    if (dm.battleDeck[j] != null &&
                                        dm.battleDeck[j].name.Replace("(Clone)", "").Trim() == orbName)
                                    {
                                        match = dm.battleDeck[j];
                                        break;
                                    }
                                }
                            }

                            if (match != null)
                                dm.shuffledDeck.Push(match);
                        }

                        _log.LogInfo($"[DeckApplier] Built shuffledDeck in host order: {dm.shuffledDeck.Count} orbs");
                    }
                    else if (needsRebuild && snapshot.ShuffledOrder == null)
                    {
                        // Fallback: ShuffledOrder is null (no data at all), use battleDeck order
                        dm.shuffledDeck.Clear();
                        for (int i = dm.battleDeck.Count - 1; i >= 0; i--)
                        {
                            if (dm.battleDeck[i] != null)
                                dm.shuffledDeck.Push(dm.battleDeck[i]);
                        }

                        _log.LogInfo($"[DeckApplier] Built shuffledDeck (fallback order): {dm.shuffledDeck.Count} orbs");
                    }
                    else if (needsRebuild && snapshot.ShuffledOrder != null && snapshot.ShuffledOrder.Count == 0)
                    {
                        // Host explicitly says shuffledDeck is empty (all orbs drawn as active)
                        dm.shuffledDeck.Clear();
                        _log.LogInfo("[DeckApplier] Cleared shuffledDeck — host says deck empty (active orb drawn)");
                    }

                    // Adjust shuffledDeck count to match host, and keep _displayOrbs in sync.
                    // Only trim when host sent actual shuffled data (Count > 0). An empty
                    // ShuffledOrder means "no data" (not "deck is empty"), so don't wipe.
                    if (snapshot.ShuffledOrder != null && snapshot.ShuffledOrder.Count > 0)
                    {
                        int hostCount = snapshot.ShuffledOrder.Count;
                        int popped = 0;

                        // Also trim _displayOrbs to stay in sync with shuffledDeck.
                        // Without this, the deck tube visual stays full while data shrinks.
                        Stack<GameObject> displayOrbs = null;
                        try
                        {
                            var dim = UnityEngine.Object.FindObjectOfType<DeckInfoManager>();
                            if (dim != null)
                            {
                                var displayOrbsField = AccessTools.Field(typeof(DeckInfoManager), "_displayOrbs");
                                displayOrbs = displayOrbsField?.GetValue(dim) as Stack<GameObject>;
                            }
                        }
                        catch (Exception dimEx) { _log.LogWarning($"[DeckApplier] Failed to get _displayOrbs: {dimEx.Message}"); }

                        while (dm.shuffledDeck.Count > hostCount && dm.shuffledDeck.Count > 0)
                        {
                            dm.shuffledDeck.Pop();
                            // Pop corresponding display orb and destroy it
                            if (displayOrbs != null && displayOrbs.Count > 0)
                            {
                                var displayOrb = displayOrbs.Pop();
                                if (displayOrb != null)
                                    UnityEngine.Object.Destroy(displayOrb);
                            }
                            popped++;
                        }
                        if (popped > 0)
                        {
                            _log.LogInfo($"[DeckApplier] Trimmed {popped} orbs from shuffledDeck+displayOrbs to match host count {hostCount}");

                            // Move the plunger parent up to fill the visual gap left by
                            // destroyed display orbs. Each orb slot is ~0.875 units tall.
                            try
                            {
                                var dim = UnityEngine.Object.FindObjectOfType<DeckInfoManager>();
                                if (dim != null)
                                {
                                    var plungerField = AccessTools.Field(typeof(DeckInfoManager), "_plungerParent");
                                    var plungerParent = plungerField?.GetValue(dim) as Transform;
                                    if (plungerParent != null)
                                    {
                                        const float ORB_SPRITE_OFFSET = 0.875f;
                                        plungerParent.position += Vector3.up * (popped * ORB_SPRITE_OFFSET);
                                        _log.LogInfo($"[DeckApplier] Moved plunger parent up by {popped * ORB_SPRITE_OFFSET:F3} to fill gap");
                                    }
                                }
                            }
                            catch (Exception plungerEx)
                            {
                                _log.LogWarning($"[DeckApplier] Failed to adjust plunger position: {plungerEx.Message}");
                            }
                        }
                    }

                    // Rebuild DeckInfoManager visual display to match the new shuffledDeck.
                    // The deck tube UI is driven by _displayOrbs, not by DeckManager directly.
                    // Without this, the visual deck goes stale after turn changes or reshuffles.
                    // ALWAYS rebuild on client every heartbeat tick — the deck tube must converge
                    // to the host state regardless of what happened locally. This is cheap (just
                    // destroys old display orbs and creates new ones from shuffledDeck).
                    {
                        try
                        {
                            var services = MultiplayerPlugin.Services;
                            if (services?.TryResolve<GameState.CoopStateManager>(out var csm) == true)
                                csm.RebuildDeckInfoDisplay(dm);
                        }
                        catch (Exception rebuildEx) { _log.LogWarning($"[DeckApplier] RebuildDeckInfoDisplay call failed: {rebuildEx.Message}"); }
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

            // === Post-apply verification ===
            VerifyDeckState(dm, snapshot);
        }
        catch (Exception ex)
        {
            _log.LogError($"[DeckApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// In coop mode, sync only the active orb display without touching the deck contents.
    /// Each player has their own deck, but the aimer should show whichever orb the
    /// current active player (on the host) is about to fire.
    /// </summary>
    public void ApplyActiveOrbOnly(DeckStateSnapshot snapshot)
    {
        if (snapshot == null || string.IsNullOrEmpty(snapshot.CurrentOrb)) return;
        var scene = SceneManager.GetActiveScene().name;
        if (scene != "Battle") return;

        try
        {
            EnsureAimerOrbShown(snapshot.CurrentOrb);
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[DeckApplier] ApplyActiveOrbOnly failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensure DeckInfoManager shows the correct active orb at the top of the deck UI.
    /// Called every heartbeat. Always replaces _currentOrb with the top displayOrb
    /// so the display converges to host state (dumb canvas approach).
    /// </summary>
    private void EnsureDeckUIShowsActiveOrb(DeckManager dm, string activeOrbName)
    {
        try
        {
            var dim = UnityEngine.Object.FindObjectOfType<DeckInfoManager>();
            if (dim == null) return;

            // Always replace _currentOrb with the top displayOrb. RebuildDeckInfoDisplay
            // recreates ALL displayOrbs from host shuffledDeck every heartbeat, so we must
            // pop the top one for the active display each time. Without this, _currentOrb
            // goes stale after a player shoots (the active orb changes but the display
            // doesn't update because the old code skipped when _currentOrb was non-null).
            var currentOrbField = AccessTools.Field(typeof(DeckInfoManager), "_currentOrb");
            var currentOrb = currentOrbField?.GetValue(dim) as GameObject;
            if (currentOrb != null)
            {
                UnityEngine.Object.Destroy(currentOrb);
                currentOrbField.SetValue(dim, null);
            }

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
    /// Called every heartbeat. If already showing the correct orb, does nothing.
    /// If showing a stale (wrong) orb, refreshes the display.
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
            var ballObjField = AccessTools.Field(typeof(ClientBallRenderer), "_ballObject");
            var rendererField = AccessTools.Field(typeof(ClientBallRenderer), "_ballRenderer");
            var renderCopiedField = AccessTools.Field(typeof(ClientBallRenderer), "_renderCopied");
            bool isAiming = (bool)(aimingField?.GetValue(cbr) ?? false);
            bool isActive = (bool)(activeField?.GetValue(cbr) ?? false);

            var ballObj = ballObjField?.GetValue(cbr) as GameObject;
            var sr = rendererField?.GetValue(cbr) as UnityEngine.SpriteRenderer;
            bool renderCopied = (bool)(renderCopiedField?.GetValue(cbr) ?? false);
            var pos = ballObj?.transform.position ?? UnityEngine.Vector3.zero;
            var scale = ballObj?.transform.localScale ?? UnityEngine.Vector3.zero;
            _log.LogInfo($"[DeckApplier] AimerOrb: aiming={isAiming} active={isActive} " +
                $"pos=({pos.x:F1},{pos.y:F1},{pos.z:F1}) scale=({scale.x:F2},{scale.y:F2}) " +
                $"sprite={sr?.sprite != null} enabled={sr?.enabled} color={sr?.color} " +
                $"material={sr?.material?.name ?? "NULL"} layer={sr?.sortingLayerName} order={sr?.sortingOrder} " +
                $"renderCopied={renderCopied}");

            if (isAiming || isActive)
            {
                // Already showing, but check if the displayed orb is stale (wrong orb name).
                // This happens when the host switches active orb between heartbeats (e.g. coop turn change).
                var cleanActiveOrb = activeOrbName?.Replace("(Clone)", "").Trim();
                var displayedOrb = cbr.CurrentOrbName;
                if (!string.IsNullOrEmpty(cleanActiveOrb) && displayedOrb != cleanActiveOrb)
                {
                    _log.LogInfo($"[DeckApplier] Aimer orb stale: displayed='{displayedOrb}' host='{cleanActiveOrb}', refreshing");
                    cbr.OnOrbDrawn(activeOrbName);
                }
                return;
            }

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
                    UnityEngine.Object.DontDestroyOnLoad(instance);
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

        // Rebuild if counts differ OR if GUIDs don't match (stale instances from previous battle)
        if (dm.battleDeck.Count == hostBattleDeck.Count)
        {
            bool guidsMatch = true;
            for (int i = 0; i < hostBattleDeck.Count; i++)
            {
                var hostGuid = hostBattleDeck[i].Guid;
                if (string.IsNullOrEmpty(hostGuid)) continue;
                var clientGuid = _orbId.GetGuid(dm.battleDeck[i]);
                if (clientGuid != hostGuid) { guidsMatch = false; break; }
            }
            if (guidsMatch) return false;
        }

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
                    UnityEngine.Object.DontDestroyOnLoad(instance);
                    newBattleDeck.Add(instance);
                    loaded++;

                    // Register client instance with host's GUID for shuffledDeck matching
                    if (!string.IsNullOrEmpty(entry.Guid))
                        _orbId.Register(instance, entry.Guid);
                }
            }
            catch (Exception orbEx) { _log.LogWarning($"[DeckApplier] Failed to load battle orb: {orbEx.Message}"); }
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
        catch (Exception logEx) { _log.LogWarning($"[DeckApplier] LogActualDeckState failed: {logEx.Message}"); }
    }

    /// <summary>
    /// Post-apply verification: re-read actual game state and compare with what was sent.
    /// Logs MISMATCH warnings for any differences, INFO on success.
    /// </summary>
    private void VerifyDeckState(DeckManager dm, DeckStateSnapshot snapshot)
    {
        try
        {
            bool allMatch = true;

            int actualComplete = DeckManager.completeDeck?.Count ?? 0;
            int expectedComplete = snapshot.CompleteDeck?.Count ?? 0;
            if (expectedComplete > 0 && actualComplete != expectedComplete)
            {
                _log.LogWarning($"[Verify] MISMATCH completeDeck: actual={actualComplete} expected={expectedComplete}");
                allMatch = false;
            }

            int actualBattle = dm.battleDeck?.Count ?? 0;
            int expectedBattle = snapshot.BattleDeck?.Count ?? 0;
            if (expectedBattle > 0 && actualBattle != expectedBattle)
            {
                _log.LogWarning($"[Verify] MISMATCH battleDeck: actual={actualBattle} expected={expectedBattle}");
                allMatch = false;
            }

            int actualShuffled = dm.shuffledDeck?.Count ?? 0;
            int expectedShuffled = snapshot.ShuffledOrder?.Count ?? 0;
            if (snapshot.ShuffledOrder != null && actualShuffled != expectedShuffled)
            {
                _log.LogWarning($"[Verify] MISMATCH shuffledDeck: actual={actualShuffled} expected={expectedShuffled}");
                allMatch = false;
            }

            try
            {
                var dim = UnityEngine.Object.FindObjectOfType<DeckInfoManager>();
                if (dim != null && dim.displayOrbs != null && actualShuffled > 0)
                {
                    int actualDisplay = dim.displayOrbs.Count;
                    int expectedDisplay = !string.IsNullOrEmpty(snapshot.CurrentOrb) ? actualShuffled - 1 : actualShuffled;
                    if (expectedDisplay >= 0 && actualDisplay != expectedDisplay)
                    {
                        _log.LogWarning($"[Verify] MISMATCH displayOrbs: actual={actualDisplay} expected={expectedDisplay} (shuffled={actualShuffled})");
                        allMatch = false;
                    }
                }
            }
            catch (Exception verifyEx) { _log.LogWarning($"[DeckApplier] VerifyDeckState failed: {verifyEx.Message}"); }

            if (allMatch)
                _log.LogInfo($"[Verify] DeckState OK: complete={actualComplete} battle={actualBattle} shuffled={actualShuffled}");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[Verify] DeckState verification failed: {ex.Message}");
        }
    }

    /// <summary>Check if a string is a valid hexadecimal string (used to distinguish GUIDs from orb names).</summary>
    private static bool IsHexString(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }
        return true;
    }
}
