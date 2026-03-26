using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace PeglinMods.Spectator.Utility;

public class MainThreadDispatcher : MonoBehaviour
{
    private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

    public void Enqueue(Action action)
    {
        _queue.Enqueue(action);
    }

    private void Update()
    {
        while (_queue.TryDequeue(out var action))
        {
            action.Invoke();
        }
    }
}
