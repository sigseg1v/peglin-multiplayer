using UnityEngine;

namespace PeglinMods.Multiplayer.Network;

public class NetworkPollBehaviour : MonoBehaviour
{
    private INetworkTransport _transport;

    public void Initialize(INetworkTransport transport)
    {
        _transport = transport;
    }

    private void Update()
    {
        _transport?.PollEvents();
    }
}
