// commented out for performance: see class body below.
// using System;
// using Multipeglin.Events.Network.Relic;

namespace Multipeglin.Events.Handlers.Relic;

// commented out for performance: was logging one line per relic activation
// during shots; per-peg-hit relics drove ~5000 sync Logger.LogInfo calls
// per big shot on the Unity main thread, tanking client framerate.
/*
public sealed class RelicUsedClientHandler : IClientHandler<RelicUsedEvent>
{
    public void Handle(RelicUsedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Relic used (effect {networkEvent.RelicEffect})");
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"RelicUsed handler failed: {e.Message}");
        }
    }
}
*/
