namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class VictoryServerHandler : IServerHandler<VictoryEvent>
{
    public VictoryEvent Handle(VictoryEvent networkEvent) => networkEvent;
}
