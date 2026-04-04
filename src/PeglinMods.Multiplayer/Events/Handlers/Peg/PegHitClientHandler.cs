namespace PeglinMods.Multiplayer.Events.Handlers.Peg;

using System;
using PeglinMods.Multiplayer.Events.Network.Peg;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Utility;

public sealed class PegHitClientHandler : IClientHandler<PegHitEvent>
{
    public void Handle(PegHitEvent e)
    {
        // Peg hit state is handled authoritatively by the heartbeat snapshot.
        // Do NOT fire Peg.OnPegHit — subscribers run game logic (battle controller
        // state transitions, relic effects, damage calculations, status effects)
        // that the dumb-canvas client must not execute.
    }
}
