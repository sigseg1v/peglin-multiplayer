using PeglinMods.Multiplayer.Events.Network.Battle;

namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

public sealed class DamageTextServerHandler : IServerHandler<DamageTextEvent>
{
    public DamageTextEvent Handle(DamageTextEvent e) => e;
}
