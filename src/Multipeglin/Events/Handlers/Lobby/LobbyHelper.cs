using System;
using System.Collections.Generic;
using System.Linq;
using Multipeglin.Events.Network.Lobby;
using Multipeglin.Multiplayer;
using Multipeglin.UI;

namespace Multipeglin.Events.Handlers.Lobby;

public static class LobbyHelper
{
    private static readonly string[] ClassNames = { "Peglin", "Balladin", "Roundrel", "Spinventor" };

    public static string GetClassName(int classIndex)
    {
        if (classIndex >= 0 && classIndex < ClassNames.Length)
        {
            return ClassNames[classIndex];
        }

        return $"Unknown({classIndex})";
    }

    public static void BroadcastLobbyState(PlayerRegistry registry, IGameEventRegistry eventRegistry)
    {
        var present = registry.GetAllSlots().Select(s => new LobbyPlayerEntry
        {
            SlotIndex = s.SlotIndex,
            PlayerName = s.PlayerName,
            ChosenClass = s.ChosenClass,
            ChosenClassName = GetClassName(s.ChosenClass),
            IsReady = s.IsReady,
            IsHost = s.IsHost,
            GameVersion = s.GameVersion,
            ModVersion = s.ModVersion,
        }).ToList();

        var lobbyState = new LobbyStateEvent
        {
            Players = BuildOrderedRoster(present),
            CruciballLevel = LobbyUI.HostCruciballLevel,
            IsContinue = Continue.ContinueSession.IsActive,
        };

        eventRegistry.Dispatch(lobbyState);
    }

    /// <summary>
    /// In a continue lobby, reorder the roster to match the saved slot order
    /// and pad with MISSING placeholders for absent saved players. This runs
    /// on the host so the network broadcast already carries the correct order
    /// — clients then render the list as-is (no client-side knowledge of the
    /// saved roster needed). For non-continue lobbies, returns the input as-is
    /// (join order).
    /// </summary>
    public static List<LobbyPlayerEntry> BuildOrderedRoster(List<LobbyPlayerEntry> present)
    {
        if (!Continue.ContinueSession.IsActive
            || Continue.ContinueSession.ActiveSave?.Players == null)
        {
            return present;
        }

        var presentByName = new Dictionary<string, LobbyPlayerEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in present)
        {
            var key = Continue.ContinueFiles.Sanitize(p.PlayerName ?? string.Empty);
            if (!presentByName.ContainsKey(key))
            {
                presentByName[key] = p;
            }
        }

        var ordered = new List<LobbyPlayerEntry>();
        foreach (var saved in Continue.ContinueSession.ActiveSave.Players.OrderBy(p => p.SlotIndex))
        {
            var key = Continue.ContinueFiles.Sanitize(saved.PlayerName ?? string.Empty);
            if (presentByName.TryGetValue(key, out var match))
            {
                match.IsMissing = false;
                ordered.Add(match);
            }
            else
            {
                ordered.Add(new LobbyPlayerEntry
                {
                    SlotIndex = saved.SlotIndex,
                    PlayerName = saved.PlayerName,
                    ChosenClass = saved.ChosenClass,
                    ChosenClassName = GetClassName(saved.ChosenClass),
                    IsReady = false,
                    IsHost = saved.SlotIndex == 0,
                    GameVersion = string.Empty,
                    ModVersion = string.Empty,
                    IsMissing = true,
                });
            }
        }

        return ordered;
    }
}
