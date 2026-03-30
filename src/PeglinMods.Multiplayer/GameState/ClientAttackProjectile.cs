using System.Collections;
using PeglinMods.Multiplayer.Utility;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState;

/// <summary>
/// Creates a visual-only attack projectile on the client.
/// When the peglin throw animation reaches the fire point, this creates a sprite
/// that flies from the player to the target enemy.
/// </summary>
public class ClientAttackProjectile : MonoBehaviour
{
    public static ClientAttackProjectile Instance { get; private set; }

    private string _targetEnemyGuid;
    private bool _waitingForFire;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void SetupAttack(string targetEnemyGuid)
    {
        _targetEnemyGuid = targetEnemyGuid;
        _waitingForFire = true;

        // Subscribe to OnFirePoint to know when to launch
        PeglinBattleAnimationController.OnFirePoint += OnFirePoint;
    }

    private void OnFirePoint()
    {
        if (!_waitingForFire) return;
        _waitingForFire = false;
        PeglinBattleAnimationController.OnFirePoint -= OnFirePoint;

        // Find target enemy
        if (string.IsNullOrEmpty(_targetEnemyGuid)) return;
        var enemyId = MultiplayerPlugin.Services?.TryResolve<EnemyIdentifier>(out var eid) == true ? eid : null;
        var enemy = enemyId?.Find(_targetEnemyGuid);
        if (enemy == null) return;

        // Find player position
        var bc = Object.FindObjectOfType<Battle.BattleController>();
        if (bc == null) return;
        var playerField = HarmonyLib.AccessTools.Field(typeof(Battle.BattleController), "_playerTransform");
        var playerTransform = playerField?.GetValue(bc) as Transform;
        if (playerTransform == null) return;

        // Launch projectile
        StartCoroutine(LaunchProjectile(playerTransform.position, enemy.transform));
    }

    private IEnumerator LaunchProjectile(Vector3 startPos, Transform target)
    {
        // Create simple projectile sprite
        var go = new GameObject("ClientProjectile");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 150;

        // Try to get sprite from current orb
        try
        {
            var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
            var dm = dms.Length > 0 ? dms[0] : null;
            if (dm?.battleDeck != null && dm.battleDeck.Count > 0)
            {
                var orbRenderer = dm.battleDeck[0]?.GetComponentInChildren<SpriteRenderer>();
                if (orbRenderer?.sprite != null)
                {
                    sr.sprite = orbRenderer.sprite;
                    go.transform.localScale = Vector3.one * 0.5f;
                }
            }
        }
        catch { }

        // Fallback circle sprite
        if (sr.sprite == null)
        {
            sr.sprite = CreateCircleSprite();
            sr.color = Color.yellow;
            go.transform.localScale = Vector3.one * 0.3f;
        }

        go.transform.position = startPos + Vector3.up * 0.5f;

        // Fly toward target over 0.3 seconds
        float elapsed = 0f;
        float duration = 0.3f;
        Vector3 from = go.transform.position;

        while (elapsed < duration && target != null)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            // Arc trajectory
            Vector3 mid = Vector3.Lerp(from, target.position, t);
            mid.y += Mathf.Sin(t * Mathf.PI) * 1.5f; // arc height
            go.transform.position = mid;
            yield return null;
        }

        // Hit effect — invoke OnFireComplete for damage timing
        Battle.Attacks.AttackManager.OnFireComplete?.Invoke();

        // Destroy projectile
        Destroy(go);
    }

    /// <summary>Helper to create a circle sprite (same as ClientBallRenderer).</summary>
    private static Sprite CreateCircleSprite()
    {
        int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = size / 2f;
        float radius = center - 1;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) <= radius ? Color.white : Color.clear);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
