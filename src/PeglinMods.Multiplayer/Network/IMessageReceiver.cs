namespace PeglinMods.Multiplayer.Network;

public interface IMessageReceiver
{
    void ProcessIncoming(byte[] data);
}
