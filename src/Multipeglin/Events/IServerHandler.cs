namespace Multipeglin.Events;

public interface IServerHandler<TNetworkEvent> where TNetworkEvent : class
{
    TNetworkEvent Handle(TNetworkEvent networkEvent);
}
