namespace PeglinMods.Spectator.Events.Handlers.Deck;

using PeglinMods.Spectator.Events.Network.Deck;

public sealed class BallUsedClientHandler : IClientHandler<BallUsedEvent>
{
    public void Handle(BallUsedEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Used {networkEvent.OrbName}");
    }
}
