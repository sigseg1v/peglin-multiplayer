using BepInEx.Logging;
using PeglinMods.Multiplayer.Events.Network.Ball;
using PeglinMods.Multiplayer.Multiplayer;
using UnityEngine;

namespace PeglinMods.Multiplayer.Events.Subscriptions;

public sealed class BallSubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly ManualLogSource _log;

    public BallSubscriptions(IGameEventRegistry registry, ManualLogSource log)
    {
        _registry = registry;
        _log = log;
    }

    private static bool IsHosting =>
        MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var mode) == true && mode.IsHosting;

    public void Subscribe()
    {
        PachinkoBall.OnShotFired += OnShotFired;
        PachinkoBall.OnPachinkoBallWallBounce += OnWallBounce;
        PachinkoBall.OnPachinkoBallDestroyed += OnBallDestroyed;
        _log.LogInfo("BallSubscriptions registered");
    }

    public void Unsubscribe()
    {
        PachinkoBall.OnShotFired -= OnShotFired;
        PachinkoBall.OnPachinkoBallWallBounce -= OnWallBounce;
        PachinkoBall.OnPachinkoBallDestroyed -= OnBallDestroyed;
    }

    private void OnShotFired(Vector2 aimVector)
    {
        if (!IsHosting) return;

        // Get the current orb name from the active PachinkoBall (not the deck —
        // the current orb was already popped from shuffledDeck during DrawBall,
        // so Peek() would return the NEXT orb, not the fired one).
        string orbName = null;
        float spawnX = 0, spawnY = 0;
        try
        {
            // Find the ball that was just fired
            var balls = Object.FindObjectsOfType<PachinkoBall>();
            foreach (var ball in balls)
            {
                if (ball != null && !ball.IsDummy)
                {
                    orbName = ball.gameObject.name;
                    break;
                }
            }

            var bc = Object.FindObjectOfType<Battle.BattleController>();
            if (bc != null)
            {
                var playerField = HarmonyLib.AccessTools.Field(typeof(Battle.BattleController), "_playerTransform");
                var pt = playerField?.GetValue(bc) as Transform;
                if (pt != null) { spawnX = pt.position.x; spawnY = pt.position.y; }
            }
        }
        catch { }

        _log.LogInfo($"[BallSub] ShotFired: aim=({aimVector.x:F2},{aimVector.y:F2}), orb={orbName}, spawn=({spawnX:F1},{spawnY:F1})");
        _registry.Dispatch(new ShotFiredEvent
        {
            AimX = aimVector.x,
            AimY = aimVector.y,
            OrbName = orbName,
            SpawnX = spawnX,
            SpawnY = spawnY,
        });
    }

    private void OnWallBounce(Vector3 pos)
    {
        if (!IsHosting) return;
        _registry.Dispatch(new BallWallBounceEvent
        {
            PosX = pos.x,
            PosY = pos.y,
            PosZ = pos.z
        });
    }

    private void OnBallDestroyed(PachinkoBall pBall)
    {
        if (!IsHosting) return;
        _registry.Dispatch(new BallDestroyedEvent());
    }
}
