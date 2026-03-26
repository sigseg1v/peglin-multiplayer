namespace PeglinMods.Spectator.Events.Handlers.Battle;

using PeglinMods.Spectator.Events.Network.Battle;

public sealed class VictoryServerHandler : IServerHandler<VictoryEvent>
{
    public VictoryEvent Handle(VictoryEvent networkEvent) => networkEvent;
}
