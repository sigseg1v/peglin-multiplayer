namespace Multipeglin.Network;

public interface IMessageSender
{
    void Send<TNetworkEvent>(TNetworkEvent networkEvent) where TNetworkEvent : class;

    void SendTo<TNetworkEvent>(int peerId, TNetworkEvent networkEvent) where TNetworkEvent : class;
}
