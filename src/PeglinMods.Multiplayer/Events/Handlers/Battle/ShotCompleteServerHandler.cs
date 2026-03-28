namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class ShotCompleteServerHandler : IServerHandler<ShotCompleteEvent>
{
    public ShotCompleteEvent Handle(ShotCompleteEvent networkEvent) => networkEvent;
}
