
using System;
using HarmonyLib;
using Multipeglin.Events.Network.Peg;
using Multipeglin.Multiplayer;
using Multipeglin.Utility;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Peg;
/// <summary>
/// Real-time visual sync for pegs that get hit but DON'T pop (bombs ticking
/// down hit count, coin pegs decrementing on collection, shield overlays).
/// Popping pegs are handled by <see cref="PegActivatedClientHandler"/>.
/// We do NOT fire Peg.OnPegHit here — subscribers run game logic the
/// dumb-canvas client must not execute. We only patch the visual counters
/// so they don't have to wait for the 1s heartbeat to catch up.
/// </summary>
public sealed class PegHitClientHandler : IClientHandler<PegHitEvent>
{
    public void Handle(PegHitEvent e)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating)
            {
                return;
            }

            if (string.IsNullOrEmpty(e.PegGuid))
            {
                return;
            }

            var pegId = MultiplayerPlugin.Services?.TryResolve<PegIdentifier>(out var p) == true ? p : null;
            var peg = pegId?.Find(e.PegGuid);
            if (peg == null || !peg.gameObject.activeSelf)
            {
                return;
            }

            // Bomb hit count → animator NumHits.
            if (e.HitCount >= 0 && peg is Bomb bomb)
            {
                if (bomb.HitCount != e.HitCount)
                {
                    bomb.HitCount = e.HitCount;
                    try
                    { bomb.GetComponent<Animator>()?.SetInteger("NumHits", e.HitCount); }
                    catch { }
                }
            }

            // Coin overlay: collect the diff so the visual matches the host.
            if (e.CoinCount >= 0)
            {
                try
                {
                    var overlayField = AccessTools.Field(typeof(global::Peg), "PegCoinOverlayInstance");
                    var overlay = overlayField?.GetValue(peg) as global::Battle.PegBehaviour.PegCoinOverlay;
                    if (overlay != null && overlay.NumCoins > e.CoinCount)
                    {
                        overlay.CollectCoins(overlay.NumCoins - e.CoinCount);
                    }
                }
                catch { }
            }

            // Shield overlay hit count (e.g. after a successful block).
            if (e.ShieldHitCount >= 0 && e.ShieldHitLimit > 0)
            {
                try
                {
                    var overlayField = AccessTools.Field(typeof(global::Peg), "PegShieldOverlayInstance");
                    var shield = overlayField?.GetValue(peg) as global::Battle.PegBehaviour.PegShieldOverlay;
                    if (shield != null && shield.hitCount != e.ShieldHitCount)
                    {
                        shield.hitCount = e.ShieldHitCount;
                        shield.hitLimit = e.ShieldHitLimit;
                        try
                        {
                            var anim = shield.GetComponent<Animator>();
                            anim?.SetInteger(Animator.StringToHash("HitCount"), e.ShieldHitCount);
                            var rend = shield.GetComponent<SpriteRenderer>();
                            rend?.enabled = e.ShieldHitCount < e.ShieldHitLimit;
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"PegHit handler failed: {ex.Message}");
        }
    }
}
