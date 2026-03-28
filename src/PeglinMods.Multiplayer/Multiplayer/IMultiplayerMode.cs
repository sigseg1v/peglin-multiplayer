namespace PeglinMods.Multiplayer.Multiplayer;

public interface IMultiplayerMode
{
    bool IsSpectating { get; }
    bool IsHosting { get; }
    void EnableHosting();
    void EnableSpectating();
    void Disable();
}
