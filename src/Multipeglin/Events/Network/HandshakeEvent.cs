namespace Multipeglin.Events.Network;

public sealed class HandshakeEvent
{
    public string PlayerName { get; set; }
    public string ModVersion { get; set; }
    public string CompiledGameVersion { get; set; }
    public string RuntimeGameVersion { get; set; }
    public bool IsHost { get; set; }
    public int RegisteredHandlerCount { get; set; }
}
