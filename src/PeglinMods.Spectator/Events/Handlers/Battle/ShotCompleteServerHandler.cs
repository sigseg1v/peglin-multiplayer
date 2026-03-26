namespace PeglinMods.Spectator.Events.Handlers.Battle;

using PeglinMods.Spectator.Events.Network.Battle;

public sealed class ShotCompleteServerHandler : IServerHandler<ShotCompleteEvent>
{
    public ShotCompleteEvent Handle(ShotCompleteEvent networkEvent) => networkEvent;
}
