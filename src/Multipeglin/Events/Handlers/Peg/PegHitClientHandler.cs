namespace Multipeglin.Events.Handlers.Peg;

using System;
using Multipeglin.Events.Network.Peg;
using Multipeglin.Multiplayer;
using Multipeglin.Utility;

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
