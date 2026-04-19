using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

public sealed class PostBattleGoldSpentServerHandler : IServerHandler<PostBattleGoldSpentEvent>
{
    public PostBattleGoldSpentEvent Handle(PostBattleGoldSpentEvent networkEvent) => null;
}
