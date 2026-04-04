namespace PeglinMods.Multiplayer.Events.Handlers.Peg;

using System;
using PeglinMods.Multiplayer.Events.Network.Peg;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Utility;

public sealed class PegActivatedClientHandler : IClientHandler<PegActivatedEvent>
{
    public void Handle(PegActivatedEvent networkEvent)
    {
        // Peg activation is handled authoritatively by the heartbeat snapshot.
        // Do NOT call peg.PegActivated() — it fires Peg.OnPegActivated which
        // runs game logic on the client (attack resolution, relic effects,
        // status effect application, damage calculations).
        // Do NOT fire the Peg.OnPegActivated delegate for the same reason.
        // The heartbeat applier will clear/destroy pegs based on host state.
    }
}
