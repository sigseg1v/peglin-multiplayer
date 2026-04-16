using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class DamageTextServerHandler : IServerHandler<DamageTextEvent>
{
    public DamageTextEvent Handle(DamageTextEvent e) => e;
}
