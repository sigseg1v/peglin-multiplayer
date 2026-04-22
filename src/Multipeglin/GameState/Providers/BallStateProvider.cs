using System;
using BepInEx.Logging;
using Multipeglin.GameState.Snapshots;
using Multipeglin.Utility;
using UnityEngine;

namespace Multipeglin.GameState.Providers;

/// <summary>
/// Host-side capture of every active non-dummy PachinkoBall in the scene.
/// Assigns GUIDs on first sight and returns an entry per ball with position,
/// velocity, and the orb prefab name so the client can pick the right sprite.
/// </summary>
public class BallStateProvider : IGameStateProvider<BallStateSnapshot>
{
    private readonly ManualLogSource _log;
    private readonly BallIdentifier _ballId;

    public BallStateProvider(ManualLogSource log, BallIdentifier ballId)
    {
        _log = log;
        _ballId = ballId;
    }

    public BallStateSnapshot Capture()
    {
        var snap = new BallStateSnapshot { Timestamp = Time.time };
        try
        {
            var balls = UnityEngine.Object.FindObjectsOfType<PachinkoBall>();
            if (balls == null) return snap;

            foreach (var ball in balls)
            {
                if (ball == null || ball.IsDummy) continue;

                // CoopTempOrb_host is a placeholder stuck off-screen at (-999,-999)
                // used by the coop damage pipeline; never stream it.
                var goName = ball.gameObject.name;
                if (!string.IsNullOrEmpty(goName) && goName.StartsWith("CoopTempOrb"))
                    continue;

                // Skip balls parked at the off-screen position regardless of name.
                var pos = ball.transform.position;
                if (pos.x < -900f && pos.y < -900f) continue;

                // Only sync balls that are actually in flight — skip WAITING/AIMING
                // (those haven't been fired). Multiball children are spawned directly
                // into FIRING state, so they pass.
                if (!ball.IsFiring()) continue;

                var guid = _ballId.GetOrAssignGuid(ball);
                var rb = ball.GetComponent<Rigidbody2D>();
                var vel = rb != null ? rb.velocity : Vector2.zero;
                var scale = ball.transform.localScale;

                // Primary ball: the one tracked by the client-patch _firedBallGO.
                bool isPrimary = ReferenceEquals(ball.gameObject, Patches.MultiplayerClientPatches.PrimaryBall);

                snap.Balls.Add(new BallEntry
                {
                    Guid = guid,
                    PosX = pos.x,
                    PosY = pos.y,
                    VelX = vel.x,
                    VelY = vel.y,
                    OrbName = goName,
                    ScaleX = scale.x,
                    ScaleY = scale.y,
                    IsPrimary = isPrimary,
                });
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[BallProvider] Capture failed: {ex.Message}");
        }
        return snap;
    }
}
