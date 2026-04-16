using System.Collections.Generic;
using UnityEngine;

namespace Multipeglin.GameState;

/// <summary>
/// Visual-only ball renderer for the spectating client.
/// Supports the primary ball + additional multiball visuals.
/// No physics — just sprites that follow the host's ball positions.
/// </summary>
public class ClientBallRenderer : MonoBehaviour
{
    public static ClientBallRenderer Instance { get; private set; }

    /// <summary>Name of the orb currently displayed (for stale detection by heartbeat applier).</summary>
    public string CurrentOrbName => _currentOrbName;

    private GameObject _ballObject;
    private SpriteRenderer _ballRenderer;
    private Vector2 _targetPos;
    private Vector2 _velocity;
    private float _lastUpdateTime;
    private bool _isActive;
    private bool _isAiming; // True between BallUsed (orb drawn) and ShotFired
    private Vector2 _aimDirection;
    private Vector3 _spawnPos;
    private bool _renderCopied; // True once material+sorting copied from a real renderer
    private string _currentOrbName; // Name of the orb currently being displayed (for stale detection)

    // Additional multiball visuals
    private readonly List<GameObject> _multiballs = new List<GameObject>();

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Show the orb at the spawn point during the aiming phase.
    /// Called from BallUsedClientHandler when the host draws an orb.
    /// The orb stays at the spawn position until OnShotFired launches it.
    /// </summary>
    public void OnOrbDrawn(string orbName)
    {
        if (_ballObject == null)
            CreateBall();

        _currentOrbName = orbName?.Replace("(Clone)", "").Trim();
        UpdateBallSprite(orbName);

        // Position at player spawn
        var bc = Object.FindObjectOfType<Battle.BattleController>();
        Transform pt = null;
        if (bc != null)
        {
            var playerField = HarmonyLib.AccessTools.Field(typeof(Battle.BattleController), "_playerTransform");
            pt = playerField?.GetValue(bc) as Transform;
            if (pt != null)
            {
                _spawnPos = pt.position;
                _ballObject.transform.position = new Vector3(_spawnPos.x, _spawnPos.y, -1f);
            }
        }

        _isAiming = true;
        _isActive = false; // Not launched yet
        _ballObject.SetActive(true);

        // Fallback: if UpdateBallSprite didn't copy render settings from the orb prefab,
        // copy material + sorting from a visible peg so we use the correct URP 2D material.
        if (!_renderCopied && _ballRenderer != null)
        {
            try
            {
                var pegs = Object.FindObjectsOfType<Peg>();
                foreach (var p in pegs)
                {
                    if (p == null || !p.gameObject.activeSelf) continue;
                    var pr = p.GetComponentInChildren<SpriteRenderer>();
                    if (pr != null)
                    {
                        if (pr.sharedMaterial != null)
                            _ballRenderer.material = pr.sharedMaterial;
                        _ballRenderer.sortingLayerID = pr.sortingLayerID;
                        _ballRenderer.sortingOrder = pr.sortingOrder + 10;
                        _renderCopied = true;
                        break;
                    }
                }
            }
            catch { }
        }

        var hasSprite = _ballRenderer?.sprite != null;
        MultiplayerPlugin.Logger?.LogInfo(
            $"[ClientBallRenderer] OnOrbDrawn '{orbName}' playerTransform={pt != null} " +
            $"pos=({_ballObject.transform.position.x:F1},{_ballObject.transform.position.y:F1},{_ballObject.transform.position.z:F1}) " +
            $"scale=({_ballObject.transform.localScale.x:F2},{_ballObject.transform.localScale.y:F2}) " +
            $"hasSprite={hasSprite} layer={_ballRenderer?.sortingLayerName} order={_ballRenderer?.sortingOrder} " +
            $"material={_ballRenderer?.material?.name ?? "NULL"} renderCopied={_renderCopied} " +
            $"ballActive={_ballObject.activeSelf}");
    }

    /// <summary>
    /// Update the aim direction so the orb sprite rotates with the aimer.
    /// Called from ClientAimRenderer or ShotFired with the aim vector.
    /// </summary>
    public void UpdateAimDirection(float aimX, float aimY)
    {
        _aimDirection = new Vector2(aimX, aimY).normalized;
    }

    /// <summary>
    /// Update the spawn position from the host's aim event data.
    /// This ensures the ball is at the same position as the aimer line origin.
    /// </summary>
    public void UpdateSpawnPosition(float spawnX, float spawnY)
    {
        _spawnPos = new Vector3(spawnX, spawnY, 0f);
    }

    public void OnShotFired(float aimX, float aimY, string orbName = null)
    {
        // Clean up any previous multiball visuals
        foreach (var mb in _multiballs)
        {
            if (mb != null) Destroy(mb);
        }
        _multiballs.Clear();

        if (_ballObject == null)
            CreateBall();

        // Update the ball sprite to match the orb being fired (from host's ShotFiredEvent)
        UpdateBallSprite(orbName);

        // Find spawn position from BattleController's player transform
        var bc = Object.FindObjectOfType<Battle.BattleController>();
        if (bc != null)
        {
            var playerField = HarmonyLib.AccessTools.Field(typeof(Battle.BattleController), "_playerTransform");
            var playerTransform = playerField?.GetValue(bc) as Transform;
            if (playerTransform != null)
            {
                _targetPos = (Vector2)playerTransform.position;
                _ballObject.transform.position = new Vector3(_targetPos.x, _targetPos.y, -1f);
            }
        }

        _velocity = Vector2.zero;
        _lastUpdateTime = Time.time;
        _isAiming = false;
        _isActive = true;
        _ballObject.SetActive(true);
    }

    private void UpdateBallSprite(string orbName)
    {
        if (_ballRenderer == null) return;
        try
        {
            // Find the orb prefab by name from the host's ShotFiredEvent
            GameObject orbGo = null;
            string cleanName = orbName?.Replace("(Clone)", "").Trim();

            if (!string.IsNullOrEmpty(cleanName))
            {
                var loader = Loading.AssetLoading.Instance;
                if (loader != null)
                    orbGo = loader.GetOrbPrefab(cleanName);
            }

            // NavigationOrb: get from BattleController's serialized prefab
            if (orbGo == null && cleanName == "NavigationOrb")
            {
                var bc = Object.FindObjectOfType<Battle.BattleController>();
                if (bc != null)
                {
                    var navField = HarmonyLib.AccessTools.Field(typeof(Battle.BattleController), "_navigationOrb");
                    orbGo = navField?.GetValue(bc) as GameObject;
                }
            }

            // Fallback: try battleDeck
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

            if (orbGo != null)
            {
                var orbRenderer = orbGo.GetComponentInChildren<SpriteRenderer>();
                if (orbRenderer?.sprite != null)
                {
                    _ballRenderer.sprite = orbRenderer.sprite;
                    // Copy material — URP 2D uses a different sprite material than the
                    // built-in pipeline.  A bare AddComponent<SpriteRenderer>() gets the
                    // wrong material and renders invisible.
                    if (orbRenderer.sharedMaterial != null)
                        _ballRenderer.material = orbRenderer.sharedMaterial;
                    // Copy sorting settings from the prefab — sorting layer is baked into
                    // the asset, not set in code.
                    _ballRenderer.sortingLayerID = orbRenderer.sortingLayerID;
                    _ballRenderer.sortingOrder = 100;
                    _ballObject.transform.localScale = orbGo.transform.localScale * 0.8f;
                    _renderCopied = true;
                    return;
                }
            }
        }
        catch { }
    }

    public void UpdateBallPosition(float posX, float posY, float velX, float velY, float timestamp)
    {
        if (!_isActive || _ballObject == null) return;

        _targetPos = new Vector2(posX, posY);
        _velocity = new Vector2(velX, velY);
        _lastUpdateTime = Time.time;

        // Snap to position (with slight smoothing in Update)
        _ballObject.transform.position = new Vector3(posX, posY, -1f);
    }

    public void OnMultiballSpawned(float posX, float posY, float velX, float velY, string orbName)
    {
        var ball = new GameObject("ClientMultiball");
        ball.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(ball);

        var renderer = ball.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = 100;
        // Copy sorting layer and material from primary ball if available
        if (_ballRenderer != null)
        {
            renderer.sortingLayerID = _ballRenderer.sortingLayerID;
            if (_ballRenderer.sharedMaterial != null)
                renderer.material = _ballRenderer.sharedMaterial;
        }

        // Copy sprite from the primary ball if available
        if (_ballRenderer?.sprite != null)
        {
            renderer.sprite = _ballRenderer.sprite;
            ball.transform.localScale = _ballObject != null ? _ballObject.transform.localScale : Vector3.one * 0.5f;
        }
        else
        {
            renderer.sprite = CreateCircleSprite();
            ball.transform.localScale = Vector3.one * 0.5f;
        }

        ball.transform.position = new Vector3(posX, posY, -1f);

        // Add a simple physics simulation for the visual ball
        var rb = ball.AddComponent<Rigidbody2D>();
        rb.gravityScale = 1f;
        rb.velocity = new Vector2(velX, velY);
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        // Add a circle collider so it bounces off walls/pegs visually
        var col = ball.AddComponent<CircleCollider2D>();
        col.radius = 0.15f;
        col.isTrigger = false;

        // Auto-destroy after 30 seconds (safety net)
        Destroy(ball, 30f);

        _multiballs.Add(ball);
    }

    public void OnBallDestroyed()
    {
        _isActive = false;
        _currentOrbName = null;
        if (_ballObject != null)
            _ballObject.SetActive(false);

        // Clean up multiball visuals
        foreach (var mb in _multiballs)
        {
            if (mb != null) Destroy(mb);
        }
        _multiballs.Clear();
    }

    private void Update()
    {
        if (_ballObject == null) return;

        // Aiming phase: show orb at spawn position, rotate with aim direction
        if (_isAiming && !_isActive)
        {
            _ballObject.transform.position = new Vector3(_spawnPos.x, _spawnPos.y, -0.5f);

            // Rotate to face aim direction
            if (_aimDirection.sqrMagnitude > 0.01f)
            {
                float angle = Mathf.Atan2(_aimDirection.y, _aimDirection.x) * Mathf.Rad2Deg;
                _ballObject.transform.rotation = Quaternion.Euler(0, 0, angle);
            }
            return;
        }

        if (!_isActive) return;

        // Flight phase: extrapolate position using velocity between network updates
        float dt = Time.time - _lastUpdateTime;
        if (dt > 0f && dt < 0.2f)
        {
            var extrapolated = _targetPos + _velocity * dt;
            extrapolated.y += -9.81f * dt * dt * 0.5f;
            _ballObject.transform.position = new Vector3(extrapolated.x, extrapolated.y, -1f);
        }
    }

    private void CreateBall()
    {
        _ballObject = new GameObject("ClientBall");
        _ballObject.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(_ballObject);

        _ballRenderer = _ballObject.AddComponent<SpriteRenderer>();
        _ballRenderer.sortingOrder = 100;
        _renderCopied = false;

        // Copy the URP 2D sprite material from any visible SpriteRenderer in the scene.
        // A bare AddComponent<SpriteRenderer>() in URP gets the built-in Sprites-Default
        // material which doesn't render through the URP 2D renderer pipeline.
        try
        {
            var anyRenderer = Object.FindObjectOfType<SpriteRenderer>();
            if (anyRenderer?.sharedMaterial != null)
            {
                _ballRenderer.material = anyRenderer.sharedMaterial;
                _ballRenderer.sortingLayerID = anyRenderer.sortingLayerID;
            }
        }
        catch { }

        // Fallback: create a simple circle if no sprite found
        _ballRenderer.sprite = CreateCircleSprite();
        _ballRenderer.color = Color.white;
        _ballObject.transform.localScale = Vector3.one * 0.5f;

        _ballObject.SetActive(false);
    }

    private static Sprite CreateCircleSprite()
    {
        int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = size / 2f;
        float radius = center - 1;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                tex.SetPixel(x, y, dist <= radius ? Color.white : Color.clear);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
