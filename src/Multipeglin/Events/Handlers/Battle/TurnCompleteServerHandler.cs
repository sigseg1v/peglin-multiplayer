namespace Multipeglin.Events.Handlers.Battle;

using Multipeglin.Events.Network.Battle;

public sealed class TurnCompleteServerHandler : IServerHandler<TurnCompleteEvent>
{
    public TurnCompleteEvent Handle(TurnCompleteEvent networkEvent) => networkEvent;
}
