using Battle;
using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class PegManagerPatches
{
    /// <summary>
    /// Block special peg type shuffling on client. The pegboard layout loads
    /// with all pegs as REGULAR. The host sends the correct peg types and
    /// the applier sets them.
    ///
    /// On the host, log the caller so we can diagnose excessive shuffle
    /// frequency (see bug report: "shuffle fucks up everything").
    /// </summary>
    [HarmonyPatch(typeof(PegManager), "ShuffleSpecialPegs")]
    [HarmonyPrefix]
    public static bool PegManager_ShuffleSpecialPegs_Prefix(bool forceRefresh)
    {
        if (ShouldSuppressClientLogic)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked ShuffleSpecialPegs — host will send peg types");
            return false;
        }

        if (IsHosting)
        {
            MultiplayerPlugin.Logger?.LogInfo(
                $"[PegShuffleHost] ShuffleSpecialPegs(forceRefresh={forceRefresh}) caller={DescribeShuffleCaller()}");
        }

        return true;
    }

    /// <summary>
    /// Block individual special peg creation on client.
    /// Covers ShuffleCritPegs, CreateRefreshPegs, and direct CreateSpecialPegs calls.
    /// </summary>
    [HarmonyPatch(typeof(PegManager), "CreateSpecialPegs")]
    [HarmonyPrefix]
    public static bool PegManager_CreateSpecialPegs_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Block crit peg shuffling on client; on host, log the caller.
    /// </summary>
    [HarmonyPatch(typeof(PegManager), "ShuffleCritPegs")]
    [HarmonyPrefix]
    public static bool PegManager_ShuffleCritPegs_Prefix()
    {
        if (ShouldSuppressClientLogic)
        {
            return false;
        }

        if (IsHosting)
        {
            MultiplayerPlugin.Logger?.LogInfo(
                $"[PegShuffleHost] ShuffleCritPegs caller={DescribeShuffleCaller()}");
        }

        return true;
    }

    /// <summary>Block refresh peg creation on client.</summary>
    [HarmonyPatch(typeof(PegManager), "CreateRefreshPegs")]
    [HarmonyPrefix]
    public static bool PegManager_CreateRefreshPegs_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        return false;
    }

    // RegularPeg_PopPeg_Postfix (removed): previously called RemoveIfCleared() on host
    // pegs so they'd fade to invisible, but the DOFade tween it starts has an onComplete
    // Disable() callback that Reset() does NOT kill. When a refresh peg fires during the
    // same shot, pegs popped within the last second get deactivated 1s later despite
    // Reset()'s SetActive(true) call — breaking the refresh. The client keeps popped
    // pegs at scale 0.3 (no fade), so the host doing the same is fine and consistent.

    /// <summary>Block failsafe refresh peg creation on client.</summary>
    [HarmonyPatch(typeof(PegManager), "FailSafeCreateRefreshPegs")]
    [HarmonyPrefix]
    public static bool PegManager_FailSafeCreateRefreshPegs_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        return false;
    }

    /// <summary>Block peg reset on client — host sync handles peg state.</summary>
    [HarmonyPatch(typeof(PegManager), "ResetPegs")]
    [HarmonyPrefix]
    public static bool PegManager_ResetPegs_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        return false;
    }

    /// <summary>Block shield peg creation on client — host sync handles shield state.</summary>
    [HarmonyPatch(typeof(PegManager), "ApplyShieldToRegularPegs")]
    [HarmonyPrefix]
    public static bool PegManager_ApplyShieldToRegularPegs_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Block client-side RNG bomb placement. The host is authoritative for which
    /// pegs become bombs. If the client runs its own ConvertPegsToBombs (via
    /// BattleController.CheckRelicsForStartingBombCount or PlayerStatusEffectController),
    /// it uses seeded RNG to pick DIFFERENT pegs than the host, producing stale
    /// bombs that never match the host's and leak into the _bombs list forever.
    /// </summary>
    [HarmonyPatch(typeof(PegManager), "ConvertPegsToBombs")]
    [HarmonyPrefix]
    public static bool PegManager_ConvertPegsToBombs_Prefix()
    {
        if (ShouldSuppressClientLogic)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked PegManager.ConvertPegsToBombs — host drives bomb placement");
            return false;
        }

        return true;
    }
}
