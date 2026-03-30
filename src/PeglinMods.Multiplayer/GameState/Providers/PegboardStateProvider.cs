using System;
using Battle;
using BepInEx.Logging;
using HarmonyLib;
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

            // Get pegs from PegManager._allPegs — the authoritative list.
            // FindObjectsOfType<Peg> finds orphaned pre-instanced duplicates.
            // PegManager is a plain C# class (not MonoBehaviour), accessed via BattleController.
            var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
            var pm = bc?.pegManager;
            if (pm == null || pm.allPegs == null)
            {
                _log.LogInfo("[PegProvider] No PegManager or allPegs list found");
                return snapshot;
            }

            var pegs = pm.allPegs;
            for (int i = 0; i < pegs.Count; i++)
            {
                var peg = pegs[i];
                if (peg == null) continue;

                var guid = _pegId.GetOrAssignGuid(peg);
                var pt = (int)peg.pegType;
                // A peg is "destroyed" if: inactive, has DESTROYED type flag, OR was cleared (hit during ball physics)
                bool destroyed = !peg.gameObject.activeSelf || (pt & 0x20) != 0 || peg.Cleared;

                snapshot.Pegs.Add(new PegEntry
                {
                    Guid = guid,
                    Index = i,
                    PegType = pt,
                    PegTypeName = peg.pegType.ToString(),
                    PosX = peg.transform.position.x,
                    PosY = peg.transform.position.y,
                    SlimeType = (int)peg.slimeType,
                    IsDestroyed = destroyed,
                    CoinCount = peg.NumCoins(),
                });

                if (peg.gameObject.activeSelf)
                {
                    if ((pt & 0x2) != 0) snapshot.CritPegCount++;
                    if ((pt & 0x4) != 0) snapshot.BombPegCount++;
                    if ((pt & 0x8) != 0) snapshot.ResetPegCount++;
                }
            }
            snapshot.TotalPegCount = pegs.Count;

            _log.LogInfo($"[PegProvider] Captured {snapshot.TotalPegCount} pegs from PegManager.allPegs " +
                $"(crit={snapshot.CritPegCount}, bomb={snapshot.BombPegCount}, reset={snapshot.ResetPegCount}, registry={_pegId.Count})");

            return snapshot;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"PegboardStateProvider.Capture failed: {ex.Message}");
            return null;
        }
    }
}
