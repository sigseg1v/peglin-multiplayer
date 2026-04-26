namespace Multipeglin.Events;

public interface IGameEventRegistry
{
    void Register<TNetworkEvent>(IServerHandler<TNetworkEvent> serverHandler, IClientHandler<TNetworkEvent> clientHandler) where TNetworkEvent : class;

    void Dispatch<TNetworkEvent>(TNetworkEvent networkEvent) where TNetworkEvent : class;

    void HandleIncoming(string typeId, string jsonPayload, int senderPeerId);

    /// <summary>The peer ID of the sender of the event currently being handled. Only valid inside a handler.</summary>
    int CurrentSenderPeerId { get; }
}
