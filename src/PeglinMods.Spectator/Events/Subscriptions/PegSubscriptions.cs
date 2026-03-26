using BepInEx.Logging;
using PeglinMods.Spectator.Events.Network.Peg;

namespace PeglinMods.Spectator.Events.Subscriptions;

public class PegSubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly ManualLogSource _log;

    private Peg.PegHitEvent _onPegHit;
    private Peg.PegHitEvent _onPegActivated;
    private Peg.PegDestroyed _onPegDestroyed;

    public PegSubscriptions(IGameEventRegistry registry, ManualLogSource log)
    {
        _registry = registry;
        _log = log;
    }

    public void Subscribe()
    {
        _onPegHit = (Peg.PegType pegType, Peg peg) =>
        {
            var pos = peg != null ? peg.transform.position : UnityEngine.Vector3.zero;
            _registry.Dispatch(new PegHitEvent
            {
                PegType = (int)pegType,
                PosX = pos.x,
                PosY = pos.y
            });
        };
        Peg.OnPegHit += _onPegHit;

        _onPegActivated = (Peg.PegType pegType, Peg peg) =>
        {
            var pos = peg != null ? peg.transform.position : UnityEngine.Vector3.zero;
            _registry.Dispatch(new PegActivatedEvent
            {
                PegType = (int)pegType,
                PosX = pos.x,
                PosY = pos.y
            });
        };
        Peg.OnPegActivated += _onPegActivated;

        _onPegDestroyed = (Peg.PegType pegType, Peg peg) =>
        {
            var pos = peg != null ? peg.transform.position : UnityEngine.Vector3.zero;
            _registry.Dispatch(new PegDestroyedEvent
            {
                PegType = (int)pegType,
                PosX = pos.x,
                PosY = pos.y
            });
        };
        Peg.OnPegDestroyed += _onPegDestroyed;

        _log.LogInfo("PegSubscriptions registered");
    }

    public void Unsubscribe()
    {
        Peg.OnPegHit -= _onPegHit;
        Peg.OnPegActivated -= _onPegActivated;
        Peg.OnPegDestroyed -= _onPegDestroyed;
    }
}
