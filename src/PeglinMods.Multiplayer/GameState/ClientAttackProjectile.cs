using System.Collections;
using PeglinMods.Multiplayer.Utility;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState;

/// <summary>
/// Creates a visual-only attack projectile on the client.
/// When the peglin throw animation reaches the fire point, this creates a sprite
/// that flies horizontally from the player to the target enemy — matching the
/// host's ShotBehavior visual (straight-line flight, size scales with peg count).
/// </summary>
public class ClientAttackProjectile : MonoBehaviour
{
    public static ClientAttackProjectile Instance { get; private set; }

    private string _targetEnemyGuid;
    private int _numPegsHit;
    private bool _isCrit;
    private string _orbName;
    private bool _waitingForFire;

    // ShotBehavior default values (from decomp — serialized fields)
    private const float MinForce = 100f;
    private const float MaxForce = 900f;
    private const float ForcePerPeg = 50f;
    private static readonly Vector3 MinSize = new Vector3(0.2f, 0.2f, 1f);
    private static readonly Vector3 MaxSize = new Vector3(2f, 2f, 1f);

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void SetupAttack(string targetEnemyGuid, int numPegsHit = 0, bool isCrit = false, string orbName = null)
    {
        _targetEnemyGuid = targetEnemyGuid;
        _numPegsHit = numPegsHit;
        _isCrit = isCrit;
        _orbName = orbName;
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
        // Create projectile game object
        var go = new GameObject("ClientProjectile");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 150;

        // Get sprite from orb prefab (same lookup as ClientBallRenderer)
        bool spriteFound = false;
        try
        {
            spriteFound = TrySetOrbSprite(sr, _orbName);
        }
        catch { }

        // Fallback: try to copy from the ClientBallRenderer's current ball
        if (!spriteFound)
        {
            try
            {
                spriteFound = TryCopyFromBallRenderer(sr);
            }
            catch { }
        }

        // Last resort: yellow circle
        if (!spriteFound)
        {
            sr.sprite = CreateCircleSprite();
            sr.color = Color.yellow;
        }

        // Scale based on peg count — matching ShotBehavior's formula:
        // force = Clamp(numPegsHit * forcePerPeg, minForce, maxForce)
        // scale = Lerp(minSize, maxSize, abs(force) / maxForce)
        float force = Mathf.Clamp(_numPegsHit * ForcePerPeg, MinForce, MaxForce);
        float sizeT = Mathf.Abs(force) / MaxForce;
        Vector3 projScale = Vector3.Lerp(MinSize, MaxSize, sizeT);
        go.transform.localScale = projScale;

        // Position at the player's attack origin
        go.transform.position = startPos;

        // Orient toward target (horizontal flight like ShotBehavior.ApplyFiringForce)
        Vector3 direction = (target.position - startPos).normalized;
        // ShotBehavior uses transform.right for flight direction
        if (direction.sqrMagnitude > 0.001f)
            go.transform.right = direction;

        // Fly toward target — straight line, matching host's horizontal projectile
        float elapsed = 0f;
        float distance = Vector3.Distance(startPos, target.position);
        // Duration scales with distance; use force as approximate speed
        // ShotBehavior uses physics (AddForce), so we approximate the visual travel time
        float speed = Mathf.Max(force * 0.02f, 5f); // rough conversion from force to visual speed
        float duration = Mathf.Clamp(distance / speed, 0.15f, 0.6f);
        Vector3 from = go.transform.position;

        while (elapsed < duration && target != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Straight line — no arc
            go.transform.position = Vector3.Lerp(from, target.position, t);
            yield return null;
        }

        // Hit effect — invoke OnFireComplete for damage timing
        Battle.Attacks.AttackManager.OnFireComplete?.Invoke();

        // Destroy projectile
        Destroy(go);
    }

    /// <summary>Look up orb prefab by name and copy its sprite + material.</summary>
    private static bool TrySetOrbSprite(SpriteRenderer sr, string orbName)
    {
        if (string.IsNullOrEmpty(orbName)) return false;
        string cleanName = orbName.Replace("(Clone)", "").Trim();

        GameObject orbGo = null;

        // Try AssetLoading cache first
        var loader = Loading.AssetLoading.Instance;
        if (loader != null)
            orbGo = loader.GetOrbPrefab(cleanName);

        // Fallback: search battleDeck
        if (orbGo == null)
        {
            var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
            var dm = dms.Length > 0 ? dms[0] : null;
            if (dm?.battleDeck != null)
            {
                foreach (var orb in dm.battleDeck)
                {
                    if (orb != null && orb.name.Replace("(Clone)", "").Trim() == cleanName)
                    {
                        orbGo = orb;
                        break;
                    }
                }
            }
        }

        if (orbGo == null) return false;

        var orbRenderer = orbGo.GetComponentInChildren<SpriteRenderer>();
        if (orbRenderer?.sprite == null) return false;

        sr.sprite = orbRenderer.sprite;
        if (orbRenderer.sharedMaterial != null)
            sr.material = orbRenderer.sharedMaterial;
        sr.sortingLayerID = orbRenderer.sortingLayerID;
        sr.sortingOrder = 150;
        return true;
    }

    /// <summary>Copy sprite + material from ClientBallRenderer's active ball.</summary>
    private static bool TryCopyFromBallRenderer(SpriteRenderer sr)
    {
        var cbr = ClientBallRenderer.Instance;
        if (cbr == null) return false;

        // Access _ballRenderer via reflection (same assembly, but private)
        var field = HarmonyLib.AccessTools.Field(typeof(ClientBallRenderer), "_ballRenderer");
        var ballSr = field?.GetValue(cbr) as SpriteRenderer;
        if (ballSr?.sprite == null) return false;

        sr.sprite = ballSr.sprite;
        if (ballSr.sharedMaterial != null)
            sr.material = ballSr.sharedMaterial;
        sr.sortingLayerID = ballSr.sortingLayerID;
        sr.sortingOrder = 150;
        return true;
    }

    /// <summary>Helper to create a circle sprite (fallback).</summary>
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
