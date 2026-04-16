using BepInEx.Logging;
using Rewired;

namespace Multipeglin.Multiplayer;

/// <summary>
/// Disables player input via Rewired when in multiplayer mode.
/// Re-enables input when spectating ends.
/// </summary>
public class MultiplayerInputSuppressor
{
    private readonly ManualLogSource _log;
    private bool _inputSuppressed;

    public MultiplayerInputSuppressor(ManualLogSource log)
    {
        _log = log;
    }

    public void SuppressInput()
    {
        if (_inputSuppressed)
            return;

        if (!ReInput.isReady)
        {
            _log.LogWarning("Rewired not ready, cannot suppress input");
            return;
        }

        var player = ReInput.players.GetPlayer(0);
        if (player != null)
        {
            player.controllers.maps.SetAllMapsEnabled(false);
            _inputSuppressed = true;
            _log.LogInfo("Multiplayer input suppressed via Rewired");
        }
    }

    public void RestoreInput()
    {
        if (!_inputSuppressed)
            return;

        if (!ReInput.isReady)
            return;

        var player = ReInput.players.GetPlayer(0);
        if (player != null)
        {
            player.controllers.maps.SetAllMapsEnabled(true);
            _inputSuppressed = false;
            _log.LogInfo("Multiplayer input restored via Rewired");
        }
    }
}
