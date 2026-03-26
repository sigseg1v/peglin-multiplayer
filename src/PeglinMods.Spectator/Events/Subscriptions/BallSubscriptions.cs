using BepInEx.Logging;
using PeglinMods.Spectator.Events.Network.Ball;
using UnityEngine;

namespace PeglinMods.Spectator.Events.Subscriptions;

public class BallSubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly ManualLogSource _log;

    private PachinkoBall.PachinkoBallFired _onShotFired;
    private PachinkoBall.PachinkoBallWallBounce _onWallBounce;
    private PachinkoBall.PachinkoBallDestroyed _onBallDestroyed;

    public BallSubscriptions(IGameEventRegistry registry, ManualLogSource log)
    {
        _registry = registry;
        _log = log;
    }

    public void Subscribe()
    {
        _onShotFired = (Vector2 aimVector) =>
        {
            _registry.Dispatch(new ShotFiredEvent
            {
                AimX = aimVector.x,
                AimY = aimVector.y
            });
        };
        PachinkoBall.OnShotFired += _onShotFired;

        _onWallBounce = (Vector3 pos) =>
        {
            _registry.Dispatch(new BallWallBounceEvent
            {
                PosX = pos.x,
                PosY = pos.y,
                PosZ = pos.z
            });
        };
        PachinkoBall.OnPachinkoBallWallBounce += _onWallBounce;

        _onBallDestroyed = (PachinkoBall pBall) =>
        {
            _registry.Dispatch(new BallDestroyedEvent());
        };
        PachinkoBall.OnPachinkoBallDestroyed += _onBallDestroyed;

        _log.LogInfo("BallSubscriptions registered");
    }

    public void Unsubscribe()
    {
        PachinkoBall.OnShotFired -= _onShotFired;
        PachinkoBall.OnPachinkoBallWallBounce -= _onWallBounce;
        PachinkoBall.OnPachinkoBallDestroyed -= _onBallDestroyed;
    }
}
