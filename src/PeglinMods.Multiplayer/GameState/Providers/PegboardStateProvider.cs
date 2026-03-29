using System;
using System.Collections.Generic;
using BepInEx.Logging;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Utility;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState.Providers;

public class PegboardStateProvider : IGameStateProvider<PegboardStateSnapshot>
{
    private readonly ManualLogSource _log;
    private readonly PegIdentifier _pegId;

    public PegboardStateProvider(ManualLogSource log, PegIdentifier pegId)
    {
        _log = log;
        _pegId = pegId;
    }

    public PegboardStateSnapshot Capture()
    {
        try
        {
            var snapshot = new PegboardStateSnapshot();

            var allPegs = UnityEngine.Object.FindObjectsOfType<Peg>(true);

            // De-duplicate pegs at the same position (host can have pre-instanced + real pegboard)
            var seenPositions = new HashSet<string>();
            var uniquePegs = new System.Collections.Generic.List<Peg>();
            foreach (var peg in allPegs)
            {
                if (peg == null) continue;
                var posKey = $"{peg.transform.position.x:F3},{peg.transform.position.y:F3}";
                if (seenPositions.Add(posKey))
                    uniquePegs.Add(peg);
            }

            if (uniquePegs.Count != allPegs.Length)
                _log.LogInfo($"[PegProvider] De-duped {allPegs.Length} → {uniquePegs.Count} pegs (removed {allPegs.Length - uniquePegs.Count} duplicates)");

            foreach (var peg in uniquePegs)
            {
                if (peg == null) continue;

                var guid = _pegId.GetOrAssignGuid(peg);

                snapshot.Pegs.Add(new PegEntry
                {
                    Guid = guid,
                    PegType = (int)peg.pegType,
                    PegTypeName = peg.pegType.ToString(),
                    PosX = peg.transform.position.x,
                    PosY = peg.transform.position.y,
                    SlimeType = (int)peg.slimeType,
                    IsDestroyed = !peg.gameObject.activeSelf || ((int)peg.pegType & 0x20) != 0,
                });

                var pt = (int)peg.pegType;
                if (peg.gameObject.activeSelf)
                {
                    if ((pt & 0x2) != 0) snapshot.CritPegCount++;
                    if ((pt & 0x4) != 0) snapshot.BombPegCount++;
                    if ((pt & 0x8) != 0) snapshot.ResetPegCount++;
                }
            }
            snapshot.TotalPegCount = uniquePegs.Count;

            _log.LogInfo($"[PegProvider] Captured {snapshot.TotalPegCount} pegs ({_pegId.Count} in registry)");

            return snapshot;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"PegboardStateProvider.Capture failed: {ex.Message}");
            return null;
        }
    }
}
