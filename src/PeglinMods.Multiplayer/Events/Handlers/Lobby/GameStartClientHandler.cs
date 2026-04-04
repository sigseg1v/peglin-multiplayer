namespace PeglinMods.Multiplayer.Events.Handlers.Lobby;

using System;
using System.Linq;
using PeglinMods.Multiplayer.Events.Network.Lobby;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.UI;

/// <summary>
/// Client receives GameStartEvent: set the local player's class in StaticGameData,
/// store the final player list, and prepare for game start.
/// </summary>
public sealed class GameStartClientHandler : IClientHandler<GameStartEvent>
{
    public void Handle(GameStartEvent networkEvent)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating) return;

            MultiplayerPlugin.Logger?.LogInfo($"[GameStart] Received game start with {networkEvent.FinalPlayers?.Count ?? 0} players");

            // Find this client's entry in the player list and set the chosen class
            if (networkEvent.FinalPlayers != null)
            {
                // Client is the non-host player — find our entry
                var myEntry = networkEvent.FinalPlayers.FirstOrDefault(p => !p.IsHost);
                if (myEntry != null)
                {
                    StaticGameData.chosenClass = (Peglin.ClassSystem.Class)myEntry.ChosenClass;
                    MultiplayerPlugin.Logger?.LogInfo($"[GameStart] Set client class to {myEntry.ChosenClassName} ({myEntry.ChosenClass})");
                }

                // Also set PlayerRegistry.LocalSlot if available
                var services = MultiplayerPlugin.Services;
                if (services?.TryResolve<PlayerRegistry>(out var registry) == true && myEntry != null)
                {
                    var slot = registry.GetSlotByIndex(myEntry.SlotIndex);
                    if (slot != null)
                        registry.LocalSlot = slot;
                }
            }

            // Store final player list for CoopStateManager and UI to use
            LobbyUI.OnGameStartReceived(networkEvent);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[GameStart] Handler failed: {e.Message}");
        }
    }
}
