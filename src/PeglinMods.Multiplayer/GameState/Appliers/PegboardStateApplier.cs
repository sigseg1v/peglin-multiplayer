using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Utility;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState.Appliers;

/// <summary>
/// Syncs pegboard state from host to client using GUID-based tracking.
/// 1. Match pegs by GUID (primary) or position (fallback for first sync)
/// 2. Force peg types to match host
/// 3. Deactivate destroyed pegs / extra pegs
/// 4. Register all matched pegs with host GUIDs
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

            var livePegs = UnityEngine.Object.FindObjectsOfType<Peg>(true);
            if (livePegs == null || livePegs.Length == 0)
            {
                _log.LogInfo($"[PegboardApplier] No pegs in scene. Snapshot has {snapshot.TotalPegCount} pegs.");
                return;
            }

            var matched = new HashSet<Peg>();
            int guidMatched = 0, posMatched = 0, typeChanged = 0, destroyed = 0, reactivated = 0;

            foreach (var entry in snapshot.Pegs)
            {
                Peg peg = null;

                // Try GUID match first
                if (!string.IsNullOrEmpty(entry.Guid))
                {
                    peg = _pegId.Find(entry.Guid);
                    if (peg != null && !matched.Contains(peg))
                    {
                        guidMatched++;
                    }
                    else
                    {
                        peg = null;
                    }
                }

                // Fallback to position match
                if (peg == null)
                {
                    peg = FindClosestPeg(livePegs, entry.PosX, entry.PosY, matched);
                    if (peg != null)
                        posMatched++;
                }

                if (peg == null) continue;
                matched.Add(peg);

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
            }

            // Deactivate client pegs not in host snapshot
            int extras = 0;
            foreach (var peg in livePegs)
            {
                if (peg != null && !matched.Contains(peg) && peg.gameObject.activeSelf)
                {
                    peg.gameObject.SetActive(false);
                    extras++;
                }
            }

            _log.LogInfo($"[PegboardApplier] GUIDMatched={guidMatched}, PosMatched={posMatched}, " +
                $"TypeChanged={typeChanged}, Destroyed={destroyed}, Reactivated={reactivated}, ExtrasRemoved={extras} " +
                $"(host={snapshot.TotalPegCount}, client={livePegs.Length}, " +
                $"crit={snapshot.CritPegCount}, bomb={snapshot.BombPegCount}, reset={snapshot.ResetPegCount}, " +
                $"registry={_pegId.Count})");
        }
        catch (Exception ex)
        {
            _log.LogError($"[PegboardApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static Peg FindClosestPeg(Peg[] livePegs, float posX, float posY, HashSet<Peg> alreadyMatched)
    {
        Peg closest = null;
        float closestDist = float.MaxValue;

        foreach (var peg in livePegs)
        {
            if (peg == null || alreadyMatched.Contains(peg)) continue;
            var pos = peg.transform.position;
            float dx = pos.x - posX;
            float dy = pos.y - posY;
            float dist = dx * dx + dy * dy;
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = peg;
            }
        }

        return closestDist <= 1f ? closest : null;
    }
}
