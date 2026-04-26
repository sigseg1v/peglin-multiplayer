using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Multipeglin.Utility;
using UnityEngine;

namespace Multipeglin.GameState;

/// <summary>
/// Visual-only attack projectile shown on host and spectating clients.
/// Instantiates the orb's real shot prefab with physics/collision/game-logic
/// components stripped, then applies the exact same scale formula ShotBehavior
/// uses. This guarantees the rendered size matches singleplayer and is identical
/// across players (same prefab, same formula, same inputs).
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

    // Defaults used when the orb has no ProjectileAttack / shot prefab (healing, AoE, etc.).
    private const float DefaultMinForce = 100f;
    private const float DefaultMaxForce = 900f;
    private const float DefaultForcePerPeg = 50f;
    private static readonly Vector3 DefaultMinSize = new Vector3(0.2f, 0.2f, 1f);
    private static readonly Vector3 DefaultMaxSize = new Vector3(0.6f, 0.6f, 1f);
    private static readonly Vector3 DefaultStartupOffset = new Vector3(0f, 1.2f, 0f);

    private struct ShotParams
    {
        public GameObject ShotPrefab; // real prefab to instantiate (null = fallback path)
        public Vector3 MinSize;
        public Vector3 MaxSize;
        public Vector3 StartupOffset;
        public float MinForce;
        public float MaxForce;
        public float ForcePerPeg;
    }

    private static readonly Dictionary<string, ShotParams?> _shotCache = new Dictionary<string, ShotParams?>();

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
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
        var waited = 0f;
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
        if (!_waitingForFire)
        {
            return;
        }

        _waitingForFire = false;
        PeglinBattleAnimationController.OnFirePoint -= OnFirePoint;

        if (string.IsNullOrEmpty(_targetEnemyGuid))
        { IsAttacking = false;
            return; }

        var enemyId = MultiplayerPlugin.Services?.TryResolve<EnemyIdentifier>(out var eid) == true ? eid : null;
        var enemy = enemyId?.Find(_targetEnemyGuid);
        if (enemy == null)
        { IsAttacking = false;
            return; }

        var bc = Object.FindObjectOfType<Battle.BattleController>();
        if (bc == null)
        { IsAttacking = false;
            return; }

        var playerField = AccessTools.Field(typeof(Battle.BattleController), "_playerTransform");
        var playerTransform = playerField?.GetValue(bc) as Transform;
        if (playerTransform == null)
        { IsAttacking = false;
            return; }

        StartCoroutine(LaunchProjectile(playerTransform.position, enemy));
    }

    private IEnumerator LaunchProjectile(Vector3 playerGroundPos, Battle.Enemies.Enemy targetEnemy)
    {
        var paramsOpt = TryGetShotParams(_orbName, _isCrit);

        Vector3 minSize = DefaultMinSize;
        Vector3 maxSize = DefaultMaxSize;
        Vector3 startupOffset = DefaultStartupOffset;
        var minForce = DefaultMinForce;
        var maxForce = DefaultMaxForce;
        var forcePerPeg = DefaultForcePerPeg;

        GameObject go;

        if (paramsOpt.HasValue && paramsOpt.Value.ShotPrefab != null)
        {
            var p = paramsOpt.Value;
            minSize = p.MinSize;
            maxSize = p.MaxSize;
            minForce = p.MinForce;
            maxForce = p.MaxForce;
            forcePerPeg = p.ForcePerPeg;
            if (p.StartupOffset.sqrMagnitude > 0.0001f)
            {
                startupOffset = p.StartupOffset;
            }

            // Instantiate the real shot prefab so the visual (sprite hierarchy,
            // animator, particle children) is identical to singleplayer. Then
            // strip every game-logic component so it can't cause collisions,
            // damage, or state changes.
            go = Object.Instantiate(p.ShotPrefab);
            go.name = "ClientProjectile";
            StripGameLogicComponents(go);
        }
        else
        {
            go = new GameObject("ClientProjectile");
            var sr = go.AddComponent<SpriteRenderer>();
            var ok = TryCopyOrbSprite(sr, _orbName) || TryCopyFromBallRenderer(sr);
            if (!ok)
            {
                sr.sprite = CreateCircleSprite();
                sr.color = Color.yellow;
            }

            sr.sortingOrder = 150;
        }

        // ShotBehavior.Fire formula, applied to the instance's root transform —
        // exactly as the game does it.
        var force = Mathf.Clamp(_numPegsHit * forcePerPeg, minForce, maxForce);
        var t = maxForce > 0f ? Mathf.Abs(force) / maxForce : 0f;
        go.transform.localScale = Vector3.Lerp(minSize, maxSize, t);

        Vector3 startPos = new Vector3(
            playerGroundPos.x + startupOffset.x,
            playerGroundPos.y + startupOffset.y,
            playerGroundPos.z);
        go.transform.position = startPos;

        Vector3 targetPos = targetEnemy.transform.position;
        var col = targetEnemy.GetComponentInChildren<Collider2D>();
        if (col != null)
        {
            targetPos = col.bounds.center;
        }

        Vector3 endPos = new Vector3(targetPos.x, targetPos.y, startPos.z);

        Vector3 direction = (endPos - startPos).normalized;
        if (direction.sqrMagnitude > 0.001f)
        {
            go.transform.right = direction;
        }

        var distance = Vector3.Distance(startPos, endPos);
        var speed = Mathf.Max(force * 0.02f, 5f);
        var duration = Mathf.Clamp(distance / speed, 0.15f, 0.6f);
        var elapsed = 0f;

        while (elapsed < duration && targetEnemy != null)
        {
            elapsed += Time.deltaTime;
            var u = Mathf.Clamp01(elapsed / duration);
            go.transform.position = Vector3.Lerp(startPos, endPos, u);
            yield return null;
        }

        Battle.Attacks.AttackManager.OnFireComplete?.Invoke();

        Destroy(go);
        IsAttacking = false;
    }

    /// <summary>
    /// Remove every component on the instance that could interact with the running
    /// battle: ShotBehavior (damage/collision logic), colliders (peg/enemy overlap),
    /// Rigidbody2D (physics), AudioSource (reduces duplicate SFX). Leave SpriteRenderer
    /// and Animator so the visual renders and animates.
    /// </summary>
    private static void StripGameLogicComponents(GameObject go)
    {
        foreach (var sb in go.GetComponentsInChildren<Battle.Attacks.ShotBehavior>(includeInactive: true))
        {
            Destroy(sb);
        }

        foreach (var beh in go.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
        {
            // Kill anything in the Battle.Attacks namespace except what we need for visuals.
            var ns = beh?.GetType().Namespace ?? string.Empty;
            if (ns.StartsWith("Battle.Attacks"))
            {
                Destroy(beh);
            }
        }

        foreach (var c in go.GetComponentsInChildren<Collider2D>(includeInactive: true))
        {
            Destroy(c);
        }

        foreach (var rb in go.GetComponentsInChildren<Rigidbody2D>(includeInactive: true))
        {
            Destroy(rb);
        }

        foreach (var audio in go.GetComponentsInChildren<AudioSource>(includeInactive: true))
        {
            Destroy(audio);
        }

        // ShotBehavior.OnEnable starts with _renderer.enabled = false; ensure all
        // SpriteRenderers are on so our instantiated visual is actually visible.
        foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
        {
            sr.enabled = true;
            if (sr.sortingOrder < 150)
            {
                sr.sortingOrder = 150;
            }
        }
    }

    /// <summary>
    /// Resolve the shot prefab and scaling params for this orb. Results are cached
    /// per (orb,crit) key so the reflection only runs once per unique shot.
    /// </summary>
    private static ShotParams? TryGetShotParams(string orbName, bool isCrit)
    {
        if (string.IsNullOrEmpty(orbName))
        {
            return null;
        }

        var key = orbName.Replace("(Clone)", string.Empty).Trim() + (isCrit ? "#crit" : "#normal");
        if (_shotCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        ShotParams? result = null;
        try
        {
            var orbGo = FindOrbPrefab(orbName);
            if (orbGo == null)
            { _shotCache[key] = null;
                return null; }

            var pa = orbGo.GetComponent<Battle.Attacks.ProjectileAttack>();
            if (pa == null)
            { _shotCache[key] = null;
                return null; }

            var primaryField = isCrit ? "_criticalShotPrefab" : "_shotPrefab";
            var shotGo = AccessTools.Field(typeof(Battle.Attacks.ProjectileAttack), primaryField)
                ?.GetValue(pa) as GameObject;
            if (shotGo == null && isCrit)
            {
                shotGo = AccessTools.Field(typeof(Battle.Attacks.ProjectileAttack), "_shotPrefab")
                    ?.GetValue(pa) as GameObject;
            }

            if (shotGo == null)
            { _shotCache[key] = null;
                return null; }

            var sb = shotGo.GetComponent<Battle.Attacks.ShotBehavior>();
            if (sb == null)
            { _shotCache[key] = null;
                return null; }

            Vector3 minSize = (Vector3)(AccessTools.Field(typeof(Battle.Attacks.ShotBehavior), "_minSize")
                ?.GetValue(sb) ?? DefaultMinSize);
            Vector3 maxSize = (Vector3)(AccessTools.Field(typeof(Battle.Attacks.ShotBehavior), "_maxSize")
                ?.GetValue(sb) ?? DefaultMaxSize);
            Vector3 startupOffset = (Vector3)(AccessTools.Field(typeof(Battle.Attacks.ShotBehavior), "_startupOffset")
                ?.GetValue(sb) ?? DefaultStartupOffset);
            var minForce = (float)(AccessTools.Field(typeof(Battle.Attacks.ShotBehavior), "_minForce")
                ?.GetValue(sb) ?? DefaultMinForce);
            var maxForce = (float)(AccessTools.Field(typeof(Battle.Attacks.ShotBehavior), "_maxForce")
                ?.GetValue(sb) ?? DefaultMaxForce);
            var forcePerPeg = (float)(AccessTools.Field(typeof(Battle.Attacks.ShotBehavior), "_forcePerPeg")
                ?.GetValue(sb) ?? DefaultForcePerPeg);

            result = new ShotParams
            {
                ShotPrefab = shotGo,
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
        var cleanName = orbName.Replace("(Clone)", string.Empty).Trim();

        var loader = Loading.AssetLoading.Instance;
        var prefab = loader?.GetOrbPrefab(cleanName);
        if (prefab != null)
        {
            return prefab;
        }

        var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
        var dm = dms.Length > 0 ? dms[0] : null;
        if (dm?.battleDeck != null)
        {
            foreach (var orb in dm.battleDeck)
            {
                if (orb != null && orb.name.Replace("(Clone)", string.Empty).Trim() == cleanName)
                {
                    return orb;
                }
            }
        }

        return null;
    }

    /// <summary>Fallback when no shot prefab is available (healing, AoE orbs).</summary>
    private static bool TryCopyOrbSprite(SpriteRenderer sr, string orbName)
    {
        if (string.IsNullOrEmpty(orbName))
        {
            return false;
        }

        var orbGo = FindOrbPrefab(orbName);
        if (orbGo == null)
        {
            return false;
        }

        var orbRenderer = orbGo.GetComponentInChildren<SpriteRenderer>(includeInactive: true);
        if (orbRenderer?.sprite == null)
        {
            return false;
        }

        sr.sprite = orbRenderer.sprite;
        if (orbRenderer.sharedMaterial != null)
        {
            sr.material = orbRenderer.sharedMaterial;
        }

        sr.sortingLayerID = orbRenderer.sortingLayerID;
        return true;
    }

    private static bool TryCopyFromBallRenderer(SpriteRenderer sr)
    {
        var cbr = ClientBallRenderer.Instance;
        if (cbr == null)
        {
            return false;
        }

        var field = AccessTools.Field(typeof(ClientBallRenderer), "_ballRenderer");
        var ballSr = field?.GetValue(cbr) as SpriteRenderer;
        if (ballSr?.sprite == null)
        {
            return false;
        }

        sr.sprite = ballSr.sprite;
        if (ballSr.sharedMaterial != null)
        {
            sr.material = ballSr.sharedMaterial;
        }

        sr.sortingLayerID = ballSr.sortingLayerID;
        return true;
    }

    private static Sprite CreateCircleSprite()
    {
        var size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var center = size / 2f;
        var radius = center - 1;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                tex.SetPixel(x, y, Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) <= radius ? Color.white : Color.clear);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
