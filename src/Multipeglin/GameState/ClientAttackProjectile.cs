using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Multipeglin.Utility;
using UnityEngine;

namespace Multipeglin.GameState;

/// <summary>
/// Visual-only attack projectile shown on host and spectating clients.
/// Mirrors the host's ShotBehavior visual: flies horizontally from the player's
/// arm to the target enemy, sized by peg count using the orb's actual shot-prefab
/// min/max size values. Sprite, scale, and startup offset are read from the orb's
/// ProjectileAttack._shotPrefab (or _criticalShotPrefab for crits) so each orb
/// keeps its own look.
/// </summary>
public class ClientAttackProjectile : MonoBehaviour
{
    public static ClientAttackProjectile Instance { get; private set; }

    private string _targetEnemyGuid;
    private int _numPegsHit;
    private bool _isCrit;
    private string _orbName;
    private bool _waitingForFire;

    /// <summary>
    /// True from SetupAttack until the visual projectile finishes its flight (or the watchdog
    /// times out). Host/client attack sequencers poll this to know when one shot is done so
    /// the next can begin.
    /// </summary>
    public bool IsAttacking { get; private set; }

    // Default values used when an orb has no ProjectileAttack / shot prefab (healing, AoE, etc).
    private const float DefaultMinForce = 100f;
    private const float DefaultMaxForce = 900f;
    private const float DefaultForcePerPeg = 50f;
    private static readonly Vector3 DefaultMinSize = new Vector3(0.2f, 0.2f, 1f);
    private static readonly Vector3 DefaultMaxSize = new Vector3(0.6f, 0.6f, 1f);
    private static readonly Vector3 DefaultStartupOffset = new Vector3(0f, 1.2f, 0f);

    private struct ShotVisual
    {
        public Sprite Sprite;
        public Material Material;
        public int SortingLayerId;
        public int SortingOrder;
        public bool FlipX;
        public Vector3 MinSize;
        public Vector3 MaxSize;
        public Vector3 StartupOffset;
        public float MinForce;
        public float MaxForce;
        public float ForcePerPeg;
    }

    // Per-orb cache so we only reflect once. Null entry means "resolved, nothing useful".
    private static readonly Dictionary<string, ShotVisual?> _shotCache = new Dictionary<string, ShotVisual?>();

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
        IsAttacking = true;

        PeglinBattleAnimationController.OnFirePoint += OnFirePoint;
        StartCoroutine(WatchdogTimeout());
    }

    private IEnumerator WatchdogTimeout()
    {
        float waited = 0f;
        while (_waitingForFire && waited < 1.5f)
        {
            waited += Time.deltaTime;
            yield return null;
        }
        if (_waitingForFire)
        {
            _waitingForFire = false;
            PeglinBattleAnimationController.OnFirePoint -= OnFirePoint;
            IsAttacking = false;
        }
    }

    private void OnFirePoint()
    {
        if (!_waitingForFire) return;
        _waitingForFire = false;
        PeglinBattleAnimationController.OnFirePoint -= OnFirePoint;

        if (string.IsNullOrEmpty(_targetEnemyGuid)) { IsAttacking = false; return; }
        var enemyId = MultiplayerPlugin.Services?.TryResolve<EnemyIdentifier>(out var eid) == true ? eid : null;
        var enemy = enemyId?.Find(_targetEnemyGuid);
        if (enemy == null) { IsAttacking = false; return; }

        var bc = Object.FindObjectOfType<Battle.BattleController>();
        if (bc == null) { IsAttacking = false; return; }
        var playerField = AccessTools.Field(typeof(Battle.BattleController), "_playerTransform");
        var playerTransform = playerField?.GetValue(bc) as Transform;
        if (playerTransform == null) { IsAttacking = false; return; }

        StartCoroutine(LaunchProjectile(playerTransform.position, enemy));
    }

    private IEnumerator LaunchProjectile(Vector3 playerGroundPos, Battle.Enemies.Enemy targetEnemy)
    {
        // Resolve the shot visual for this orb (sprite, size, offset). Fall back to
        // copying from the orb prefab sprite / ball renderer / yellow circle when the
        // orb has no ProjectileAttack (healing, AoE, etc).
        var visOpt = TryGetShotVisual(_orbName, _isCrit);

        var go = new GameObject("ClientProjectile");
        var sr = go.AddComponent<SpriteRenderer>();

        Vector3 minSize = DefaultMinSize;
        Vector3 maxSize = DefaultMaxSize;
        Vector3 startupOffset = DefaultStartupOffset;
        float minForce = DefaultMinForce;
        float maxForce = DefaultMaxForce;
        float forcePerPeg = DefaultForcePerPeg;

        if (visOpt.HasValue)
        {
            var vis = visOpt.Value;
            sr.sprite = vis.Sprite;
            if (vis.Material != null) sr.material = vis.Material;
            sr.sortingLayerID = vis.SortingLayerId;
            sr.sortingOrder = vis.SortingOrder;
            sr.flipX = vis.FlipX;

            minSize = vis.MinSize;
            maxSize = vis.MaxSize;
            minForce = vis.MinForce;
            maxForce = vis.MaxForce;
            forcePerPeg = vis.ForcePerPeg;
            if (vis.StartupOffset.sqrMagnitude > 0.0001f)
                startupOffset = vis.StartupOffset;
        }
        else
        {
            bool ok = TryCopyOrbSprite(sr, _orbName) || TryCopyFromBallRenderer(sr);
            if (!ok)
            {
                sr.sprite = CreateCircleSprite();
                sr.color = Color.yellow;
            }
            sr.sortingOrder = 150;
        }

        // ShotBehavior.Fire formula:
        //   force = Clamp(numPegsHit * forcePerPeg, minForce, maxForce)
        //   scale = Lerp(minSize, maxSize, |force| / maxForce)
        float force = Mathf.Clamp(_numPegsHit * forcePerPeg, minForce, maxForce);
        float t = maxForce > 0f ? Mathf.Abs(force) / maxForce : 0f;
        go.transform.localScale = Vector3.Lerp(minSize, maxSize, t);

        // Starting Y is Peglin's ground position + the shot prefab's startup Y so the
        // sprite leaves from the arm, not the floor. X/Z stay with the player.
        Vector3 startPos = new Vector3(
            playerGroundPos.x + startupOffset.x,
            playerGroundPos.y + startupOffset.y,
            playerGroundPos.z);
        go.transform.position = startPos;

        // Target: collider bounds center so we land on the middle of the enemy sprite,
        // not the transform root which tends to sit at the enemy's feet.
        Vector3 targetPos = targetEnemy.transform.position;
        var col = targetEnemy.GetComponentInChildren<Collider2D>();
        if (col != null) targetPos = col.bounds.center;

        // Horizontal flight — Y locked to start so grounded enemies take a flat shot.
        // Flying enemies get a diagonal line up to their hurtbox center.
        Vector3 endPos = new Vector3(
            targetPos.x,
            targetEnemy.IsFlying ? targetPos.y : startPos.y,
            startPos.z);

        Vector3 direction = (endPos - startPos).normalized;
        if (direction.sqrMagnitude > 0.001f)
            go.transform.right = direction;

        float distance = Vector3.Distance(startPos, endPos);
        float speed = Mathf.Max(force * 0.02f, 5f);
        float duration = Mathf.Clamp(distance / speed, 0.15f, 0.6f);
        float elapsed = 0f;

        while (elapsed < duration && targetEnemy != null)
        {
            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / duration);
            go.transform.position = Vector3.Lerp(startPos, endPos, u);
            yield return null;
        }

        Battle.Attacks.AttackManager.OnFireComplete?.Invoke();

        Destroy(go);
        IsAttacking = false;
    }

    /// <summary>
    /// Looks up the shot prefab for an orb (via ProjectileAttack) and pulls out
    /// the visual data we need to mirror the host's ShotBehavior: sprite/material,
    /// serialized min/max size, startup offset, and force range.
    /// </summary>
    private static ShotVisual? TryGetShotVisual(string orbName, bool isCrit)
    {
        if (string.IsNullOrEmpty(orbName)) return null;
        string key = orbName.Replace("(Clone)", "").Trim() + (isCrit ? "#crit" : "#normal");
        if (_shotCache.TryGetValue(key, out var cached)) return cached;

        ShotVisual? result = null;
        try
        {
            var orbGo = FindOrbPrefab(orbName);
            if (orbGo == null) { _shotCache[key] = null; return null; }

            var pa = orbGo.GetComponent<Battle.Attacks.ProjectileAttack>();
            if (pa == null) { _shotCache[key] = null; return null; }

            string primaryField = isCrit ? "_criticalShotPrefab" : "_shotPrefab";
            var shotGo = AccessTools.Field(typeof(Battle.Attacks.ProjectileAttack), primaryField)
                ?.GetValue(pa) as GameObject;
            // Some orbs don't define a critical prefab — fall back to the normal shot.
            if (shotGo == null && isCrit)
            {
                shotGo = AccessTools.Field(typeof(Battle.Attacks.ProjectileAttack), "_shotPrefab")
                    ?.GetValue(pa) as GameObject;
            }
            if (shotGo == null) { _shotCache[key] = null; return null; }

            var sb = shotGo.GetComponent<Battle.Attacks.ShotBehavior>();
            if (sb == null) { _shotCache[key] = null; return null; }

            // Prefer the shot's child SpriteRenderer (the actual projectile graphic).
            // Fall back to the orb's renderer if the shot uses only an Animator that
            // hasn't populated the default sprite.
            var shotSr = shotGo.GetComponentInChildren<SpriteRenderer>(includeInactive: true);
            if (shotSr == null || shotSr.sprite == null)
                shotSr = orbGo.GetComponentInChildren<SpriteRenderer>(includeInactive: true);
            if (shotSr == null || shotSr.sprite == null) { _shotCache[key] = null; return null; }

            Vector3 minSize = (Vector3)(AccessTools.Field(typeof(Battle.Attacks.ShotBehavior), "_minSize")
                ?.GetValue(sb) ?? DefaultMinSize);
            Vector3 maxSize = (Vector3)(AccessTools.Field(typeof(Battle.Attacks.ShotBehavior), "_maxSize")
                ?.GetValue(sb) ?? DefaultMaxSize);
            Vector3 startupOffset = (Vector3)(AccessTools.Field(typeof(Battle.Attacks.ShotBehavior), "_startupOffset")
                ?.GetValue(sb) ?? DefaultStartupOffset);
            float minForce = (float)(AccessTools.Field(typeof(Battle.Attacks.ShotBehavior), "_minForce")
                ?.GetValue(sb) ?? DefaultMinForce);
            float maxForce = (float)(AccessTools.Field(typeof(Battle.Attacks.ShotBehavior), "_maxForce")
                ?.GetValue(sb) ?? DefaultMaxForce);
            float forcePerPeg = (float)(AccessTools.Field(typeof(Battle.Attacks.ShotBehavior), "_forcePerPeg")
                ?.GetValue(sb) ?? DefaultForcePerPeg);

            result = new ShotVisual
            {
                Sprite = shotSr.sprite,
                Material = shotSr.sharedMaterial,
                SortingLayerId = shotSr.sortingLayerID,
                SortingOrder = Mathf.Max(shotSr.sortingOrder, 150),
                FlipX = shotSr.flipX,
                MinSize = minSize,
                MaxSize = maxSize,
                StartupOffset = startupOffset,
                MinForce = minForce,
                MaxForce = maxForce,
                ForcePerPeg = forcePerPeg,
            };
        }
        catch { result = null; }

        _shotCache[key] = result;
        return result;
    }

    private static GameObject FindOrbPrefab(string orbName)
    {
        string cleanName = orbName.Replace("(Clone)", "").Trim();

        var loader = Loading.AssetLoading.Instance;
        var prefab = loader?.GetOrbPrefab(cleanName);
        if (prefab != null) return prefab;

        var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
        var dm = dms.Length > 0 ? dms[0] : null;
        if (dm?.battleDeck != null)
        {
            foreach (var orb in dm.battleDeck)
            {
                if (orb != null && orb.name.Replace("(Clone)", "").Trim() == cleanName)
                    return orb;
            }
        }

        return null;
    }

    /// <summary>Last-resort fallback: copy the orb's ball sprite.</summary>
    private static bool TryCopyOrbSprite(SpriteRenderer sr, string orbName)
    {
        if (string.IsNullOrEmpty(orbName)) return false;
        var orbGo = FindOrbPrefab(orbName);
        if (orbGo == null) return false;

        var orbRenderer = orbGo.GetComponentInChildren<SpriteRenderer>(includeInactive: true);
        if (orbRenderer?.sprite == null) return false;

        sr.sprite = orbRenderer.sprite;
        if (orbRenderer.sharedMaterial != null)
            sr.material = orbRenderer.sharedMaterial;
        sr.sortingLayerID = orbRenderer.sortingLayerID;
        return true;
    }

    /// <summary>Copy sprite + material from ClientBallRenderer's active ball.</summary>
    private static bool TryCopyFromBallRenderer(SpriteRenderer sr)
    {
        var cbr = ClientBallRenderer.Instance;
        if (cbr == null) return false;

        var field = AccessTools.Field(typeof(ClientBallRenderer), "_ballRenderer");
        var ballSr = field?.GetValue(cbr) as SpriteRenderer;
        if (ballSr?.sprite == null) return false;

        sr.sprite = ballSr.sprite;
        if (ballSr.sharedMaterial != null)
            sr.material = ballSr.sharedMaterial;
        sr.sortingLayerID = ballSr.sortingLayerID;
        return true;
    }

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
