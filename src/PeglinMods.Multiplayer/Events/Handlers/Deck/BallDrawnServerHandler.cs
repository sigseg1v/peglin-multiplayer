namespace PeglinMods.Multiplayer.Events.Handlers.Deck;

using PeglinMods.Multiplayer.Events.Network.Deck;

public sealed class BallDrawnServerHandler : IServerHandler<BallDrawnEvent>
{
    public BallDrawnEvent Handle(BallDrawnEvent networkEvent) => networkEvent;
}
