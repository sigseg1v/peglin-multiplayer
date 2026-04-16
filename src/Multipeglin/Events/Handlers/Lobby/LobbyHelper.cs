using System.Linq;
using Multipeglin.Events.Network.Lobby;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers.Lobby;

public static class LobbyHelper
{
    private static readonly string[] ClassNames = { "Peglin", "Balladin", "Roundrel", "Spinventor" };

    public static string GetClassName(int classIndex)
    {
        if (classIndex >= 0 && classIndex < ClassNames.Length)
            return ClassNames[classIndex];
        return $"Unknown({classIndex})";
    }

    public static void BroadcastLobbyState(PlayerRegistry registry, IGameEventRegistry eventRegistry)
    {
        var lobbyState = new LobbyStateEvent
        {
            Players = registry.GetAllSlots().Select(s => new LobbyPlayerEntry
            {
                SlotIndex = s.SlotIndex,
                PlayerName = s.PlayerName,
                ChosenClass = s.ChosenClass,
                ChosenClassName = GetClassName(s.ChosenClass),
                IsReady = s.IsReady,
                IsHost = s.IsHost,
            }).ToList()
        };

        eventRegistry.Dispatch(lobbyState);
    }
}
