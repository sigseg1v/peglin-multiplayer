namespace Multipeglin.Multiplayer;

public enum ClientMode
{
    Mirror,      // Full game rendering - client sees host's game
    Diagnostics // Text event feed - shows raw network events
}
