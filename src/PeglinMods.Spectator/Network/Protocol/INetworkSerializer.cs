namespace PeglinMods.Spectator.Network.Protocol;

public interface INetworkSerializer
{
    byte[] Serialize<TNetworkEvent>(TNetworkEvent networkEvent) where TNetworkEvent : class;
    (string typeId, string jsonPayload) Deserialize(byte[] data);
}
