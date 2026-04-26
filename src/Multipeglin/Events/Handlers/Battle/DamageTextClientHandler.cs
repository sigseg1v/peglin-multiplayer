using System;
using Multipeglin.Events.Network.Battle;
using Multipeglin.Multiplayer;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class DamageTextClientHandler : IClientHandler<DamageTextEvent>
{
    public void Handle(DamageTextEvent e)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating)
                return;

            var dcd = UnityEngine.Object.FindObjectOfType<DamageCountDisplay>();
            if (dcd == null)
                return;

            var pos = new Vector2(e.PosX, e.PosY);
            var color = new Color(e.R, e.G, e.B, e.A);
            dcd.CreateText(e.Text, pos, color);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"DamageText handler failed: {ex.Message}");
        }
    }
}
