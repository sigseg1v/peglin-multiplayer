using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class DefeatServerHandler : IServerHandler<DefeatEvent>
{
    public DefeatEvent Handle(DefeatEvent networkEvent) => networkEvent;
}
