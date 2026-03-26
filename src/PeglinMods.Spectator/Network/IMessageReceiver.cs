namespace PeglinMods.Spectator.Network;

public interface IMessageReceiver
{
    void ProcessIncoming(byte[] data);
}
