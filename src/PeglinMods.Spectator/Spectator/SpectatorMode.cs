namespace PeglinMods.Spectator.Spectator;

public class SpectatorMode : ISpectatorMode
{
    public bool IsSpectating { get; private set; }
    public bool IsHosting { get; private set; }

    public void EnableHosting()
    {
        IsHosting = true;
        IsSpectating = false;
    }

    public void EnableSpectating()
    {
        IsSpectating = true;
        IsHosting = false;
    }

    public void Disable()
    {
        IsHosting = false;
        IsSpectating = false;
    }
}
