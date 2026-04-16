using Multipeglin.Events.Network.Scenarios;

namespace Multipeglin.Events.Handlers.Scenarios;

/// <summary>
/// Server handler for ShopCompleteEvent (client -> host).
/// Suppresses rebroadcast — the host processes results directly.
/// </summary>
public sealed class ShopCompleteServerHandler : IServerHandler<ShopCompleteEvent>
{
    public ShopCompleteEvent Handle(ShopCompleteEvent networkEvent)
    {
        MultiplayerPlugin.Logger?.LogInfo("[ShopCompleteServer] Received — suppressing rebroadcast");
        return null;
    }
}
