using System.Collections.Generic;

namespace Multipeglin.GameState.Snapshots;

public class PegboardStateSnapshot
{
    public List<PegEntry> Pegs { get; set; } = new List<PegEntry>();

    public List<VineEntry> Vines { get; set; } = new List<VineEntry>();

    public List<BlackHoleEntry> BlackHoles { get; set; } = new List<BlackHoleEntry>();

    public List<SplineGeneratorEntry> SplineGenerators { get; set; } = new List<SplineGeneratorEntry>();

    /// <summary>
    /// SuperSapper boss minesweeper-style obscurer grid (RandomObscuredPegGrid).
    /// Null when no such grid exists in the current battle. Sparse — only
    /// revealed cells are listed; all other cells are assumed unrevealed.
    /// </summary>
    public ObscurerGridSnapshot ObscurerGrid { get; set; }

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

    /// <summary>
    /// True when the peg's own activeSelf is true but a parent up the hierarchy
    /// is inactive — i.e. the peg is hidden because the entire group is toggled
    /// off (e.g. Spirit of Radia's pegboardA/B alternation). The client should
    /// not destroy the peg AND must not force its parent chain active; both host
    /// and client run PegLayoutAlternator independently, so the peg will become
    /// visible naturally when the host activates the parent group and the next
    /// snapshot reports activeInHierarchy=true.
    /// </summary>
    public bool IsParentHidden { get; set; }

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

    /// <summary>
    /// Local Z rotation in degrees. Captured per peg so the applier can align
    /// rotation alongside position. Without this, when a peg is taken from the
    /// available pool and snapped to a host slot intended for an angled peg
    /// (e.g. a rotated LongPeg prefab variant), the snapped peg keeps its
    /// source rotation and visually appears at the right (x,y) but at the
    /// wrong angle. Local (not world) so per-frame parent rotators
    /// (RotatingPegCircle) don't leak into the value.
    /// </summary>
    public float RotZ { get; set; }
}

public class VineEntry
{
    public string Peg1Guid { get; set; }

    public string Peg2Guid { get; set; }
}

/// <summary>
/// One <see cref="Battle.PegBehaviour.PegSplineFollow"/> generator's current spline phase
/// (the distance offset of its sibling#0 peg along the spline). Both host and client run
/// PegSplineFollow.FixedUpdate independently, so without a periodic phase reset the client's
/// pegs slowly drift around the loop and end up far from where the host says they are —
/// visible as some pegs appearing in totally wrong areas of the figure-8 / spline path.
/// </summary>
public class SplineGeneratorEntry
{
    /// <summary>Slash-joined ancestry chain of the generator GameObject; stable across host/client because it comes from the prefab hierarchy.</summary>
    public string HierarchyPath { get; set; }

    /// <summary>Distance along the spline of sibling#0 (kept for back-compat / log readability).</summary>
    public float Phase { get; set; }

    /// <summary>Number of pegs the generator instantiated — sanity check the match.</summary>
    public int NumPegs { get; set; }

    /// <summary>
    /// Full per-peg spline-distance list. The applier writes these directly into
    /// <c>PegSplineFollow._pegSplineDistances</c> so the client's per-peg distance
    /// EXACTLY matches the host's, eliminating drift / spacing-mismatch edge cases
    /// that "shift by Phase" couldn't fix (e.g. when a peg's distance got out of
    /// step with its siblings or two generators briefly overlapped). Sized NumPegs.
    /// </summary>
    public List<float> Distances { get; set; } = new List<float>();

    /// <summary>World position of the generator GameObject's parent. PegSplineFollow positions
    /// each peg at <c>splinePoint + transform.parent.position</c>, so if the parent (often the
    /// moving boss) sits at different world coords on host vs client, the entire loop is offset
    /// across the screen even after phase is synced.</summary>
    public float ParentPosX { get; set; }

    public float ParentPosY { get; set; }

    /// <summary>True if the generator actually has a parent. When false, the parent-pos fields
    /// are not meaningful and the applier should skip the parent-pos correction.</summary>
    public bool HasParent { get; set; }
}

/// <summary>
/// SuperSapper-only minesweeper grid state. Each cell is either unrevealed
/// (the default — the "full block") or revealed with a specific neighbour-peg
/// colour code. Both host and client run RandomObscuredPegGrid.CreatePegs
/// deterministically (boss-RNG-seeded), so cell coordinates and assignedPeg
/// references match across machines — only the revealed/colour state needs
/// syncing.
/// </summary>
public class ObscurerGridSnapshot
{
    /// <summary>Total grid dimensions (sanity-check the local grid before applying).</summary>
    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>Sparse list of revealed cells. Cells not in this list are unrevealed.</summary>
    public List<ObscurerCellEntry> RevealedCells { get; set; } = new List<ObscurerCellEntry>();
}

public class ObscurerCellEntry
{
    public int X { get; set; }

    public int Y { get; set; }

    /// <summary>Cast of <see cref="Battle.PegBehaviour.RandomObscuredPegGrid.CellPegType"/>.</summary>
    public int RevealedPegType { get; set; }
}

/// <summary>
/// Spirit of Radia (and any other source of <see cref="Battle.Pachinko.Obstacles.PegboardBlackHole"/>)
/// spawns black-hole obstacles that are independent GameObjects, not pegs. The client blocks the
/// boss action that instantiates them, so the host must enumerate and ship their world positions
/// every heartbeat — the client mirrors them as visual-only clones (no gravity simulation needed
/// since the client never owns balls).
/// </summary>
public class BlackHoleEntry
{
    /// <summary>Stable index within the host's spawn list — used as an identity key for diffing.</summary>
    public int Index { get; set; }

    public float PosX { get; set; }

    public float PosY { get; set; }
}
