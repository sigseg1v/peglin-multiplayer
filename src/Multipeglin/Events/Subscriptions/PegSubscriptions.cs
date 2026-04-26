using BepInEx.Logging;
using Multipeglin.Events.Network.Peg;
using Multipeglin.Multiplayer;
using Multipeglin.Utility;

namespace Multipeglin.Events.Subscriptions;

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
        MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var mode) == true && mode.IsHosting;

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
        if (!IsHosting)
        {
            return;
        }

        var pos = peg != null ? peg.transform.position : UnityEngine.Vector3.zero;
        var pegId = MultiplayerPlugin.Services?.TryResolve<PegIdentifier>(out var p) == true ? p : null;

        int hitCount = -1, coinCount = -1, shieldHits = -1, shieldLimit = -1;
        if (peg != null)
        {
            try
            { if (peg is Bomb bomb)
                {
                    hitCount = bomb.HitCount;
                }
            }
            catch { }

            try
            {
                var overlayField = HarmonyLib.AccessTools.Field(typeof(Peg), "PegCoinOverlayInstance");
                var overlay = overlayField?.GetValue(peg) as Battle.PegBehaviour.PegCoinOverlay;
                if (overlay != null)
                {
                    coinCount = overlay.NumCoins;
                }
            }
            catch { }

            try
            {
                var overlayField = HarmonyLib.AccessTools.Field(typeof(Peg), "PegShieldOverlayInstance");
                var shield = overlayField?.GetValue(peg) as Battle.PegBehaviour.PegShieldOverlay;
                if (shield != null)
                { shieldHits = shield.hitCount; shieldLimit = shield.hitLimit; }
            }
            catch { }
        }

        _registry.Dispatch(new PegHitEvent
        {
            PegType = (int)pegType,
            PosX = pos.x,
            PosY = pos.y,
            PegGuid = pegId?.GetGuid(peg),
            HitCount = hitCount,
            CoinCount = coinCount,
            ShieldHitCount = shieldHits,
            ShieldHitLimit = shieldLimit,
        });
    }

    private void OnPegActivated(Peg.PegType pegType, Peg peg)
    {
        if (!IsHosting)
        {
            return;
        }

        var pos = peg != null ? peg.transform.position : UnityEngine.Vector3.zero;
        var pegId = MultiplayerPlugin.Services?.TryResolve<PegIdentifier>(out var p) == true ? p : null;
        _registry.Dispatch(new PegActivatedEvent
        {
            PegType = (int)pegType,
            PosX = pos.x,
            PosY = pos.y,
            PegGuid = pegId?.GetGuid(peg),
        });
    }

    private void OnPegDestroyed(Peg.PegType pegType, Peg peg)
    {
        if (!IsHosting)
        {
            return;
        }

        var pos = peg != null ? peg.transform.position : UnityEngine.Vector3.zero;
        var pegId = MultiplayerPlugin.Services?.TryResolve<PegIdentifier>(out var p) == true ? p : null;
        _registry.Dispatch(new PegDestroyedEvent
        {
            PegType = (int)pegType,
            PosX = pos.x,
            PosY = pos.y,
            PegGuid = pegId?.GetGuid(peg),
        });
    }
}
