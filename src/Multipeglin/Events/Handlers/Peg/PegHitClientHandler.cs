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
    /// <summary>
    /// MULTIPEGLIN_DEBUG, read once. PegHitEvent is the highest-frequency event in
    /// the game (every peg collision of every shot); Environment.GetEnvironmentVariable
    /// hits the process environment block on each call. Env vars are set before
    /// launch and never change mid-run.
    /// </summary>
    private static readonly bool DebugEnabled = ReadDebugFlag();

    /// <summary>Peg.PegCoinOverlayInstance; AccessTools.Field is uncached per call.</summary>
    private static readonly System.Reflection.FieldInfo CoinOverlayField
        = AccessTools.Field(typeof(global::Peg), "PegCoinOverlayInstance");

    private static bool ReadDebugFlag()
    {
        var dbg = Environment.GetEnvironmentVariable("MULTIPEGLIN_DEBUG");
        return dbg == "1" || string.Equals(dbg, "true", StringComparison.OrdinalIgnoreCase);
    }

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

            // Bomb fuse / detonate — full ForceState (material + _detonated + hide).
            if (e.HitCount >= 0 && peg is Bomb bomb)
            {
                var before = bomb.HitCount;
                if (DebugEnabled && !BombVisualHelper.MatchesState(bomb, e.HitCount))
                {
                    MultiplayerPlugin.Logger?.LogWarning(
                        $"[BombSync] PEGHIT guid={e.PegGuid ?? "none"} " +
                        $"{before}→{e.HitCount} pos=({e.PosX:F1},{e.PosY:F1})");
                }

                // Always force. MatchesState cannot see the Animator's NumHits
                // parameter, which is what actually draws the lit fuse, so using it
                // as a skip condition strands bombs in a lit state the host never had.
                BombVisualHelper.ForceState(bomb, e.HitCount, MultiplayerPlugin.Logger);
            }

            // Coin overlay: collect the diff so the visual matches the host.
            if (e.CoinCount >= 0)
            {
                try
                {
                    var overlay = CoinOverlayField?.GetValue(peg) as global::Battle.PegBehaviour.PegCoinOverlay;
                    if (overlay != null && overlay.NumCoins > e.CoinCount)
                    {
                        overlay.CollectCoins(overlay.NumCoins - e.CoinCount);
                    }
                }
                catch
                {
                }
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
                            if (rend != null)
                            {
                                rend.enabled = e.ShieldHitCount < e.ShieldHitLimit;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"PegHit handler failed: {ex.Message}");
        }
    }
}
