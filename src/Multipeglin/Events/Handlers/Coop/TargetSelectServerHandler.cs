
using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;
/// <summary>
/// TargetSelect is client → host only. Do NOT rebroadcast — the host
/// processes it locally to show targeting indicators.
/// </summary>
public sealed class TargetSelectServerHandler : IServerHandler<TargetSelectEvent>
{
    public TargetSelectEvent Handle(TargetSelectEvent networkEvent)
    {
        return null; // Suppress rebroadcast
    }
}
