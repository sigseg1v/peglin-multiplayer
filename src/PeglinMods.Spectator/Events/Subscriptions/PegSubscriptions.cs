using BepInEx.Logging;
using PeglinMods.Spectator.Events.Network.Peg;
using PeglinMods.Spectator.Spectator;

namespace PeglinMods.Spectator.Events.Subscriptions;

public sealed class PegSubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly ManualLogSource _log;

    public PegSubscriptions(IGameEventRegistry registry, ManualLogSource log)
    {
        _registry = registry;
        _log = log;
    }

    private static bool IsHosting =>
        SpectatorPlugin.Services?.TryResolve<ISpectatorMode>(out var mode) == true && mode.IsHosting;

    public void Subscribe()
    {
        Peg.OnPegHit += OnPegHit;
        Peg.OnPegActivated += OnPegActivated;
        Peg.OnPegDestroyed += OnPegDestroyed;
        _log.LogInfo("PegSubscriptions registered");
    }

    public void Unsubscribe()
    {
        Peg.OnPegHit -= OnPegHit;
        Peg.OnPegActivated -= OnPegActivated;
        Peg.OnPegDestroyed -= OnPegDestroyed;
    }

    private void OnPegHit(Peg.PegType pegType, Peg peg)
    {
        if (!IsHosting) return;
        var pos = peg != null ? peg.transform.position : UnityEngine.Vector3.zero;
        _registry.Dispatch(new PegHitEvent
        {
            PegType = (int)pegType,
            PosX = pos.x,
            PosY = pos.y
        });
    }

    private void OnPegActivated(Peg.PegType pegType, Peg peg)
    {
        if (!IsHosting) return;
        var pos = peg != null ? peg.transform.position : UnityEngine.Vector3.zero;
        _registry.Dispatch(new PegActivatedEvent
        {
            PegType = (int)pegType,
            PosX = pos.x,
            PosY = pos.y
        });
    }

    private void OnPegDestroyed(Peg.PegType pegType, Peg peg)
    {
        if (!IsHosting) return;
        var pos = peg != null ? peg.transform.position : UnityEngine.Vector3.zero;
        _registry.Dispatch(new PegDestroyedEvent
        {
            PegType = (int)pegType,
            PosX = pos.x,
            PosY = pos.y
        });
    }
}
