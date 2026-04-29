using System;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;
using Multipeglin.Network;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Client-side fallback that turns a left-click into a NavigateVoteEvent during
/// the parallel-shoot navigate phase. The native pipeline
/// (click -> PlayfieldMouseDetector -> PachinkoBall.Fire -> SlotTrigger ->
/// NavOnlyController.HandleSlotTriggerActivated -> our patch -> SubmitVote)
/// has been observed to silently break on the client when the scenario UI
/// tear-down doesn't perfectly match what the host's native CloseStore would
/// have done — the aimer renders, but no Fire ever runs and no vote is sent.
///
/// Rather than chase every possible UI/raycast edge case, this component
/// reads the raw mouse click and submits a vote directly from screen-space
/// position so the parallel phase always resolves. The visible nav ball is
/// destroyed too so the player gets feedback that their vote landed.
/// </summary>
public sealed class CoopNavigateClientInput : MonoBehaviour
{
    private void Update()
    {
        try
        {
            if (!CoopNavigateState.PhaseActive
                || CoopNavigateState.Resolved
                || CoopNavigateState.LocalVoteCast)
            {
                return;
            }

            var services = MultiplayerPlugin.Services;
            if (services == null
                || !services.TryResolve<IMultiplayerMode>(out var mode)
                || !mode.IsSpectating)
            {
                return; // Host uses the native slot-trigger path.
            }

            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }

            // Ignore clicks consumed by interactive UI (buttons etc).
            if (Patches.MultiplayerClientPatches.IsPointerOverInteractiveUI())
            {
                return;
            }

            var childCount = Math.Max(1, CoopNavigateState.ChildNodeCount);
            var childIndex = ResolveChildIndexFromMouse(childCount);
            if (childIndex < 0)
            {
                return;
            }

            if (services.TryResolve<IMessageSender>(out var sender))
            {
                CoopNavigateState.LocalVoteCast = true;
                sender.Send(new NavigateVoteEvent { ChildIndex = childIndex });
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[CoopNavigate] Client click->vote: child={childIndex} (childCount={childCount})");

                DestroyNavBall();
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopNavigate] Client click->vote failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Map the mouse's horizontal screen position to a child index. Mirrors
    /// the slot-trigger geometry: left third -> child 0, right third -> last,
    /// middle third -> dud for childCount==2 / center for childCount==3 /
    /// child 0 for childCount==1.
    /// </summary>
    private static int ResolveChildIndexFromMouse(int childCount)
    {
        var w = Screen.width <= 0 ? 1 : Screen.width;
        var t = Mathf.Clamp01(Input.mousePosition.x / w);

        if (childCount == 1)
        {
            return 0;
        }

        if (childCount == 2)
        {
            // Anywhere left of center -> 0, right of center -> 1. No dud zone:
            // the player intended to vote, snap to whichever side the click
            // was closer to.
            return t < 0.5f ? 0 : 1;
        }

        // 3+ children
        if (t < 0.34f)
        {
            return 0;
        }

        if (t > 0.66f)
        {
            return childCount - 1;
        }

        return 1; // center
    }

    private static void DestroyNavBall()
    {
        try
        {
            var nocs = Resources.FindObjectsOfTypeAll<global::NavOnlyController>();
            if (nocs == null)
            {
                return;
            }

            foreach (var noc in nocs)
            {
                if (noc == null || noc.gameObject == null || !noc.gameObject.scene.IsValid())
                {
                    continue;
                }

                var ballField = HarmonyLib.AccessTools.Field(typeof(global::NavOnlyController), "_ball");
                var ballGO = ballField?.GetValue(noc) as GameObject;
                if (ballGO == null)
                {
                    continue;
                }

                var pb = ballGO.GetComponent<PachinkoBall>();
                if (pb != null)
                {
                    try
                    {
                        pb.StartDestroy();
                    }
                    catch
                    {
                        UnityEngine.Object.Destroy(ballGO);
                    }
                }
                else
                {
                    UnityEngine.Object.Destroy(ballGO);
                }
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopNavigate] DestroyNavBall failed: {ex.Message}");
        }
    }
}
