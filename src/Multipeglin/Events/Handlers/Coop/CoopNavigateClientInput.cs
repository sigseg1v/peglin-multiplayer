using System;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;
using Multipeglin.Network;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Client-side click handler for the parallel-shoot navigate phase. Reads raw
/// mouse input each frame and fires the local nav ball; the vote itself is
/// submitted only when the ball physically lands in a slot trigger
/// (CoopNavigatePatches.NavOnly_HandleSlot_Prefix /
/// PostBattle_HandleSlot_Prefix), so the slot-tally colors don't change until
/// the ball actually arrives — matching the behavior on the host. If the ball
/// fails to land within FALLBACK_SECONDS, we send a screen-position vote so
/// the phase can still resolve.
/// </summary>
public sealed class CoopNavigateClientInput : MonoBehaviour
{
    private const float FallbackSeconds = 8f;

    private bool _phaseEntryLogged;
    private float _diagTimer;
    private bool _shotFired;
    private float _shotFiredAt = -1f;
    private int _pendingFallbackChildIndex = -1;
    private float _lastBallWorldX = float.NaN;
    private float _lastBallWorldY = float.NaN;

    private void Update()
    {
        try
        {
            // Reset entry-log + shot latch when a phase ends so each new phase gets one log/shot.
            if (!CoopNavigateState.PhaseActive)
            {
                _phaseEntryLogged = false;
                _shotFired = false;
                _shotFiredAt = -1f;
                _pendingFallbackChildIndex = -1;
                _lastBallWorldX = float.NaN;
                _lastBallWorldY = float.NaN;
                return;
            }

            if (CoopNavigateState.Resolved)
            {
                return;
            }

            var services = MultiplayerPlugin.Services;
            if (services == null)
            {
                return;
            }

            if (!services.TryResolve<IMultiplayerMode>(out var mode) || mode.IsHosting)
            {
                return; // Host uses native slot-trigger path.
            }

            // One-shot diagnostic per phase so we know this component is live.
            if (!_phaseEntryLogged)
            {
                _phaseEntryLogged = true;
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[CoopNavigate/ClientInput] Active: phase={CoopNavigateState.Source}, " +
                    $"children={CoopNavigateState.ChildNodeCount}, " +
                    $"isSpectating={mode.IsSpectating}, voteCast={CoopNavigateState.LocalVoteCast}");
            }

            // Periodic state diagnostic — once every 2s while phase active and
            // local vote not yet cast — so we can see if the click handler is
            // even running.
            _diagTimer += Time.unscaledDeltaTime;
            if (_diagTimer >= 2f)
            {
                _diagTimer = 0f;
                if (!CoopNavigateState.LocalVoteCast)
                {
                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[CoopNavigate/ClientInput] Waiting for click: " +
                        $"phaseActive={CoopNavigateState.PhaseActive} resolved={CoopNavigateState.Resolved} " +
                        $"voteCast={CoopNavigateState.LocalVoteCast}");
                }
            }

            if (CoopNavigateState.LocalVoteCast)
            {
                return;
            }

            // After firing, watch for the slot trigger to submit the real vote.
            // If it doesn't fire within FallbackSeconds (ball got stuck/lost), send
            // a fallback vote based on the ball's last known world X compared to
            // slot-manager world X positions — NOT the original click position.
            if (_shotFired)
            {
                TrackActiveBallPosition();

                if (_shotFiredAt > 0f
                    && Time.unscaledTime - _shotFiredAt > FallbackSeconds
                    && services.TryResolve<IMessageSender>(out var fallbackSender))
                {
                    var fallbackChild = ResolveFallbackChildFromBallPosition();
                    if (fallbackChild < 0)
                    {
                        // Ball never tracked + no slot manager geometry available — use the
                        // pre-shot click child as last resort so the phase doesn't deadlock.
                        fallbackChild = _pendingFallbackChildIndex;
                    }

                    if (fallbackChild >= 0)
                    {
                        MultiplayerPlugin.Logger?.LogWarning(
                            $"[CoopNavigate/ClientInput] Slot trigger never fired after {FallbackSeconds:F0}s — " +
                            $"sending fallback vote child={fallbackChild} (ballX={_lastBallWorldX:F2})");
                        CoopNavigateState.LocalVoteCast = true;
                        fallbackSender.Send(new NavigateVoteEvent { ChildIndex = fallbackChild });
                        _pendingFallbackChildIndex = -1;
                    }
                }

                return;
            }

            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }

            // Ignore clicks consumed by interactive UI (Buttons). Non-interactive
            // overlays (raycast-blocking sprites, peg layout frames) do NOT count.
            if (Patches.MultiplayerClientPatches.IsPointerOverInteractiveUI())
            {
                MultiplayerPlugin.Logger?.LogInfo(
                    "[CoopNavigate/ClientInput] Click ignored — pointer over interactive UI");
                return;
            }

            var childCount = Math.Max(1, CoopNavigateState.ChildNodeCount);
            var childIndex = ResolveChildIndexFromMouse(childCount);

            MultiplayerPlugin.Logger?.LogInfo(
                $"[CoopNavigate/ClientInput] Click detected at " +
                $"x={Input.mousePosition.x:F0}/{Screen.width} -> child={childIndex} " +
                $"(childCount={childCount})");

            if (childIndex < 0)
            {
                return;
            }

            // Fire the ball locally and let the slot-trigger Harmony prefix
            // (CoopNavigatePatches.NavOnly_HandleSlot_Prefix /
            // PostBattle_HandleSlot_Prefix) submit the vote when the ball
            // actually lands. We DON'T send the vote on click — that would
            // reveal the chosen side before the ball physically arrives.
            FireNavBall();

            _shotFired = true;
            _shotFiredAt = Time.unscaledTime;
            _pendingFallbackChildIndex = childIndex;
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[CoopNavigate/ClientInput] Update failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Track the active nav ball's world position each frame after firing.
    /// Stores the latest non-spawn-point sample so the fallback vote can use
    /// where the ball actually was, not the initial click position.
    /// </summary>
    private void TrackActiveBallPosition()
    {
        try
        {
            var pb = FindActiveNavBall();
            if (pb == null || pb.transform == null)
            {
                return;
            }

            var pos = pb.transform.position;
            _lastBallWorldX = pos.x;
            _lastBallWorldY = pos.y;
        }
        catch
        {
        }
    }

    private static PachinkoBall FindActiveNavBall()
    {
        try
        {
            var nocs = Resources.FindObjectsOfTypeAll<global::NavOnlyController>();
            if (nocs != null)
            {
                var ballField = HarmonyLib.AccessTools.Field(typeof(global::NavOnlyController), "_ball");
                foreach (var noc in nocs)
                {
                    if (noc == null || noc.gameObject == null || !noc.gameObject.scene.IsValid())
                    {
                        continue;
                    }

                    var go = ballField?.GetValue(noc) as GameObject;
                    var pb = go?.GetComponent<PachinkoBall>();
                    if (pb != null)
                    {
                        return pb;
                    }
                }
            }

            var bc = UnityEngine.Object.FindObjectOfType<global::Battle.BattleController>();
            if (bc != null)
            {
                var activeField = HarmonyLib.AccessTools.Field(typeof(global::Battle.BattleController), "_activePachinkoBall");
                var go = activeField?.GetValue(bc) as GameObject;
                return go?.GetComponent<PachinkoBall>();
            }
        }
        catch
        {
        }

        return null;
    }

    /// <summary>
    /// Pick the child index whose slot manager is horizontally closest to the
    /// ball's last known world X. Returns -1 when slot managers aren't available
    /// or no ball position was ever sampled — caller should fall back to the
    /// pre-shot click index.
    /// </summary>
    private int ResolveFallbackChildFromBallPosition()
    {
        if (float.IsNaN(_lastBallWorldX))
        {
            return -1;
        }

        var childCount = Math.Max(1, CoopNavigateState.ChildNodeCount);
        var slotXs = TryGetSlotManagerWorldX();
        if (slotXs == null)
        {
            return -1;
        }

        int leftSlot = 0, centerSlot = 1, rightSlot = 2;
        if (slotXs[leftSlot] > slotXs[rightSlot])
        {
            (leftSlot, rightSlot) = (rightSlot, leftSlot);
        }

        // Closest of the three slot X positions to the ball's X.
        var bestSlot = leftSlot;
        var bestDist = Mathf.Abs(_lastBallWorldX - slotXs[leftSlot]);
        var dCenter = Mathf.Abs(_lastBallWorldX - slotXs[centerSlot]);
        if (dCenter < bestDist)
        {
            bestDist = dCenter;
            bestSlot = centerSlot;
        }

        var dRight = Mathf.Abs(_lastBallWorldX - slotXs[rightSlot]);
        if (dRight < bestDist)
        {
            bestSlot = rightSlot;
        }

        // Map slot → child. Mirrors ResolvePostBattleChildIndex / ResolveNavOnlyChildIndex.
        if (bestSlot == leftSlot)
        {
            return 0;
        }

        if (bestSlot == rightSlot)
        {
            return childCount - 1;
        }

        // center
        if (childCount == 1)
        {
            return 0;
        }

        if (childCount > 2)
        {
            return 1;
        }

        // childCount == 2: center is dud — pick whichever side the ball is closer to.
        return _lastBallWorldX < slotXs[centerSlot] ? 0 : (childCount - 1);
    }

    /// <summary>
    /// Returns world X of [left, center, right] slot managers, in that order
    /// (NOT spatially sorted). Caller normalizes orientation. Returns null when
    /// slot managers can't be found (event scenes that don't have them, or the
    /// scene was already torn down).
    /// </summary>
    private static float[] TryGetSlotManagerWorldX()
    {
        try
        {
            // Try PostBattleController first — battle nav source.
            var bc = UnityEngine.Object.FindObjectOfType<global::Battle.PostBattleController>();
            if (bc != null)
            {
                var leftF = HarmonyLib.AccessTools.Field(typeof(global::Battle.PostBattleController), "_leftSlotManager");
                var centerF = HarmonyLib.AccessTools.Field(typeof(global::Battle.PostBattleController), "_centerSlotManager");
                var rightF = HarmonyLib.AccessTools.Field(typeof(global::Battle.PostBattleController), "_rightSlotManager");
                var l = leftF?.GetValue(bc) as global::Battle.SlotManager;
                var c = centerF?.GetValue(bc) as global::Battle.SlotManager;
                var r = rightF?.GetValue(bc) as global::Battle.SlotManager;
                if (l != null && c != null && r != null)
                {
                    return new[] { l.transform.position.x, c.transform.position.x, r.transform.position.x };
                }
            }

            var nocs = Resources.FindObjectsOfTypeAll<global::NavOnlyController>();
            if (nocs != null)
            {
                var leftF = HarmonyLib.AccessTools.Field(typeof(global::NavOnlyController), "_leftSlotManager");
                var centerF = HarmonyLib.AccessTools.Field(typeof(global::NavOnlyController), "_centreSlotManager");
                var rightF = HarmonyLib.AccessTools.Field(typeof(global::NavOnlyController), "_rightSlotManager");
                foreach (var noc in nocs)
                {
                    if (noc == null || noc.gameObject == null || !noc.gameObject.scene.IsValid())
                    {
                        continue;
                    }

                    var l = leftF?.GetValue(noc) as global::Battle.SlotManager;
                    var c = centerF?.GetValue(noc) as global::Battle.SlotManager;
                    var r = rightF?.GetValue(noc) as global::Battle.SlotManager;
                    if (l != null && r != null)
                    {
                        var cx = c != null ? c.transform.position.x : (l.transform.position.x + r.transform.position.x) * 0.5f;
                        return new[] { l.transform.position.x, cx, r.transform.position.x };
                    }
                }
            }
        }
        catch
        {
        }

        return null;
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

    /// <summary>
    /// Fire whichever nav ball we can find (NavOnlyController._ball for shop→nav,
    /// or BattleController._activePachinkoBall for post-battle nav). Going through
    /// PachinkoBall.Fire() gives the player visual feedback — the orb actually
    /// shoots in the aim direction. PachinkoBallPatches.AllowNavigateLogic lets
    /// the call through. We don't need the slot trigger to fire (the vote is
    /// already sent); we just want the visual.
    /// </summary>
    private static void FireNavBall()
    {
        try
        {
            var fired = false;

            var nocs = Resources.FindObjectsOfTypeAll<global::NavOnlyController>();
            if (nocs != null)
            {
                var ballField = HarmonyLib.AccessTools.Field(typeof(global::NavOnlyController), "_ball");
                foreach (var noc in nocs)
                {
                    if (noc == null || noc.gameObject == null || !noc.gameObject.scene.IsValid())
                    {
                        continue;
                    }

                    var ballGO = ballField?.GetValue(noc) as GameObject;
                    var pb = ballGO?.GetComponent<PachinkoBall>();
                    if (pb != null && TryFire(pb))
                    {
                        fired = true;
                    }
                }
            }

            if (!fired)
            {
                var bc = UnityEngine.Object.FindObjectOfType<global::Battle.BattleController>();
                if (bc != null)
                {
                    var activeField = HarmonyLib.AccessTools.Field(typeof(global::Battle.BattleController), "_activePachinkoBall");
                    var activeGO = activeField?.GetValue(bc) as GameObject;
                    var pb = activeGO?.GetComponent<PachinkoBall>();
                    if (pb != null)
                    {
                        TryFire(pb);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopNavigate/ClientInput] FireNavBall failed: {ex.Message}");
        }
    }

    private static bool TryFire(PachinkoBall pb)
    {
        try
        {
            if (pb.CurrentState != PachinkoBall.FireballState.AIMING)
            {
                var stateProp = HarmonyLib.AccessTools.Property(typeof(PachinkoBall), "CurrentState");
                stateProp?.GetSetMethod(true)?.Invoke(pb, new object[] { PachinkoBall.FireballState.AIMING });
            }

            var origin = pb.transform.position;
            var aim = pb.aimVector;

            pb.Fire();
            MultiplayerPlugin.Logger?.LogInfo(
                $"[CoopNavigate/ClientInput] Fired nav ball: aim=({aim.x:F2},{aim.y:F2})");

            BroadcastShot(origin, aim);
            return true;
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopNavigate/ClientInput] TryFire failed: {ex.Message}");
            return false;
        }
    }

    private static void BroadcastShot(Vector3 origin, Vector2 aim)
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null || !services.TryResolve<IMessageSender>(out var sender))
            {
                return;
            }

            var slot = CoopSlotHelper.GetLocalSlotIndex(services);
            sender.Send(new NavBallShotEvent
            {
                Slot = slot,
                OriginX = origin.x,
                OriginY = origin.y,
                AimX = aim.x,
                AimY = aim.y,
            });
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopNavigate/ClientInput] BroadcastShot failed: {ex.Message}");
        }
    }
}
