using System.Collections.Generic;

namespace Multipeglin.GameState.Snapshots;

public class PegboardStateSnapshot
{
    public List<PegEntry> Pegs { get; set; } = new List<PegEntry>();
    public List<VineEntry> Vines { get; set; } = new List<VineEntry>();
    public int TotalPegCount { get; set; }
    public int CritPegCount { get; set; }
    public int BombPegCount { get; set; }
    public int ResetPegCount { get; set; }
    public int BouncerPegCount { get; set; }
}

public class PegEntry
{
    public string Guid { get; set; }

    /// <summary>Index in PegManager.allPegs — used for GUID assignment on first sync.</summary>
    public int Index { get; set; }

    public int PegType { get; set; }
    public string PegTypeName { get; set; }
    public float PosX { get; set; }
    public float PosY { get; set; }
    public int SlimeType { get; set; }
    public bool IsDestroyed { get; set; }

    /// <summary>Number of gold coins on this peg (0 = no gold).</summary>
    public int CoinCount { get; set; }

    /// <summary>True if the peg is currently popped (collider disabled).</summary>
    public bool IsCleared { get; set; }

    /// <summary>
    /// LongPeg-specific: host has called PegActivated and set _hit=true, but the
    /// peg is still alive (collider enabled, gray "half-hit" visual). The client
    /// should mirror this visual state without popping. When IsLongPegHit is true,
    /// IsCleared will be false. When IsCleared becomes true, the host has actually
    /// disabled the collider (via SetActiveStatus(false)) and the client should
    /// fade the peg out.
    /// </summary>
    public bool IsLongPegHit { get; set; }

    /// <summary>True if the peg was previously cleared (shows different background after refresh).</summary>
    public bool WasPreviouslyCleared { get; set; }

    /// <summary>Hit count for bombs (0=untouched, 1=fuse lit, 2+=detonated).</summary>
    public int HitCount { get; set; }

    /// <summary>True if this peg is a bomb (from _bombs list, not _allPegs).</summary>
    public bool IsBomb { get; set; }

    /// <summary>True if this peg is a bouncer (from _bouncerPegs list, not _allPegs).</summary>
    public bool IsBouncer { get; set; }

    /// <summary>Damage buff/debuff value displayed on the peg (-999 to 999).</summary>
    public int BuffAmount { get; set; }

    /// <summary>True if this peg has an active shield overlay.</summary>
    public bool IsShielded { get; set; }

    /// <summary>Shield hit count (0=untouched, hitLimit=broken).</summary>
    public int ShieldHitCount { get; set; }

    /// <summary>Shield hit limit (default 4). Shield breaks when hitCount >= hitLimit.</summary>
    public int ShieldHitLimit { get; set; }

    /// <summary>World X of the LinearPegMovement parent (null if peg is not under LPM).</summary>
    public float? LpmParentPosX { get; set; }
    /// <summary>World Y of the LinearPegMovement parent (null if peg is not under LPM).</summary>
    public float? LpmParentPosY { get; set; }

    /// <summary>Name of the transform.parent (stable across host/client because it comes from the prefab hierarchy).</summary>
    public string ParentName { get; set; }

    /// <summary>Local position relative to transform.parent (stable across host/client — baked into prefab).</summary>
    public float LocalPosX { get; set; }
    public float LocalPosY { get; set; }

    /// <summary>
    /// Sibling index under transform.parent. Paired with ParentName it forms a
    /// second structural key that stays stable even when the peg itself carries
    /// a LinearPegMovement component — LPM drives the peg's transform, so
    /// localPosition drifts each frame and (ParentName, LocalPos) matching
    /// fails. Sibling order is baked into the prefab and preserved across
    /// instantiation, so (ParentName, SiblingIndex) uniquely identifies the
    /// peg regardless of how far physics has carried it.
    /// </summary>
    public int SiblingIndex { get; set; }

    /// <summary>True if this peg (or a parent in its chain) has a LinearPegMovement
    /// component. Signals the applier to prefer (ParentName, SiblingIndex) over
    /// (ParentName, LocalPos) when resolving the structural match.</summary>
    public bool HasLpm { get; set; }
}

public class VineEntry
{
    public string Peg1Guid { get; set; }
    public string Peg2Guid { get; set; }
}
