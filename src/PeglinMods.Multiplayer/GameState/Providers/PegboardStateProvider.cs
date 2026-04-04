using System;
using Battle;
using Battle.PegBehaviour;
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

            // Capture regular pegs from allPegs
            // NOTE: Bomb instances can end up in allPegs (pre-placed in layout, or added
            // via HandlePegAdded for non-BOMB types). Detect them by checking `peg is Bomb`.
            var pegs = pm.allPegs;
            int allPegsBombCount = 0;
            for (int i = 0; i < pegs.Count; i++)
            {
                var peg = pegs[i];
                if (peg == null) continue;

                bool isBombInstance = peg is Bomb;
                if (isBombInstance) allPegsBombCount++;

                var guid = _pegId.GetOrAssignGuid(peg);
                var pt = (int)peg.pegType;
                bool destroyed = !peg.gameObject.activeSelf || (pt & 0x20) != 0;
                // Use IsDisabled() to check if peg is actually popped (collider disabled).
                // After Reset(), _cleared stays true but colliders are re-enabled —
                // so peg.Cleared is NOT reliable for "is this peg functionally popped."
                bool cleared = false;
                bool wasPreviouslyCleared = false;
                if (!destroyed)
                {
                    try { cleared = peg.IsDisabled(); } catch { }
                    // _cleared flag tracks "was this peg ever cleared this battle" — controls
                    // the previously-cleared background visual (dot/different color after refresh).
                    try
                    {
                        var clearedField = HarmonyLib.AccessTools.Field(typeof(Peg), "_cleared");
                        wasPreviouslyCleared = (bool)(clearedField?.GetValue(peg) ?? false);
                    }
                    catch { }
                }

                var entry = new PegEntry
                {
                    Guid = guid,
                    Index = i,
                    PegType = pt,
                    PegTypeName = peg.pegType.ToString(),
                    PosX = peg.transform.position.x,
                    PosY = peg.transform.position.y,
                    SlimeType = (int)peg.slimeType,
                    IsDestroyed = destroyed,
                    IsCleared = cleared,
                    WasPreviouslyCleared = wasPreviouslyCleared,
                    CoinCount = peg.NumCoins(),
                    BuffAmount = peg.buffAmount,
                    IsBomb = isBombInstance,
                    HitCount = isBombInstance ? ((Bomb)peg).HitCount : 0,
                };
                CaptureShieldState(peg, entry);
                CaptureLpmParentPos(peg, entry);
                snapshot.Pegs.Add(entry);

                if (peg.gameObject.activeSelf && !destroyed)
                {
                    if (isBombInstance) snapshot.BombPegCount++;
                    else if ((pt & 0x2) != 0) snapshot.CritPegCount++;
                    else if ((pt & 0x8) != 0) snapshot.ResetPegCount++;
                }
            }

            // Capture bombs from _bombs list (bombs are NOT in allPegs — they're separate)
            var bombsField = HarmonyLib.AccessTools.Field(typeof(Battle.PegManager), "_bombs");
            var bombs = bombsField?.GetValue(pm) as System.Collections.Generic.List<Bomb>;
            if (bombs != null)
            {
                for (int i = 0; i < bombs.Count; i++)
                {
                    var bomb = bombs[i];
                    if (bomb == null) continue;

                    var guid = _pegId.GetOrAssignGuid(bomb);
                    var pt = (int)bomb.pegType;
                    bool destroyed = !bomb.gameObject.activeSelf || (pt & 0x20) != 0;
                    bool cleared = false;
                    bool wasPreviouslyCleared = false;
                    if (!destroyed)
                    {
                        try { cleared = bomb.IsDisabled(); } catch { }
                        try
                        {
                            var clearedField = HarmonyLib.AccessTools.Field(typeof(Peg), "_cleared");
                            wasPreviouslyCleared = (bool)(clearedField?.GetValue(bomb) ?? false);
                        }
                        catch { }
                    }

                    var bombEntry = new PegEntry
                    {
                        Guid = guid,
                        Index = pegs.Count + i,
                        PegType = pt,
                        PegTypeName = bomb.pegType.ToString(),
                        PosX = bomb.transform.position.x,
                        PosY = bomb.transform.position.y,
                        SlimeType = (int)bomb.slimeType,
                        IsDestroyed = destroyed,
                        IsCleared = cleared,
                        WasPreviouslyCleared = wasPreviouslyCleared,
                        CoinCount = bomb.NumCoins(),
                        HitCount = bomb.HitCount,
                        IsBomb = true,
                        BuffAmount = bomb.buffAmount,
                    };
                    CaptureShieldState(bomb, bombEntry);
                    CaptureLpmParentPos(bomb, bombEntry);
                    snapshot.Pegs.Add(bombEntry);

                    if (bomb.gameObject.activeSelf && !destroyed)
                        snapshot.BombPegCount++;

                    _log.LogInfo($"[PegProvider] BOMB[{i}] guid={guid} pos=({bomb.transform.position.x:F1},{bomb.transform.position.y:F1}) " +
                        $"type={bomb.pegType} active={bomb.gameObject.activeSelf} cleared={cleared} destroyed={destroyed} hits={bomb.HitCount}");
                }
            }

            // Capture bouncer pegs from _bouncerPegs (bouncers are NOT in allPegs — they're separate)
            var bouncers = pm.bouncerPegs;
            if (bouncers != null)
            {
                for (int i = 0; i < bouncers.Count; i++)
                {
                    var bouncer = bouncers[i];
                    if (bouncer == null) continue;

                    var guid = _pegId.GetOrAssignGuid(bouncer);
                    var pt = (int)bouncer.pegType;
                    bool destroyed = !bouncer.gameObject.activeSelf || (pt & 0x20) != 0;
                    bool cleared = false;
                    bool wasPreviouslyCleared = false;
                    if (!destroyed)
                    {
                        try { cleared = bouncer.IsDisabled(); } catch { }
                        try
                        {
                            var clearedField = HarmonyLib.AccessTools.Field(typeof(Peg), "_cleared");
                            wasPreviouslyCleared = (bool)(clearedField?.GetValue(bouncer) ?? false);
                        }
                        catch { }
                    }

                    var bouncerEntry = new PegEntry
                    {
                        Guid = guid,
                        Index = pegs.Count + (bombs?.Count ?? 0) + i,
                        PegType = pt,
                        PegTypeName = bouncer.pegType.ToString(),
                        PosX = bouncer.transform.position.x,
                        PosY = bouncer.transform.position.y,
                        SlimeType = (int)bouncer.slimeType,
                        IsDestroyed = destroyed,
                        IsCleared = cleared,
                        WasPreviouslyCleared = wasPreviouslyCleared,
                        CoinCount = bouncer.NumCoins(),
                        IsBouncer = true,
                        BuffAmount = bouncer.buffAmount,
                    };
                    CaptureShieldState(bouncer, bouncerEntry);
                    CaptureLpmParentPos(bouncer, bouncerEntry);
                    snapshot.Pegs.Add(bouncerEntry);

                    if (bouncer.gameObject.activeSelf && !destroyed)
                        snapshot.BouncerPegCount++;
                }
            }

            snapshot.TotalPegCount = snapshot.Pegs.Count;

            int bombsListCount = bombs?.Count ?? -1;
            _log.LogInfo($"[PegProvider] Captured {snapshot.TotalPegCount} pegs from PegManager " +
                $"(crit={snapshot.CritPegCount}, bomb={snapshot.BombPegCount}, reset={snapshot.ResetPegCount}, " +
                $"bouncer={snapshot.BouncerPegCount}, registry={_pegId.Count}, " +
                $"_bombs={bombsListCount}, allPegsBombs={allPegsBombCount})");

            return snapshot;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"PegboardStateProvider.Capture failed: {ex.Message}");
            return null;
        }
    }

    private static void CaptureShieldState(Peg peg, PegEntry entry)
    {
        try
        {
            if (!peg.shielded) return;
            entry.IsShielded = true;
            var overlayField = HarmonyLib.AccessTools.Field(typeof(Peg), "PegShieldOverlayInstance");
            var overlay = overlayField?.GetValue(peg) as Battle.PegBehaviour.PegShieldOverlay;
            if (overlay != null)
            {
                entry.ShieldHitCount = overlay.hitCount;
                entry.ShieldHitLimit = overlay.hitLimit;
            }
        }
        catch { }
    }

    /// <summary>
    /// If the peg is under a LinearPegMovement parent (self or direct parent),
    /// capture the parent's world position for direct sync on the client.
    /// </summary>
    private static void CaptureLpmParentPos(Peg peg, PegEntry entry)
    {
        try
        {
            var lpm = peg.GetComponent<LinearPegMovement>();
            if (lpm == null && peg.transform.parent != null)
                lpm = peg.transform.parent.GetComponent<LinearPegMovement>();
            // Also check grandparent — bombs are children of the original peg,
            // which is a child of the LPM row.
            if (lpm == null && peg.transform.parent?.parent != null)
                lpm = peg.transform.parent.parent.GetComponent<LinearPegMovement>();

            if (lpm != null)
            {
                entry.LpmParentPosX = lpm.transform.position.x;
                entry.LpmParentPosY = lpm.transform.position.y;
            }
        }
        catch { }
    }
}
