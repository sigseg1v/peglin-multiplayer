using BepInEx.Logging;
using PeglinMods.Spectator.Events.Network.Ball;
using PeglinMods.Spectator.Spectator;
using UnityEngine;

namespace PeglinMods.Spectator.Events.Subscriptions;

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
        SpectatorPlugin.Services?.TryResolve<ISpectatorMode>(out var mode) == true && mode.IsHosting;

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
        _registry.Dispatch(new ShotFiredEvent
        {
            AimX = aimVector.x,
            AimY = aimVector.y
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
