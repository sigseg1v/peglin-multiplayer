namespace Multipeglin.Events.Handlers.Battle;

using Multipeglin.Events.Network.Battle;

public sealed class ShotCompleteServerHandler : IServerHandler<ShotCompleteEvent>
{
    public ShotCompleteEvent Handle(ShotCompleteEvent networkEvent) => networkEvent;
}
