using System;
using System.Collections.Generic;
using System.Text.Json;
using BepInEx.Logging;
using PeglinMods.Spectator.Network;
using PeglinMods.Spectator.Network.Protocol;

namespace PeglinMods.Spectator.Events;

public class GameEventRegistry : IGameEventRegistry
{
    private readonly INetworkSerializer _serializer;
    private readonly INetworkTransport _transport;
    private readonly MessageTypeRegistry _typeRegistry;
    private readonly ManualLogSource _log;

    private readonly Dictionary<Type, Action<object>> _serverDispatchers = new Dictionary<Type, Action<object>>();
    private readonly Dictionary<string, Action<string>> _clientDispatchers = new Dictionary<string, Action<string>>();

    public GameEventRegistry(
        INetworkSerializer serializer,
        INetworkTransport transport,
        MessageTypeRegistry typeRegistry,
        ManualLogSource log)
    {
        _serializer = serializer;
        _transport = transport;
        _typeRegistry = typeRegistry;
        _log = log;
    }

    public void Register<TNetworkEvent>(IServerHandler<TNetworkEvent> serverHandler, IClientHandler<TNetworkEvent> clientHandler) where TNetworkEvent : class
    {
        _typeRegistry.Register<TNetworkEvent>();
        var typeId = _typeRegistry.GetTypeId<TNetworkEvent>();

        _serverDispatchers[typeof(TNetworkEvent)] = obj =>
        {
            var networkEvent = (TNetworkEvent)obj;
            var result = serverHandler.Handle(networkEvent);
            var data = _serializer.Serialize(result);
            _transport.Broadcast(data);
        };

        _clientDispatchers[typeId] = jsonPayload =>
        {
            var networkEvent = JsonSerializer.Deserialize<TNetworkEvent>(jsonPayload);
            clientHandler.Handle(networkEvent);
        };
    }

    public void Dispatch<TNetworkEvent>(TNetworkEvent networkEvent) where TNetworkEvent : class
    {
        var type = typeof(TNetworkEvent);
        if (_serverDispatchers.TryGetValue(type, out var dispatcher))
        {
            try
            {
                dispatcher(networkEvent);
            }
            catch (Exception ex)
            {
                _log.LogError($"Error dispatching {type.Name}: {ex}");
            }
        }
        else
        {
            _log.LogWarning($"No server dispatcher registered for {type.Name}");
        }
    }

    public void HandleIncoming(string typeId, string jsonPayload)
    {
        if (_clientDispatchers.TryGetValue(typeId, out var dispatcher))
        {
            try
            {
                dispatcher(jsonPayload);
            }
            catch (Exception ex)
            {
                _log.LogError($"Error handling incoming {typeId}: {ex}");
            }
        }
        else
        {
            _log.LogWarning($"No client dispatcher registered for typeId '{typeId}'");
        }
    }
}
