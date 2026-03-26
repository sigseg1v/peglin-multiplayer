namespace PeglinMods.Spectator.Events;

public interface IClientHandler<in TNetworkEvent> where TNetworkEvent : class
{
    void Handle(TNetworkEvent networkEvent);
}
