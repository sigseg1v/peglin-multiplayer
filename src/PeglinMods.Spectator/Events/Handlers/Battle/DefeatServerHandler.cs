namespace PeglinMods.Spectator.Events.Handlers.Battle;

using PeglinMods.Spectator.Events.Network.Battle;

public sealed class DefeatServerHandler : IServerHandler<DefeatEvent>
{
    public DefeatEvent Handle(DefeatEvent networkEvent) => networkEvent;
}
