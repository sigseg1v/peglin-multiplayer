using BepInEx.Logging;
using PeglinMods.Multiplayer.Events.Network.Coop;

namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

/// <summary>
/// Server handler for AllChoicesCompleteEvent (host -> all clients).
/// Passes through for broadcast.
/// </summary>
public sealed class AllChoicesCompleteServerHandler : IServerHandler<AllChoicesCompleteEvent>
{
    private static readonly ManualLogSource _log = Logger.CreateLogSource("AllChoicesCompleteServer");

    public AllChoicesCompleteEvent Handle(AllChoicesCompleteEvent networkEvent)
    {
        _log.LogInfo($"[AllChoicesCompleteServer] Broadcasting all choices complete: phase={networkEvent.Phase}");

        // Clear waiting state on the host — the ClientHandler does this on clients,
        // but only the ServerHandler runs on the host during Dispatch.
        CoopRewardState.WaitingForOtherPlayers = false;

        // For the shop phase, the host is about to do the post-shop navigation shot
        // — must NOT set AllChoicesComplete=true or the CoopRewardUI will keep
        // dismissing the overlay on the host too (fine) but more importantly the
        // host's shop-phase-active state gets reset by the overlay dismiss. Just
        // clear WaitingForOtherPlayers and let CloseStore proceed normally.
        if (networkEvent.Phase != "shop")
        {
            CoopRewardState.AllChoicesComplete = true;
        }

        if (networkEvent.Phase == "post_battle")
        {
            CoopRewardState.ClientInNativeRewardPhase = false;
            Patches.MultiplayerClientPatches.AllowNativeRewardLogic = false;
        }

        return networkEvent;
    }
}
