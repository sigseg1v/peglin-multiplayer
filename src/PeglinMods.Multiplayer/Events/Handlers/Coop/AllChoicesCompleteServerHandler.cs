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
        CoopRewardState.AllChoicesComplete = true;
        CoopRewardState.WaitingForOtherPlayers = false;

        if (networkEvent.Phase == "post_battle")
        {
            CoopRewardState.ClientInNativeRewardPhase = false;
            Patches.MultiplayerClientPatches.AllowNativeRewardLogic = false;
        }

        return networkEvent;
    }
}
