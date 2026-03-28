namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class DefeatServerHandler : IServerHandler<DefeatEvent>
{
    public DefeatEvent Handle(DefeatEvent networkEvent) => networkEvent;
}
