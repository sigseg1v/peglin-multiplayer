namespace Multipeglin.Events.Handlers.Lobby;

using System;
using System.Linq;
using Multipeglin.Events.Network.Lobby;
using Multipeglin.Multiplayer;
using Multipeglin.UI;

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
                    var chosenClass = (Peglin.ClassSystem.Class)myEntry.ChosenClass;
                    StaticGameData.chosenClass = chosenClass;
                    MultiplayerPlugin.Logger?.LogInfo($"[GameStart] Set client class to {myEntry.ChosenClassName} ({myEntry.ChosenClass})");

                    // Set StartingOrbs/StartingRelics from ClassLoadoutData so GameInit
                    // can initialize the deck properly when the client enters the game
                    Patches.MultiplayerClientPatches.SetStartingLoadoutFromClass(chosenClass);

                    // Mirror into CruciballManager.currentClass — PeglinClassAnimationSwitcher
                    // reads this at battle scene OnEnable to pick the base sprite / animator,
                    // and we skip the native class-select flow entirely in multiplayer.
                    Patches.MultiplayerClientPatches.SetCruciballManagerClass(chosenClass);

                    // Apply the host-chosen cruciball level so client-side systems that
                    // gate visuals/text on currentCruciballLevel render correctly.
                    Patches.MultiplayerClientPatches.SetCruciballManagerLevel(networkEvent.CruciballLevel);

                    // Populate the RelicManager's pools + _selectedClass for the client's
                    // chosen class. Normally LoadoutManager.SetupLoadout does this when the
                    // player confirms their character; since we skip that UI in multiplayer,
                    // the relic queue would otherwise stay on Peglin (the LoadoutManager.Awake
                    // default), which can leave the shop with zero relics for non-Peglin classes.
                    Patches.MultiplayerClientPatches.SetRelicManagerClass(chosenClass);
                }

                // Set PlayerRegistry.LocalSlot so other handlers can identify this client's slot.
                // GetSlotByIndex may return null on the client (the client doesn't register
                // itself), so create a PlayerSlot directly from the GameStartEvent data.
                var services = MultiplayerPlugin.Services;
                if (services?.TryResolve<PlayerRegistry>(out var registry) == true && myEntry != null)
                {
                    var slot = registry.GetSlotByIndex(myEntry.SlotIndex);
                    if (slot == null)
                    {
                        slot = new PlayerSlot
                        {
                            SlotIndex = myEntry.SlotIndex,
                            PeerId = -1,
                            PlayerName = myEntry.PlayerName,
                            IsHost = false,
                            ChosenClass = myEntry.ChosenClass,
                            IsReady = true,
                        };
                    }
                    registry.LocalSlot = slot;
                    MultiplayerPlugin.Logger?.LogInfo($"[GameStart] Set LocalSlot: index={slot.SlotIndex}, name={slot.PlayerName}");
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
