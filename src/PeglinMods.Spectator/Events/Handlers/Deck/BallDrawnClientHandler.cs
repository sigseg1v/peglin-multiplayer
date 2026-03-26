namespace PeglinMods.Spectator.Events.Handlers.Deck;

using PeglinMods.Spectator.Events.Network.Deck;

public sealed class BallDrawnClientHandler : IClientHandler<BallDrawnEvent>
{
    public void Handle(BallDrawnEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Drew {networkEvent.OrbName} (level {networkEvent.Level})");
    }
}
