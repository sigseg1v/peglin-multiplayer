namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class TurnCompleteServerHandler : IServerHandler<TurnCompleteEvent>
{
    public TurnCompleteEvent Handle(TurnCompleteEvent networkEvent) => networkEvent;
}
