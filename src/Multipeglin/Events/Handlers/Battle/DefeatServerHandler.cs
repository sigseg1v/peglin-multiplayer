namespace Multipeglin.Events.Handlers.Battle;

using Multipeglin.Events.Network.Battle;

public sealed class DefeatServerHandler : IServerHandler<DefeatEvent>
{
    public DefeatEvent Handle(DefeatEvent networkEvent) => networkEvent;
}
