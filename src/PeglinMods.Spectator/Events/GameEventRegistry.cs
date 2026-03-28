using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
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

    /// <summary>All registered type IDs for introspection.</summary>
    public IReadOnlyCollection<string> RegisteredTypeIds => _clientDispatchers.Keys.ToList();

    /// <summary>Type IDs received from remote that we had no handler for.</summary>
    public IReadOnlyCollection<string> UnhandledTypeIds => _unhandledTypeIds;
    private readonly HashSet<string> _unhandledTypeIds = new HashSet<string>();

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
            if (result == null) return;
            var data = _serializer.Serialize(result);
            _transport.Broadcast(data);
        };

        _clientDispatchers[typeId] = jsonPayload =>
        {
            var networkEvent = JsonConvert.DeserializeObject<TNetworkEvent>(jsonPayload);
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
            // Unknown event - log it but don't crash
            if (_unhandledTypeIds.Add(typeId))
            {
                // First time seeing this typeId - log a warning
                _log.LogWarning($"UNHANDLED EVENT from remote: '{typeId}' (no client handler registered)");
                _log.LogWarning($"  This may be from a newer mod version. Payload preview: {Truncate(jsonPayload, 200)}");
            }
            else
            {
                _log.LogDebug($"Unhandled event (repeated): '{typeId}'");
            }
        }
    }

    private static string Truncate(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s)) return "(empty)";
        return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
    }
}
