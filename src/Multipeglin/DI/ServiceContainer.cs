using System;
using System.Collections.Generic;

namespace Multipeglin.DI;

public class ServiceContainer : IServiceContainer
{
    private readonly Dictionary<Type, object> _singletons = new Dictionary<Type, object>();
    private readonly Dictionary<Type, Func<object>> _factories = new Dictionary<Type, Func<object>>();

    public void RegisterSingleton<TInterface>(TInterface instance)
    {
        _singletons[typeof(TInterface)] = instance;
    }

    public void RegisterSingleton<TInterface, TImplementation>() where TImplementation : class, TInterface, new()
    {
        _factories[typeof(TInterface)] = () => new TImplementation();
    }

    public T Resolve<T>()
    {
        var type = typeof(T);

        if (_singletons.TryGetValue(type, out var instance))
            return (T)instance;

        if (_factories.TryGetValue(type, out var factory))
        {
            var created = (T)factory();
            _singletons[type] = created;
            _factories.Remove(type);
            return created;
        }

        throw new InvalidOperationException($"No registration found for type {type.FullName}");
    }

    public bool TryResolve<T>(out T instance)
    {
        try
        {
            instance = Resolve<T>();
            return true;
        }
        catch (InvalidOperationException)
        {
            instance = default;
            return false;
        }
    }
}
