namespace PeglinMods.Spectator.Events.Handlers.Battle;

using PeglinMods.Spectator.Events.Network.Battle;

public sealed class BombThrownServerHandler : IServerHandler<BombThrownEvent>
{
    public BombThrownEvent Handle(BombThrownEvent networkEvent) => networkEvent;
}
