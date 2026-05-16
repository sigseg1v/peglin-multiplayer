using System;
using System.Collections.Generic;
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
    private readonly List<PachinkoBall> _ballBuffer = new List<PachinkoBall>(64);
    private readonly Dictionary<int, BallComponents> _componentCache = new Dictionary<int, BallComponents>(64);
    private readonly List<int> _pruneBuffer = new List<int>(16);
    private int _captureCount;

    private struct BallComponents
    {
        public PachinkoBall Ball;
        public Rigidbody2D Rb;
        public Transform SpriteTransform;
    }

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
            PachinkoBallRegistry.CopyInto(_ballBuffer);

            foreach (var ball in _ballBuffer)
            {
                if (ball == null || ball.IsDummy)
                {
                    continue;
                }

                // CoopTempOrb_host is a placeholder stuck off-screen at (-999,-999)
                // used by the coop damage pipeline; never stream it.
                var goName = ball.gameObject.name;
                if (!string.IsNullOrEmpty(goName) && goName.StartsWith("CoopTempOrb"))
                {
                    continue;
                }

                // Skip balls parked at the off-screen position regardless of name.
                var pos = ball.transform.position;
                if (pos.x < -900f && pos.y < -900f)
                {
                    continue;
                }

                // Only sync balls that are actually in flight — skip WAITING/AIMING
                // (those haven't been fired). Multiball children are spawned directly
                // into FIRING state, so they pass.
                if (!ball.IsFiring())
                {
                    continue;
                }

                var guid = _ballId.GetOrAssignGuid(ball);
                var components = GetCached(ball);
                var vel = components.Rb != null ? components.Rb.velocity : Vector2.zero;

                // The PachinkoBall's SpriteRenderer lives on a CHILD transform with
                // its own localScale (see PachinkoBall multiball code). The actual
                // rendered world size = root.localScale * spriteChild.localScale.
                // The client flattens the sprite onto a single GameObject, so mirror
                // the sprite's world (lossy) scale, not the root's local scale.
                var scale = components.SpriteTransform != null
                    ? components.SpriteTransform.lossyScale
                    : ball.transform.localScale;

                // Primary ball: the one tracked by the client-patch _firedBallGO.
                var isPrimary = ReferenceEquals(ball.gameObject, Patches.MultiplayerClientPatches.PrimaryBall);

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

        // Lazily prune destroyed balls from the component cache; full sweeps
        // every ~5 seconds (at 20 Hz capture) are cheap and bound the dict size.
        if ((++_captureCount & 0x7F) == 0)
        {
            PruneCache();
        }

        return snap;
    }

    private BallComponents GetCached(PachinkoBall ball)
    {
        // The Ball == ball reference check is load-bearing for correctness
        // across host/disconnect/host cycles: stale entries from a prior
        // session can share an InstanceID with a freshly-spawned ball, and
        // this check forces a re-resolve when the underlying reference
        // doesn't match. Don't drop it.
        var id = ball.GetInstanceID();
        if (_componentCache.TryGetValue(id, out var cached) && cached.Ball == ball)
        {
            return cached;
        }

        var rb = ball.GetComponent<Rigidbody2D>();
        var spriteRenderer = ball.GetComponentInChildren<SpriteRenderer>();
        cached = new BallComponents
        {
            Ball = ball,
            Rb = rb,
            SpriteTransform = spriteRenderer?.transform,
        };
        _componentCache[id] = cached;
        return cached;
    }

    private void PruneCache()
    {
        _pruneBuffer.Clear();
        foreach (var kvp in _componentCache)
        {
            if (kvp.Value.Ball == null)
            {
                _pruneBuffer.Add(kvp.Key);
            }
        }

        foreach (var id in _pruneBuffer)
        {
            _componentCache.Remove(id);
        }
    }
}
