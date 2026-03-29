using System;
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

            foreach (var peg in allPegs)
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
            snapshot.TotalPegCount = allPegs.Length;

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
