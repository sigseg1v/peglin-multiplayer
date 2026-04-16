namespace Multipeglin.Events.Handlers.Battle;

using Multipeglin.Events.Network.Battle;

public sealed class AwaitingShotServerHandler : IServerHandler<AwaitingShotEvent>
{
    public AwaitingShotEvent Handle(AwaitingShotEvent networkEvent) => networkEvent;
}
