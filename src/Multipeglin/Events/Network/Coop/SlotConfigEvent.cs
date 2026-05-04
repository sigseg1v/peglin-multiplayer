namespace Multipeglin.Events.Network.Coop;

/// <summary>
/// Mirrors the post-shuffle output of <c>SpecialSlotController.TurnComplete</c>
/// from host to client. The native code shuffles <c>_slotMultipliersRelicAmounts</c>
/// using unseeded <c>UnityEngine.Random</c>, so without this sync the host and
/// client choose different slots for Pumpkin Pi (SLOT_PORTAL) portals and
/// SLOT_MULTIPLIERS x-values every turn.
///
/// Flow:
///  - Host: postfix on SpecialSlotController.TurnComplete captures the applied
///    state from each SlotTrigger and dispatches.
///  - Client: prefix on SpecialSlotController.TurnComplete returns false to
///    skip the local shuffle entirely. The handler below applies the host's
///    config to the local slotTriggers.
/// </summary>
public class SlotConfigEvent
{
    public float[] Multipliers { get; set; }

    public bool[] PortalsOn { get; set; }

    public bool[] FlamesOn { get; set; }
}
