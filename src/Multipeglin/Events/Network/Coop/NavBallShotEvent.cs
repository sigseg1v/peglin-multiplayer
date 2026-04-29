namespace Multipeglin.Events.Network.Coop;

/// <summary>
/// Broadcast when any player fires their navigate ball during the parallel-shoot
/// vote phase. Receivers spawn a ghost ball at OriginX/OriginY with the given
/// aim so every instance shows every player's shot — even the player whose
/// click triggered it (their own native Fire() handles their primary ball; this
/// event is suppressed on receive when Slot == localSlot).
///
/// Flow:
///  - Client: CoopNavigateClientInput sends after pb.Fire()
///  - Host:   PachinkoBallPatches.Fire postfix dispatches when nav active
///  - Server: NavBallShotServerHandler returns the event so it's rebroadcast to
///            every client.
///  - Client: NavBallShotClientHandler instantiates a ghost orb and fires it.
/// </summary>
public class NavBallShotEvent
{
    public int Slot { get; set; }

    public float OriginX { get; set; }

    public float OriginY { get; set; }

    public float AimX { get; set; }

    public float AimY { get; set; }
}
