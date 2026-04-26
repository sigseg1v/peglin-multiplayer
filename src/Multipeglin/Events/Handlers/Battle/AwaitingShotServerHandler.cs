
using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;
public sealed class AwaitingShotServerHandler : IServerHandler<AwaitingShotEvent>
{
    public AwaitingShotEvent Handle(AwaitingShotEvent networkEvent) => networkEvent;
}
