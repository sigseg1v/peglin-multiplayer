namespace PeglinMods.Multiplayer.Network;

public interface IMessageSender
{
    void Send<TNetworkEvent>(TNetworkEvent networkEvent) where TNetworkEvent : class;
}
