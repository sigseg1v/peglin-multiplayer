namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

using PeglinMods.Multiplayer.Events.Network.Coop;

/// <summary>
/// Host broadcasts turn change events to all clients. Pass through.
/// </summary>
public sealed class TurnChangeServerHandler : IServerHandler<TurnChangeEvent>
{
    public TurnChangeEvent Handle(TurnChangeEvent networkEvent) => networkEvent;
}
