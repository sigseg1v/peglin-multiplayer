namespace Multipeglin.Network;

public interface IMessageReceiver
{
    void ProcessIncoming(int senderPeerId, byte[] data);
}
