namespace PeglinMods.Multiplayer.Network;

public interface IMessageReceiver
{
    void ProcessIncoming(int senderPeerId, byte[] data);
}
