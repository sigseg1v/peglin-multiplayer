using System;
using System.Collections.Generic;
using Battle;
using BepInEx.Logging;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Utility;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState.Appliers;

/// <summary>
/// Syncs pegboard state from host to client using GUID-based tracking.
/// 1. On first sync: match by index in PegManager.allPegs, assign host GUIDs
/// 2. On subsequent syncs: match by GUID only
/// 3. Force peg types to match host
/// 4. Deactivate destroyed pegs
/// </summary>
public class PegboardStateApplier : IGameStateApplier<PegboardStateSnapshot>
{
    private readonly ManualLogSource _log;
    private readonly PegIdentifier _pegId;

    public PegboardStateApplier(ManualLogSource log, PegIdentifier pegId)
    {
        _log = log;
        _pegId = pegId;
    }

    public void Apply(PegboardStateSnapshot snapshot)
    {
        try
        {
            if (snapshot.Pegs == null || snapshot.Pegs.Count == 0)
            {
                _log.LogInfo("[PegboardApplier] No pegs in snapshot.");
                return;
            }

            // Get pegs from PegManager — same authoritative list as the provider.
            // PegManager is a plain C# class, accessed via BattleController.
            var bc = UnityEngine.Object.FindObjectOfType<Battle.BattleController>();
            var pm = bc?.pegManager;
            if (pm == null || pm.allPegs == null)
            {
                _log.LogInfo($"[PegboardApplier] No PegManager in scene. Snapshot has {snapshot.TotalPegCount} pegs.");
                return;
            }

            var clientPegs = pm.allPegs;
            // Also get bombs list (bombs are separate from allPegs)
            var bombsField = HarmonyLib.AccessTools.Field(typeof(Battle.PegManager), "_bombs");
            var clientBombs = bombsField?.GetValue(pm) as System.Collections.Generic.List<Bomb>;

            int guidMatched = 0, indexMatched = 0, typeChanged = 0, destroyed = 0, reactivated = 0, missed = 0;
            var matchedPegs = new HashSet<Peg>();

            foreach (var entry in snapshot.Pegs)
            {
                Peg peg = null;

                // Try GUID match first (works on subsequent syncs)
                if (!string.IsNullOrEmpty(entry.Guid))
                {
                    peg = _pegId.Find(entry.Guid);
                    if (peg != null)
                    {
                        guidMatched++;
                    }
                }

                // Fallback: match by closest position (handles random layouts where index order differs)
                if (peg == null)
                {
                    Peg closest = null;
                    float closestDist = float.MaxValue;

                    // Search allPegs
                    foreach (var p in clientPegs)
                    {
                        if (p == null || matchedPegs.Contains(p)) continue;
                        float dx = p.transform.position.x - entry.PosX;
                        float dy = p.transform.position.y - entry.PosY;
                        float dist = dx * dx + dy * dy;
                        if (dist < closestDist) { closestDist = dist; closest = p; }
                    }
                    // Also search bombs
                    if (clientBombs != null)
                    {
                        foreach (var b in clientBombs)
                        {
                            if (b == null || matchedPegs.Contains(b)) continue;
                            float dx = b.transform.position.x - entry.PosX;
                            float dy = b.transform.position.y - entry.PosY;
                            float dist = dx * dx + dy * dy;
                            if (dist < closestDist) { closestDist = dist; closest = b; }
                        }
                    }

                    if (closest != null && closestDist < 1f) // within 1 unit
                    {
                        peg = closest;
                        indexMatched++; // reusing counter for position matches
                    }
                }

                if (peg == null)
                {
                    missed++;
                    continue;
                }

                matchedPegs.Add(peg);

                // Register with host GUID
                if (!string.IsNullOrEmpty(entry.Guid))
                    _pegId.Register(peg, entry.Guid);

                // Check if the client peg is functionally popped (collider disabled).
                // peg.Cleared is unreliable — _cleared stays true after Reset().
                bool clientPopped = false;
                try { clientPopped = peg.IsDisabled(); } catch { }

                // Handle cleared/popped pegs — host says popped, make client match
                if (entry.IsCleared && !clientPopped)
                {
                    try { peg.PegActivated(playAudio: false, forcePop: true); }
                    catch { }
                }

                // Handle destroyed pegs — use DestroyPeg for proper visual/collider cleanup
                if (entry.IsDestroyed)
                {
                    if (peg.gameObject.activeSelf && peg.pegType != Peg.PegType.DESTROYED)
                    {
                        try { peg.DestroyPeg(peg.pegType); }
                        catch { peg.gameObject.SetActive(false); }
                        destroyed++;
                    }
                    continue;
                }

                // Reactivate if host says peg is alive but client has it disabled/popped.
                // Must clear _cleared flag before Reset() so sprite shows _regularSprite
                // instead of _previouslyClearedSprite (Reset never clears _cleared itself).
                if (!entry.IsCleared && !entry.IsDestroyed)
                {
                    if (!peg.gameObject.activeSelf || peg.pegType == Peg.PegType.DESTROYED || clientPopped)
                    {
                        var clearedField = HarmonyLib.AccessTools.Field(typeof(Peg), "_cleared");
                        clearedField?.SetValue(peg, false);

                        peg.gameObject.SetActive(true);
                        try { peg.Reset(false); } catch { }
                        reactivated++;
                    }
                }

                // Force peg type to match host
                var targetType = (Peg.PegType)entry.PegType;
                if (peg.pegType != targetType)
                {
                    try
                    {
                        if (peg.SupportsPegType(targetType))
                        {
                            var result = peg.ConvertPegToType(targetType);
                            // When converting to BOMB, a new Bomb GameObject is created as a child.
                            // Re-register the GUID with the new Bomb component so future lookups work.
                            if (targetType == Peg.PegType.BOMB && result != null && result != peg.gameObject)
                            {
                                var newBomb = result.GetComponent<Peg>();
                                if (newBomb != null && !string.IsNullOrEmpty(entry.Guid))
                                {
                                    _pegId.Register(newBomb, entry.Guid);
                                }
                            }
                        }
                        else
                        {
                            peg.pegType = targetType;
                        }
                    }
                    catch
                    {
                        peg.pegType = targetType;
                    }
                    typeChanged++;
                }

                // Apply slime type
                var targetSlime = (Peg.SlimeType)entry.SlimeType;
                if (peg.slimeType != targetSlime)
                {
                    try
                    {
                        if (targetSlime == Peg.SlimeType.None)
                            peg.RemoveSlime(true);
                        else
                            peg.ApplySlimeToPeg(targetSlime);
                    }
                    catch { }
                }

                // Apply gold coins — host sends coin count per peg
                if (entry.CoinCount > 0)
                {
                    int currentCoins = peg.NumCoins();
                    for (int c = currentCoins; c < entry.CoinCount; c++)
                    {
                        try { peg.AddCoin(false); } catch { }
                    }
                }

                // Sync bomb hit count — controls fuse lit state and detonation
                if (entry.IsBomb && peg is Bomb bomb)
                {
                    if (bomb.HitCount != entry.HitCount)
                    {
                        bomb.HitCount = entry.HitCount;
                        // Update the animator to show the correct visual state (fuse lit, detonated)
                        try
                        {
                            var animator = bomb.GetComponent<UnityEngine.Animator>();
                            animator?.SetInteger("NumHits", entry.HitCount);
                        }
                        catch { }
                    }
                }
            }

            // Deactivate client pegs not in host snapshot (extras from different layout)
            int extrasRemoved = 0;
            foreach (var peg in clientPegs)
            {
                if (peg != null && peg.gameObject.activeSelf && !matchedPegs.Contains(peg))
                {
                    peg.gameObject.SetActive(false);
                    extrasRemoved++;
                }
            }
            if (clientBombs != null)
            {
                foreach (var bomb in clientBombs)
                {
                    if (bomb != null && bomb.gameObject.activeSelf && !matchedPegs.Contains(bomb))
                    {
                        bomb.gameObject.SetActive(false);
                        extrasRemoved++;
                    }
                }
            }

            int totalClient = clientPegs.Count + (clientBombs?.Count ?? 0);
            _log.LogInfo($"[PegboardApplier] GUIDMatched={guidMatched}, IndexMatched={indexMatched}, " +
                $"TypeChanged={typeChanged}, Destroyed={destroyed}, Reactivated={reactivated}, Missed={missed} " +
                $"(host={snapshot.TotalPegCount}, client={totalClient}, " +
                $"crit={snapshot.CritPegCount}, bomb={snapshot.BombPegCount}, reset={snapshot.ResetPegCount}, " +
                $"registry={_pegId.Count})");

            // Diagnostic: log what the client actually has after apply
            LogActualPegState(clientPegs, clientBombs);
        }
        catch (Exception ex)
        {
            _log.LogError($"[PegboardApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>Log actual peg state on client for verification.</summary>
    private void LogActualPegState(List<Peg> pegs, System.Collections.Generic.List<Bomb> bombs)
    {
        int active = 0, crits = 0, bombCount = 0, resets = 0, regular = 0;
        foreach (var peg in pegs)
        {
            if (peg == null || !peg.gameObject.activeSelf || peg.pegType == Peg.PegType.DESTROYED) continue;
            bool disabled = false;
            try { disabled = peg.IsDisabled(); } catch { }
            if (disabled) continue;
            active++;
            var pt = (int)peg.pegType;
            if ((pt & 0x2) != 0) crits++;
            else if ((pt & 0x8) != 0) resets++;
            else if ((pt & 0x1) != 0) regular++;
        }
        if (bombs != null)
        {
            foreach (var bomb in bombs)
            {
                if (bomb == null || !bomb.gameObject.activeSelf || bomb.pegType == Peg.PegType.DESTROYED) continue;
                bool disabled = false;
                try { disabled = bomb.IsDisabled(); } catch { }
                if (disabled) continue;
                active++;
                bombCount++;
            }
        }
        _log.LogInfo($"[PegboardApplier] CLIENT ACTUAL: {active} active pegs " +
            $"(regular={regular}, crit={crits}, bomb={bombCount}, reset={resets})");
    }
}
