using System;
using System.Collections.Generic;

namespace PeglinMods.Multiplayer.Network.Protocol;

public class MessageTypeRegistry
{
    private readonly Dictionary<string, Type> _idToType = new Dictionary<string, Type>();
    private readonly Dictionary<Type, string> _typeToId = new Dictionary<Type, string>();

    public void Register<T>()
    {
        var type = typeof(T);
        var typeId = type.FullName;
        _idToType[typeId] = type;
        _typeToId[type] = typeId;
    }

    public string GetTypeId<T>()
    {
        return _typeToId[typeof(T)];
    }

    public string GetTypeId(Type type)
    {
        return _typeToId[type];
    }

    public Type GetType(string typeId)
    {
        return _idToType[typeId];
    }

    public bool TryGetType(string typeId, out Type type)
    {
        return _idToType.TryGetValue(typeId, out type);
    }
}
