using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Multipeglin.Network;
using Multipeglin.Network.Protocol;
using Newtonsoft.Json;

namespace Multipeglin.Events;

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

    /// <summary>The peer ID of the sender of the event currently being handled.</summary>
    public int CurrentSenderPeerId { get; private set; } = -1;

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
            if (result == null)
            {
                return;
            }

            var data = _serializer.Serialize(result);
            _transport.Broadcast(data);
        };

        _clientDispatchers[typeId] = jsonPayload =>
        {
            if (string.IsNullOrEmpty(jsonPayload))
            {
                _log.LogWarning($"Received empty payload for {typeId}, skipping");
                return;
            }

            var networkEvent = JsonConvert.DeserializeObject<TNetworkEvent>(jsonPayload);
            if (networkEvent == null)
            {
                _log.LogWarning($"Failed to deserialize {typeId} payload, skipping");
                return;
            }

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

    public void HandleIncoming(string typeId, string jsonPayload, int senderPeerId)
    {
        // Feed all incoming events to the multiplayer UI
        UI.EventFeed.Add(typeId, jsonPayload ?? "");

        // Set sender context for handlers to access
        var previousSender = CurrentSenderPeerId;
        CurrentSenderPeerId = senderPeerId;

        try
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
                    _log.LogWarning($"UNHANDLED EVENT from remote: '{typeId}' (no client handler registered)");
                    _log.LogWarning($"  This may be from a newer mod version. Payload preview: {Truncate(jsonPayload, 200)}");
                }
                else
                {
                    _log.LogDebug($"Unhandled event (repeated): '{typeId}'");
                }
            }
        }
        finally
        {
            CurrentSenderPeerId = previousSender;
        }
    }

    private static string Truncate(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "(empty)";
        }

        return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
    }
}
