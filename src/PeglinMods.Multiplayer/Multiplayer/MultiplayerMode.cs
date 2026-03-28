namespace PeglinMods.Multiplayer.Multiplayer;

public class MultiplayerMode : IMultiplayerMode
{
    public bool IsSpectating { get; private set; }
    public bool IsHosting { get; private set; }
    public ClientMode ClientMode { get; set; } = ClientMode.Mirror;

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
        ClientMode = ClientMode.Mirror;
    }
}
