using System.Collections.Generic;
using Battle;
using HarmonyLib;
using Multipeglin.Events.Handlers.Coop;
using UnityEngine;

namespace Multipeglin.UI;

/// <summary>
/// Repaints the post-battle / nav-only slot managers each frame to reflect
/// the live navigate-phase vote tally. Color rules:
///   green  = winning slot(s) (max votes; ties => all max-vote slots)
///   yellow = slot with zero votes
///   red    = losing slot (any non-winner with non-zero votes)
/// Dud / icon-only slots are left untouched.
/// </summary>
public static class CoopNavigateSlotPainter
{
    private static readonly Color WinnerColor = new Color(0.25f, 0.85f, 0.25f, 1f);
    private static readonly Color LoserColor = new Color(0.85f, 0.2f, 0.2f, 1f);
    private static readonly Color ZeroVoteColor = new Color(0.95f, 0.85f, 0.15f, 1f);

    private static List<int> _lastTally;
    private static int _lastChildCount = -1;

    public static void Tick()
    {
        if (!CoopNavigateState.PhaseActive)
        {
            _lastTally = null;
            _lastChildCount = -1;
            return;
        }

        var votes = CoopNavigateState.VoteCounts;
        if (votes == null || votes.Count == 0)
        {
            return;
        }

        if (!TallyChanged(votes, CoopNavigateState.ChildNodeCount))
        {
            return;
        }

        _lastTally = new List<int>(votes);
        _lastChildCount = CoopNavigateState.ChildNodeCount;

        try
        {
            if (CoopNavigateState.Source == "nav_only")
            {
                PaintNavOnly(votes, CoopNavigateState.ChildNodeCount);
            }
            else
            {
                PaintPostBattle(votes, CoopNavigateState.ChildNodeCount);
            }
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopNavigate] Slot paint failed: {ex.Message}");
        }
    }

    private static bool TallyChanged(List<int> votes, int childCount)
    {
        if (_lastTally == null || _lastChildCount != childCount || _lastTally.Count != votes.Count)
        {
            return true;
        }

        for (var i = 0; i < votes.Count; i++)
        {
            if (_lastTally[i] != votes[i])
            {
                return true;
            }
        }

        return false;
    }

    private static void PaintPostBattle(List<int> votes, int childCount)
    {
        var pbcs = Resources.FindObjectsOfTypeAll<global::Battle.PostBattleController>();
        global::Battle.PostBattleController target = null;
        foreach (var p in pbcs)
        {
            if (p != null && p.gameObject != null && p.gameObject.activeInHierarchy)
            {
                target = p;
                break;
            }
        }

        if (target == null)
        {
            return;
        }

        var left = AccessTools.Field(typeof(global::Battle.PostBattleController), "_leftSlotManager")?.GetValue(target) as SlotManager;
        var center = AccessTools.Field(typeof(global::Battle.PostBattleController), "_centerSlotManager")?.GetValue(target) as SlotManager;
        var right = AccessTools.Field(typeof(global::Battle.PostBattleController), "_rightSlotManager")?.GetValue(target) as SlotManager;

        if (childCount == 1)
        {
            // Single-child: all three slots represent child 0.
            var c = ColorFor(votes, 0);
            ApplyColor(left, c);
            ApplyColor(center, c);
            ApplyColor(right, c);
        }
        else if (childCount == 2)
        {
            ApplyColor(left, ColorFor(votes, 0));
            ApplyColor(right, ColorFor(votes, 1));
            // center is a dud — leave untouched.
        }
        else
        {
            ApplyColor(left, ColorFor(votes, 0));
            ApplyColor(center, ColorFor(votes, 1));
            ApplyColor(right, ColorFor(votes, childCount - 1));
        }
    }

    private static void PaintNavOnly(List<int> votes, int childCount)
    {
        var nocs = Resources.FindObjectsOfTypeAll<global::NavOnlyController>();
        global::NavOnlyController target = null;
        foreach (var n in nocs)
        {
            if (n != null && n.gameObject != null && n.gameObject.activeInHierarchy)
            {
                target = n;
                break;
            }
        }

        if (target == null)
        {
            return;
        }

        var left = AccessTools.Field(typeof(global::NavOnlyController), "_leftSlotManager")?.GetValue(target) as SlotManager;
        var center = AccessTools.Field(typeof(global::NavOnlyController), "_centreSlotManager")?.GetValue(target) as SlotManager;
        var right = AccessTools.Field(typeof(global::NavOnlyController), "_rightSlotManager")?.GetValue(target) as SlotManager;

        if (childCount == 1)
        {
            var c = ColorFor(votes, 0);
            ApplyColor(left, c);
            ApplyColor(center, c);
            ApplyColor(right, c);
        }
        else
        {
            ApplyColor(left, ColorFor(votes, 0));
            ApplyColor(right, ColorFor(votes, childCount - 1));
            // center has no highlight in 2-child nav-only — leave alone.
        }
    }

    private static Color ColorFor(List<int> votes, int childIdx)
    {
        if (childIdx < 0 || childIdx >= votes.Count)
        {
            return ZeroVoteColor;
        }

        if (votes[childIdx] == 0)
        {
            return ZeroVoteColor;
        }

        var max = 0;
        for (var i = 0; i < votes.Count; i++)
        {
            if (votes[i] > max)
            {
                max = votes[i];
            }
        }

        return votes[childIdx] == max ? WinnerColor : LoserColor;
    }

    private static void ApplyColor(SlotManager slot, Color color)
    {
        if (slot == null || !slot.gameObject.activeInHierarchy)
        {
            return;
        }

        if (slot.highlightHalf != null && slot.highlightHalf.gameObject.activeInHierarchy)
        {
            var prev = slot.highlightHalf.color;
            slot.highlightHalf.color = new Color(color.r, color.g, color.b, prev.a > 0f ? prev.a : 1f);
        }

        if (slot.highlightFull != null && slot.highlightFull.gameObject.activeInHierarchy)
        {
            var prev = slot.highlightFull.color;
            slot.highlightFull.color = new Color(color.r, color.g, color.b, prev.a > 0f ? prev.a : 1f);
        }
    }
}
