using BepInEx.Logging;
using Rewired;

namespace PeglinMods.Spectator.Spectator;

/// <summary>
/// Disables player input via Rewired when in spectator mode.
/// Re-enables input when spectating ends.
/// </summary>
public class SpectatorInputSuppressor
{
    private readonly ManualLogSource _log;
    private bool _inputSuppressed;

    public SpectatorInputSuppressor(ManualLogSource log)
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
            _log.LogInfo("Spectator input suppressed via Rewired");
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
            _log.LogInfo("Spectator input restored via Rewired");
        }
    }
}
