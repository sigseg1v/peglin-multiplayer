namespace PeglinMods.Spectator.Events.Handlers.Battle;

using PeglinMods.Spectator.Events.Network.Battle;

public sealed class TurnCompleteServerHandler : IServerHandler<TurnCompleteEvent>
{
    public TurnCompleteEvent Handle(TurnCompleteEvent networkEvent) => networkEvent;
}
