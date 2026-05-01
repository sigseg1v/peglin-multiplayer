using HarmonyLib;
using Multipeglin.GameState;
using RNG.Scenarios;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class DialogueSystemScenarioPatches
{
    /// <summary>
    /// Allow DisableFadeCurtain on client so the black curtain fades out and
    /// the TextScenario scene (background, doodads) is visible. The native
    /// Dialogue System UI will render on the client with correct fonts/layout.
    /// </summary>
    [HarmonyPatch(typeof(DialogueSystemScenario), "DisableFadeCurtain")]
    [HarmonyPrefix]
    public static bool DialogueSystemScenario_DisableFadeCurtain_Prefix()
    {
        // Always allow — the curtain must fade out on the client too.
        // StartConversation (called inside DisableFadeCurtain) is also allowed
        // so the native dialogue UI renders. Response clicks are blocked separately.
        if (ShouldSuppressClientLogic)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] ALLOWING DisableFadeCurtain on client (native dialogue UI)");
        }

        return true;
    }

    /// <summary>
    /// DialogueSystemScenario.ConversationEnded —
    /// Client: allow when AllowTextScenarioLogic (let native dialogue flow complete, then capture state).
    /// Host: in coop, do wait-for-all before allowing StartNavigation.
    /// </summary>
    [HarmonyPatch(typeof(DialogueSystemScenario), "ConversationEnded")]
    [HarmonyPrefix]
    public static bool DialogueSystemScenario_ConversationEnded_Prefix(DialogueSystemScenario __instance)
    {
        if (!UI.LobbyUI.GameStartReceived)
        {
            return true; // Not in coop
        }

        if (ShouldSuppressClientLogic)
        {
            if (AllowTextScenarioLogic)
            {
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] ALLOWING DialogueSystemScenario.ConversationEnded (AllowTextScenarioLogic)");
                // Don't call StartNavigation on client — just capture state and send to host.
                // We return false and handle the post-dialogue logic ourselves.
                CaptureAndSendTextScenarioState();
                return false;
            }

            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked DialogueSystemScenario.ConversationEnded (spectating)");
            return false;
        }

        if (IsHosting && Events.Handlers.Coop.CoopRewardState.TextScenarioPhaseActive)
        {
            // HOST: save own state first
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<GameState.CoopStateManager>(out var coopState) == true)
            {
                coopState.SaveActivePlayerState();
            }

            Events.Handlers.Coop.CoopRewardState.HostTextScenarioDone = true;

            if (Events.Handlers.Coop.CoopRewardState.AllClientTextScenarioChoicesReceived)
            {
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Host ConversationEnded — all clients done, proceeding");
                Events.Handlers.Coop.CoopRewardState.TextScenarioPhaseActive = false;
                Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = false;
                if (services?.TryResolve<Events.IGameEventRegistry>(out var reg) == true)
                {
                    reg.Dispatch(new Events.Network.Coop.AllChoicesCompleteEvent { Phase = "text_scenario" });
                }

                return true; // Let ConversationEnded run normally (calls StartNavigation)
            }
            else
            {
                // Not all clients done — store reference and block
                Events.Handlers.Coop.CoopRewardState.PendingDialogueSystemScenario = __instance;
                Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Host ConversationEnded — waiting for other players to finish TextScenario");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Allow OnContinue on client — advancing through text pages is harmless
    /// (doesn't affect game state). Only response clicks are blocked.
    /// The client can read through dialogue at their own pace.
    /// </summary>

    /// <summary>
    /// Force the TextScenario peg layout to load on the client.
    ///
    /// Native ShouldSkipNavigation returns true when StaticGameData.currentNode
    /// is null — and currentNode is host-only (the live MapNode tree never
    /// exists on clients). That gates the call to PegLayoutLoader.TryLoadPegLayout
    /// inside DialogueSystemScenario.LoadData, so the client's TextScenario
    /// scene comes up with the dialogue UI and slot triggers but no pegs for
    /// the nav ball to bounce on. Override here so the layout loads.
    /// </summary>
    [HarmonyPatch(typeof(DialogueSystemScenario), "ShouldSkipNavigation")]
    [HarmonyPrefix]
    public static bool DialogueSystemScenario_ShouldSkipNavigation_Prefix(ref bool __result)
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        __result = false;
        return false;
    }

    /// <summary>
    /// Track navigation state on host; block navigation on client unless we
    /// explicitly enable it for spectating the navigation shot.
    /// </summary>
    [HarmonyPatch(typeof(DialogueSystemScenario), "StartNavigation")]
    [HarmonyPrefix]
    public static bool DialogueSystemScenario_StartNavigation_Prefix()
    {
        // On host: track that navigation has started
        if (IsHosting)
        {
            TextScenarioHoverTracker.IsNavigating = true;
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Host TextScenario navigation started");
            return true;
        }

        // On client: block unless navigation is already enabled by heartbeat
        if (ShouldSuppressClientLogic && !AllowTextScenarioNavigation)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked DialogueSystemScenario.StartNavigation (spectating)");
            return false;
        }

        return true;
    }
}
