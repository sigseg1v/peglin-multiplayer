namespace PeglinMods.Spectator.Events.Handlers.Deck;

using PeglinMods.Spectator.Events.Network.Deck;

public sealed class BallDrawnServerHandler : IServerHandler<BallDrawnEvent>
{
    public BallDrawnEvent Handle(BallDrawnEvent networkEvent) => networkEvent;
}
