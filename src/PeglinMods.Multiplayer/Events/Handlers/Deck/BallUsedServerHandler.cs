namespace PeglinMods.Multiplayer.Events.Handlers.Deck;

using PeglinMods.Multiplayer.Events.Network.Deck;

public sealed class BallUsedServerHandler : IServerHandler<BallUsedEvent>
{
    public BallUsedEvent Handle(BallUsedEvent networkEvent) => networkEvent;
}
