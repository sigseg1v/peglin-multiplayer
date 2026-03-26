namespace PeglinMods.Spectator.Events;

public interface IServerHandler<TNetworkEvent> where TNetworkEvent : class
{
    TNetworkEvent Handle(TNetworkEvent networkEvent);
}
