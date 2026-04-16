using BepInEx.Logging;
using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Server handler for RewardChoiceEvent (client -> host).
/// Suppresses rebroadcast; the host processes the choice directly.
/// </summary>
public sealed class RewardChoiceServerHandler : IServerHandler<RewardChoiceEvent>
{
    private static readonly ManualLogSource _log = Logger.CreateLogSource("RewardChoiceServer");

    public RewardChoiceEvent Handle(RewardChoiceEvent networkEvent)
    {
        _log.LogInfo($"[RewardChoiceServer] Received reward choice: optionIndex={networkEvent.ChosenOptionIndex} — suppressing rebroadcast");
        return null;
    }
}
