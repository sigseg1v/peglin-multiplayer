using System;
using System.Collections.Generic;
using HarmonyLib;
using Multipeglin.Events.Network.Coop;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// On every receiver: spawn a ghost nav ball and fire it in the direction the
/// remote player aimed. Skips the spawn when the slot is the local player —
/// they already fired their own ball locally. Ghost balls share the local
/// _navigationOrb prefab so they look identical to whatever this scene's nav
/// ball is (NavOnlyController for shop/treasure/dialogue, BattleController for
/// post-battle).
/// </summary>
public sealed class NavBallShotClientHandler : IClientHandler<NavBallShotEvent>
{
    private static readonly List<GameObject> _spawnedGhosts = new List<GameObject>();

    /// <summary>
    /// Destroys every ghost nav ball spawned during the current phase. Called on
    /// phase reset so leftover ghosts (sticky AIMING-state navigation orbs that
    /// would otherwise show a peglin-face aimer on the host during the next
    /// scene's idle moments) cannot persist past the nav phase.
    /// </summary>
    public static void DestroyAllGhostBalls()
    {
        if (_spawnedGhosts.Count == 0)
        {
            return;
        }

        var destroyed = 0;
        foreach (var go in _spawnedGhosts)
        {
            if (go == null)
            {
                continue;
            }

            try
            {
                UnityEngine.Object.Destroy(go);
                destroyed++;
            }
            catch
            {
            }
        }

        _spawnedGhosts.Clear();

        if (destroyed > 0)
        {
            MultiplayerPlugin.Logger?.LogInfo($"[NavBallShot] Destroyed {destroyed} leftover ghost nav balls");
        }
    }

    public void Handle(NavBallShotEvent networkEvent)
    {
        try
        {
            if (!CoopNavigateState.PhaseActive || CoopNavigateState.Resolved)
            {
                return;
            }

            var services = MultiplayerPlugin.Services;
            if (services == null)
            {
                return;
            }

            var localSlot = CoopSlotHelper.GetLocalSlotIndex(services);
            if (localSlot >= 0 && localSlot == networkEvent.Slot)
            {
                return; // own ball already fired locally
            }

            SpawnGhostNavBall(networkEvent);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[NavBallShot] ghost spawn failed: {ex.Message}");
        }
    }

    private static void SpawnGhostNavBall(NavBallShotEvent ev)
    {
        var origin = new Vector3(ev.OriginX, ev.OriginY, 0f);
        var aim = new Vector2(ev.AimX, ev.AimY);

        // Try NavOnlyController first (shop/treasure/dialogue nav). Its private
        // _navigationOrb prefab + _ballStartTransform mirror exactly what the
        // host instantiated, so the ghost matches.
        var prefab = TryGetNavOnlyPrefab(out var ballStartFallback)
            ?? TryGetBattleControllerNavPrefab(out ballStartFallback);

        if (prefab == null)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[NavBallShot] no nav-orb prefab available for slot {ev.Slot}");
            return;
        }

        var spawnPos = origin;
        if (spawnPos.sqrMagnitude < 0.001f && ballStartFallback != null)
        {
            spawnPos = ballStartFallback.position;
        }

        var go = UnityEngine.Object.Instantiate(prefab, spawnPos, Quaternion.identity);
        var pb = go.GetComponent<PachinkoBall>();
        if (pb == null)
        {
            UnityEngine.Object.Destroy(go);
            return;
        }

        // Mark as dummy BEFORE firing: skips PredictionManager poking, skips
        // mouse-driven aim updates in DoLateUpdate (so the host's mouse won't
        // rotate this ghost mid-flight), and lets the auto-transition to FIRING
        // kick in if anything leaves it in AIMING.
        pb.IsDummy = true;

        try
        {
            var aimVecField = AccessTools.Field(typeof(PachinkoBall), "_aimVector");
            aimVecField?.SetValue(pb, aim);
        }
        catch
        {
        }

        // Fire(notifyFire: false) sets state=FIRING, simulated=true, and applies
        // force in one go. With IsDummy=true the OnShotFired/PredictionManager
        // side effects are skipped. FireDummy alone leaves _rigid.simulated as
        // whatever the prefab had, which can be false — the ball then sits at
        // its spawn position rotating with the host's mouse cursor.
        try
        {
            pb.Fire(notifyFire: false);
        }
        catch
        {
            try
            {
                pb.FireDummy();
            }
            catch
            {
                // give up; leave the ball sitting at origin
            }
        }

        _spawnedGhosts.Add(go);

        MultiplayerPlugin.Logger?.LogInfo(
            $"[NavBallShot] ghost spawned for slot={ev.Slot} at ({ev.OriginX:F2},{ev.OriginY:F2}) aim=({ev.AimX:F2},{ev.AimY:F2})");
    }

    private static GameObject TryGetNavOnlyPrefab(out Transform ballStart)
    {
        ballStart = null;
        try
        {
            var nocs = Resources.FindObjectsOfTypeAll<global::NavOnlyController>();
            if (nocs == null)
            {
                return null;
            }

            var prefabField = AccessTools.Field(typeof(global::NavOnlyController), "_navigationOrb");
            var startField = AccessTools.Field(typeof(global::NavOnlyController), "_ballStartTransform");

            foreach (var noc in nocs)
            {
                if (noc == null || noc.gameObject == null || !noc.gameObject.scene.IsValid())
                {
                    continue;
                }

                var prefab = prefabField?.GetValue(noc) as GameObject;
                if (prefab != null)
                {
                    ballStart = startField?.GetValue(noc) as Transform;
                    return prefab;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static GameObject TryGetBattleControllerNavPrefab(out Transform ballStart)
    {
        ballStart = null;
        try
        {
            var bc = UnityEngine.Object.FindObjectOfType<global::Battle.BattleController>();
            if (bc == null)
            {
                return null;
            }

            var prefabField = AccessTools.Field(typeof(global::Battle.BattleController), "_navigationOrb");
            var prefab = prefabField?.GetValue(bc) as GameObject;
            return prefab;
        }
        catch
        {
            return null;
        }
    }
}
