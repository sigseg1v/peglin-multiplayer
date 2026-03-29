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
            int guidMatched = 0, indexMatched = 0, typeChanged = 0, destroyed = 0, reactivated = 0, missed = 0;

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

                // Fallback to index match (first sync — both sides loaded same layout)
                if (peg == null && entry.Index >= 0 && entry.Index < clientPegs.Count)
                {
                    peg = clientPegs[entry.Index];
                    if (peg != null)
                    {
                        indexMatched++;
                    }
                }

                if (peg == null)
                {
                    missed++;
                    continue;
                }

                // Register with host GUID
                if (!string.IsNullOrEmpty(entry.Guid))
                    _pegId.Register(peg, entry.Guid);

                // Handle destroyed pegs
                if (entry.IsDestroyed)
                {
                    if (peg.gameObject.activeSelf)
                    {
                        peg.gameObject.SetActive(false);
                        destroyed++;
                    }
                    continue;
                }

                // Reactivate if host says it's alive
                if (!peg.gameObject.activeSelf)
                {
                    peg.gameObject.SetActive(true);
                    reactivated++;
                }

                // Force peg type to match host
                var targetType = (Peg.PegType)entry.PegType;
                if (peg.pegType != targetType)
                {
                    try
                    {
                        if (peg.SupportsPegType(targetType))
                            peg.ConvertPegToType(targetType);
                        else
                            peg.pegType = targetType;
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
            }

            _log.LogInfo($"[PegboardApplier] GUIDMatched={guidMatched}, IndexMatched={indexMatched}, " +
                $"TypeChanged={typeChanged}, Destroyed={destroyed}, Reactivated={reactivated}, Missed={missed} " +
                $"(host={snapshot.TotalPegCount}, client={clientPegs.Count}, " +
                $"crit={snapshot.CritPegCount}, bomb={snapshot.BombPegCount}, reset={snapshot.ResetPegCount}, " +
                $"registry={_pegId.Count})");

            // Diagnostic: log what the client actually has after apply
            LogActualPegState(clientPegs);
        }
        catch (Exception ex)
        {
            _log.LogError($"[PegboardApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>Log actual peg state on client for verification.</summary>
    private void LogActualPegState(List<Peg> pegs)
    {
        int active = 0, crits = 0, bombs = 0, resets = 0, regular = 0;
        foreach (var peg in pegs)
        {
            if (peg == null || !peg.gameObject.activeSelf) continue;
            active++;
            var pt = (int)peg.pegType;
            if ((pt & 0x2) != 0) crits++;
            else if ((pt & 0x4) != 0) bombs++;
            else if ((pt & 0x8) != 0) resets++;
            else if ((pt & 0x1) != 0) regular++;
        }
        _log.LogInfo($"[PegboardApplier] CLIENT ACTUAL: {active} active pegs " +
            $"(regular={regular}, crit={crits}, bomb={bombs}, reset={resets})");
    }
}
