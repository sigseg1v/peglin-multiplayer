namespace PeglinMods.Spectator.Spectator;

public interface ISpectatorMode
{
    bool IsSpectating { get; }
    bool IsHosting { get; }
    void EnableHosting();
    void EnableSpectating();
    void Disable();
}
