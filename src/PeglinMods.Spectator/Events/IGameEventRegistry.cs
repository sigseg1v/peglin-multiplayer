namespace PeglinMods.Spectator.Events;

public interface IGameEventRegistry
{
    void Register<TNetworkEvent>(IServerHandler<TNetworkEvent> serverHandler, IClientHandler<TNetworkEvent> clientHandler) where TNetworkEvent : class;
    void Dispatch<TNetworkEvent>(TNetworkEvent networkEvent) where TNetworkEvent : class;
    void HandleIncoming(string typeId, string jsonPayload);
}
