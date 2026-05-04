using System;
using Battle;
using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Applies the host's authoritative SpecialSlotController config to the local
/// slotTriggers. The client's own TurnComplete is prefix-blocked, so this is
/// the only path that sets slot multipliers, portals, and damage flames on
/// the client.
/// </summary>
public sealed class SlotConfigClientHandler : IClientHandler<SlotConfigEvent>
{
    public void Handle(SlotConfigEvent networkEvent)
    {
        try
        {
            var ssc = UnityEngine.Object.FindObjectOfType<SpecialSlotController>();
            if (ssc == null || ssc.slotTriggers == null)
            {
                return;
            }

            var triggers = ssc.slotTriggers;
            var mult = networkEvent.Multipliers;
            var portals = networkEvent.PortalsOn;
            var flames = networkEvent.FlamesOn;
            var bottomColor = ssc.bottomPortalColor;

            for (var i = 0; i < triggers.Length; i++)
            {
                var t = triggers[i];
                if (t == null)
                {
                    continue;
                }

                if (mult != null && i < mult.Length)
                {
                    t.multiplier = mult[i];
                }

                if (portals != null && i < portals.Length)
                {
                    t.TogglePortal(portals[i], bottomColor);
                }

                if (flames != null && i < flames.Length)
                {
                    t.SetSlotFlame(flames[i]);
                }
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[SlotConfig] apply failed: {ex.Message}");
        }
    }
}
