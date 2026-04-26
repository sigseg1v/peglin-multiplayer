namespace Multipeglin.DI;

public interface IServiceContainer
{
    void RegisterSingleton<TInterface>(TInterface instance);

    void RegisterSingleton<TInterface, TImplementation>() where TImplementation : class, TInterface, new();

    T Resolve<T>();

    bool TryResolve<T>(out T instance);
}
