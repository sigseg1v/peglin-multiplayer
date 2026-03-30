using PeglinMods.Multiplayer.Events.Network.Battle;

namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

public sealed class AnimationSyncServerHandler : IServerHandler<AnimationSyncEvent>
{
    public AnimationSyncEvent Handle(AnimationSyncEvent e) => e;
}
