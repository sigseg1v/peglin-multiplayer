namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using System.Collections;
using global::Battle;
using global::Battle.Attacks;
using HarmonyLib;
using PeglinMods.Multiplayer.Events.Network.Battle;
using PeglinMods.Multiplayer.Utility;
using UnityEngine;

public sealed class BombThrownClientHandler : IClientHandler<BombThrownEvent>
{
    private static readonly System.Reflection.FieldInfo _bombPrefabField =
        AccessTools.Field(typeof(BattleController), "_tossableBombPrefab");
    private static readonly System.Reflection.FieldInfo _riggedBombPrefabField =
        AccessTools.Field(typeof(BattleController), "_tossableRiggedBombPrefab");
    private static readonly System.Reflection.FieldInfo _playerTransformField =
        AccessTools.Field(typeof(BattleController), "_playerTransform");
    private static readonly System.Reflection.FieldInfo _playerOffsetField =
        AccessTools.Field(typeof(BattleController), "_playerTransformOffset");

    public void Handle(BombThrownEvent networkEvent)
    {
        try
        {
            // Fire the delegate — triggers peglin lob animation via PeglinBattleAnimationController
            BattleController.OnBombThrown?.Invoke();

            int total = networkEvent.RegularCount + networkEvent.RiggedCount;
            if (total <= 0) return;

            // Use MainThreadDispatcher to start a coroutine for spawning visual bombs
            var dispatcher = MainThreadDispatcher.Instance;
            if (dispatcher != null)
            {
                dispatcher.StartCoroutine(SpawnBombsAfterLobAnimation(
                    networkEvent.RegularCount, networkEvent.RiggedCount));
            }
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"BombThrown handler failed: {e.Message}");
        }
    }

    private static IEnumerator SpawnBombsAfterLobAnimation(int regularCount, int riggedCount)
    {
        // Wait for the lob animation to reach the throw point
        bool lobPointReached = false;
        PeglinBattleAnimationController.LobPoint lobCallback = () => lobPointReached = true;
        PeglinBattleAnimationController.OnLobPoint += lobCallback;

        // Timeout after 1.5s in case animation event doesn't fire
        float timeout = 1.5f;
        float elapsed = 0f;
        while (!lobPointReached && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        PeglinBattleAnimationController.OnLobPoint -= lobCallback;

        // Find BattleController for prefabs and player position
        var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
        if (bc == null) yield break;

        var bombPrefab = _bombPrefabField?.GetValue(bc) as GameObject;
        var riggedPrefab = _riggedBombPrefabField?.GetValue(bc) as GameObject;
        var playerTransform = _playerTransformField?.GetValue(bc) as Transform;
        var playerOffset = _playerOffsetField != null ? (Vector3)_playerOffsetField.GetValue(bc) : Vector3.zero;

        if (playerTransform == null || bombPrefab == null) yield break;

        Vector3 spawnPos = playerTransform.position + playerOffset;
        var wait = new WaitForSeconds(0.02f);

        // Spawn rigged bombs first (matches host order)
        for (int i = 0; i < riggedCount; i++)
        {
            SpawnVisualBomb(riggedPrefab ?? bombPrefab, spawnPos, riggedCount + regularCount);
            yield return wait;
        }
        for (int i = 0; i < regularCount; i++)
        {
            SpawnVisualBomb(bombPrefab, spawnPos, riggedCount + regularCount);
            yield return wait;
        }
    }

    private static void SpawnVisualBomb(GameObject prefab, Vector3 spawnPos, int totalThrown)
    {
        try
        {
            var bombObj = UnityEngine.Object.Instantiate(prefab, spawnPos, Quaternion.identity);
            var bombLob = bombObj.GetComponent<BombLob>();
            if (bombLob != null)
            {
                bombLob.Shoot(inBoss: false, totalThrown);
            }
            // No OnBombTossComplete subscription — visual only, no damage on client
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"Failed to spawn visual bomb: {e.Message}");
        }
    }
}
