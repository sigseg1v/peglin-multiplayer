using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class AnimationSyncServerHandler : IServerHandler<AnimationSyncEvent>
{
    public AnimationSyncEvent Handle(AnimationSyncEvent e) => e;
}
