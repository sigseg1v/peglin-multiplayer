using BepInEx.Logging;
using Multipeglin.Events.Network.Peg;
using Multipeglin.Multiplayer;
using Multipeglin.Utility;

namespace Multipeglin.Events.Subscriptions;

public sealed class PegSubscriptions
{
    // cached once: AccessTools.Field is an uncached MetadataToken lookup, and
    // OnPegHit fires per peg hit (hundreds per shot). Resolving these on every
    // call burned ~2000 reflection lookups per shot.
    private static readonly System.Reflection.FieldInfo CoinOverlayField
        = HarmonyLib.AccessTools.Field(typeof(Peg), "PegCoinOverlayInstance");

    private static readonly System.Reflection.FieldInfo ShieldOverlayField
        = HarmonyLib.AccessTools.Field(typeof(Peg), "PegShieldOverlayInstance");

    // Cached service handles — set-once for plugin lifetime. The previous code
    // resolved IMultiplayerMode + PegIdentifier per peg hit (× 3 handlers).
    private static IMultiplayerMode _cachedMode;
    private static PegIdentifier _cachedPegId;

    private readonly IGameEventRegistry _registry;
    private readonly ManualLogSource _log;

    public PegSubscriptions(IGameEventRegistry registry, ManualLogSource log)
    {
        _registry = registry;
        _log = log;
    }

    private static bool IsHosting
    {
        get
        {
            if (_cachedMode == null)
            {
                var services = MultiplayerPlugin.Services;
                if (services == null || !services.TryResolve(out _cachedMode))
                {
                    return false;
                }
            }

            return _cachedMode.IsHosting;
        }
    }

    private static PegIdentifier GetPegId()
    {
        if (_cachedPegId == null)
        {
            MultiplayerPlugin.Services?.TryResolve(out _cachedPegId);
        }

        return _cachedPegId;
    }

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
        var pegId = GetPegId();

        int hitCount = -1, coinCount = -1, shieldHits = -1, shieldLimit = -1;
        if (peg != null)
        {
            try
            {
                if (peg is Bomb bomb)
                {
                    hitCount = bomb.HitCount;
                }
            }
            catch
            {
            }

            try
            {
                var overlay = CoinOverlayField?.GetValue(peg) as Battle.PegBehaviour.PegCoinOverlay;
                if (overlay != null)
                {
                    coinCount = overlay.NumCoins;
                }
            }
            catch
            {
            }

            try
            {
                var shield = ShieldOverlayField?.GetValue(peg) as Battle.PegBehaviour.PegShieldOverlay;
                if (shield != null)
                {
                    shieldHits = shield.hitCount;
                    shieldLimit = shield.hitLimit;
                }
            }
            catch
            {
            }
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
        var pegId = GetPegId();
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
        var pegId = GetPegId();
        _registry.Dispatch(new PegDestroyedEvent
        {
            PegType = (int)pegType,
            PosX = pos.x,
            PosY = pos.y,
            PegGuid = pegId?.GetGuid(peg),
        });
    }
}
