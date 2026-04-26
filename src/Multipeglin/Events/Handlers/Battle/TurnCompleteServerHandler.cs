using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class TurnCompleteServerHandler : IServerHandler<TurnCompleteEvent>
{
    public TurnCompleteEvent Handle(TurnCompleteEvent networkEvent) => networkEvent;
}
