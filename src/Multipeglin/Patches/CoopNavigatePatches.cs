using Battle;
using HarmonyLib;
using Multipeglin.Events.Handlers.Coop;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Network;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

/// <summary>
/// Patches that enable the parallel-shoot end-of-stage navigate phase.
/// Both PostBattleController.HandleSlotTriggerActivated and NavOnlyController.HandleSlotTriggerActivated
/// are intercepted: a slot hit becomes a vote (host-local or NavigateVoteEvent client→host),
/// and the original TriggerVictory / FadeAndLoad is skipped on every player.
/// The host's CoopNavigateResolver eventually picks the winner and runs the native
/// scene transition once.
/// </summary>
[HarmonyPatch]
internal static class CoopNavigatePatches
{
    // -------------------------------------------------------------------------
    // PostBattleController — end-of-battle navigate
    // -------------------------------------------------------------------------

    [HarmonyPatch(typeof(global::Battle.PostBattleController), "HandleSlotTriggerActivated")]
    [HarmonyPrefix]
    public static bool PostBattle_HandleSlot_Prefix(int index, PachinkoBall pBall)
    {
        if (!UI.LobbyUI.GameStartReceived)
        {
            return true;
        }

        if (!CoopNavigateState.PhaseActive || CoopNavigateState.Resolved)
        {
            return true;
        }

        // Ghost balls are remote players' shots rendered locally — they must
        // not register as our vote. Swallow the trigger silently.
        if (pBall != null && pBall.IsDummy)
        {
            return false;
        }

        if (CoopNavigateState.LocalVoteCast)
        {
            return false;
        }

        // StaticGameData.currentNode is null on clients (the live MapNode tree
        // is host-only). Trust CoopNavigateState.ChildNodeCount, which the host
        // sent in NavigatePhaseStartEvent. Falling back to currentNode here
        // returned 0 → every shot resolved as a miss → no vote was ever sent.
        var childCount = ShouldSuppressClientLogic
            ? CoopNavigateState.ChildNodeCount
            : (StaticGameData.currentNode?.ChildNodes?.Length ?? CoopNavigateState.ChildNodeCount);
        var childIndex = ResolvePostBattleChildIndex(index, childCount);

        if (childIndex < 0)
        {
            // Miss. On client, suppress damage and just destroy the ball so the
            // respawn cycle gives them another shot. On host, let the original
            // misnav run (host has authoritative health).
            if (ShouldSuppressClientLogic)
            {
                MultiplayerPlugin.Logger?.LogInfo($"[CoopNavigate] Client miss: triggerIndex={index}, childCount={childCount}");
                SafeDestroyBall(pBall);
                return false;
            }

            return true;
        }

        SubmitVote(childIndex);
        LockBattleNavigationComplete();
        return false;
    }

    private static int ResolvePostBattleChildIndex(int triggerIndex, int childCount)
    {
        if (childCount == 1)
        {
            return (triggerIndex > 0 && triggerIndex < 4) ? 0 : -1;
        }

        if (childCount > 1)
        {
            if (triggerIndex < 2)
            {
                return 0;
            }

            if (triggerIndex > 2)
            {
                return childCount - 1;
            }

            if (childCount > 2)
            {
                return 1;
            }

            return -1; // childCount == 2 and triggerIndex == 2 => miss (centre = dud)
        }

        return -1;
    }

    // -------------------------------------------------------------------------
    // NavOnlyController — end-of-event navigate (treasure / shop / text / peg)
    // -------------------------------------------------------------------------

    [HarmonyPatch(typeof(global::NavOnlyController), "HandleSlotTriggerActivated")]
    [HarmonyPrefix]
    public static bool NavOnly_HandleSlot_Prefix(NavOnlyController __instance, int index, PachinkoBall pBall)
    {
        if (!UI.LobbyUI.GameStartReceived)
        {
            return true;
        }

        if (!CoopNavigateState.PhaseActive || CoopNavigateState.Resolved)
        {
            return true;
        }

        // Ghost balls are remote players' shots rendered locally — they must
        // not register as our vote. Swallow the trigger silently.
        if (pBall != null && pBall.IsDummy)
        {
            return false;
        }

        if (CoopNavigateState.LocalVoteCast)
        {
            return false;
        }

        // See note in PostBattle_HandleSlot_Prefix — currentNode is null on clients.
        var childCount = ShouldSuppressClientLogic
            ? CoopNavigateState.ChildNodeCount
            : (StaticGameData.currentNode?.ChildNodes?.Length ?? CoopNavigateState.ChildNodeCount);
        var childIndex = ResolveNavOnlyChildIndex(index, childCount);

        if (childIndex < 0)
        {
            if (ShouldSuppressClientLogic)
            {
                SafeDestroyBall(pBall);
                return false;
            }

            return true;
        }

        SubmitVote(childIndex);
        LockNavOnlyStarted(__instance);
        return false;
    }

    private static int ResolveNavOnlyChildIndex(int triggerIndex, int childCount)
    {
        if (childCount <= 0)
        {
            return -1;
        }

        if (triggerIndex < 2)
        {
            return 0;
        }

        if (triggerIndex > 2)
        {
            return childCount - 1;
        }

        // triggerIndex == 2 (centre slot in 1- or 2-child case)
        if (childCount == 1)
        {
            // 1-child uses centre as the icon-only slot — count it as a hit on child 0
            return 0;
        }

        return -1; // 2-child: centre is dud => miss
    }

    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    private static void SubmitVote(int childIndex)
    {
        CoopNavigateState.LocalVoteCast = true;

        if (IsHosting)
        {
            CoopNavigateResolver.RecordHostVote(childIndex);
            return;
        }

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<IMessageSender>(out var sender) == true)
        {
            sender.Send(new NavigateVoteEvent { ChildIndex = childIndex });
            MultiplayerPlugin.Logger?.LogInfo($"[CoopNavigate] Client sent vote: child={childIndex}");
        }
    }

    private static void SafeDestroyBall(PachinkoBall pBall)
    {
        try
        {
            pBall?.StartDestroy();
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopNavigate] StartDestroy failed: {ex.Message}");
        }
    }

    /// <summary>
    /// After a successful vote, transition local BattleController state to
    /// NAVIGATION_COMPLETE so HandlePachinkoBallDestroyed does not auto-respawn
    /// the nav ball. The native TriggerVictory would have done this; we did
    /// not call it, so we set the state directly.
    /// </summary>
    private static void LockBattleNavigationComplete()
    {
        try
        {
            BattleController.CurrentBattleState = BattleController.BattleState.NAVIGATION_COMPLETE;
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopNavigate] LockBattleNavigationComplete failed: {ex.Message}");
        }
    }

    /// <summary>
    /// After a successful vote in NavOnly, set the private _navStarted flag so
    /// PachinkoBallDestroyed -> WaitAndSpawn does not auto-respawn the nav ball.
    /// </summary>
    private static void LockNavOnlyStarted(NavOnlyController noc)
    {
        try
        {
            var field = AccessTools.Field(typeof(NavOnlyController), "_navStarted");
            field?.SetValue(noc, true);
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopNavigate] LockNavOnlyStarted failed: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // NavOnlyController.PrepareForNavigation — host enters navigate phase
    // -------------------------------------------------------------------------

    /// <summary>
    /// Host-side: after the host's native PrepareForNavigation arms its own
    /// nav ball, kick off the parallel-shoot resolver and broadcast a
    /// NavigatePhaseStartEvent so every client arms their own ball too.
    /// HandleSlotTriggerActivated on each side then funnels slot hits through
    /// CoopNavigateResolver as votes; majority wins.
    /// </summary>
    [HarmonyPatch(typeof(global::NavOnlyController), "PrepareForNavigation")]
    [HarmonyPostfix]
    public static void NavOnlyController_PrepareForNavigation_Postfix(global::NavOnlyController __instance)
    {
        if (!UI.LobbyUI.GameStartReceived || !IsHosting)
        {
            return;
        }

        if (CoopNavigateState.PhaseActive)
        {
            return;
        }

        var childCount = StaticGameData.currentNode?.ChildNodes?.Length ?? 1;
        if (childCount < 1)
        {
            childCount = 1;
        }

        CoopNavigateResolver.StartPhase("nav_only", childCount, CoopNavigateResolver.CaptureChildRoomTypes());
    }

    // -------------------------------------------------------------------------
    // Block PachinkoBall.Fire after the local player has voted, so a stuck
    // aimer can't fire a second time before the host resolves.
    // -------------------------------------------------------------------------

    [HarmonyPatch(typeof(PachinkoBall), "Fire")]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.HigherThanNormal)]
    public static bool PachinkoBall_Fire_NavigateGuard(PachinkoBall __instance)
    {
        if (!UI.LobbyUI.GameStartReceived)
        {
            return true;
        }

        if (!CoopNavigateState.PhaseActive)
        {
            return true;
        }

        if (CoopNavigateState.LocalVoteCast || CoopNavigateState.Resolved)
        {
            return false;
        }

        return true;
    }
}
