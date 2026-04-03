using System.Collections.Generic;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState;

/// <summary>
/// Visual-only ball renderer for the spectating client.
/// Supports the primary ball + additional multiball visuals.
/// No physics — just sprites that follow the host's ball positions.
/// </summary>
public class ClientBallRenderer : MonoBehaviour
{
    public static ClientBallRenderer Instance { get; private set; }

    private GameObject _ballObject;
    private SpriteRenderer _ballRenderer;
    private Vector2 _targetPos;
    private Vector2 _velocity;
    private float _lastUpdateTime;
    private bool _isActive;
    private bool _isAiming; // True between BallUsed (orb drawn) and ShotFired
    private Vector2 _aimDirection;
    private Vector3 _spawnPos;

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

        // Copy render settings from a real PachinkoBall in the scene if available
        try
        {
            var realBall = Object.FindObjectOfType<PachinkoBall>();
            if (realBall != null)
            {
                var realRenderer = realBall.GetComponentInChildren<SpriteRenderer>();
                if (realRenderer?.sprite != null && _ballRenderer != null)
                {
                    _ballRenderer.sortingLayerName = realRenderer.sortingLayerName;
                    _ballRenderer.sortingOrder = realRenderer.sortingOrder + 1;
                    _ballObject.transform.localScale = realBall.transform.localScale;
                    _ballObject.transform.position = new Vector3(_spawnPos.x, _spawnPos.y, realBall.transform.position.z);
                }
            }
        }
        catch { }

        // Always force sorting layer — the real ball may not exist on client (DrawBall blocked)
        if (_ballRenderer != null && _ballRenderer.sortingLayerName == "Default")
        {
            _ballRenderer.sortingLayerName = "PegBoardMain";
            _ballRenderer.sortingOrder = 100;
        }

        var hasSprite = _ballRenderer?.sprite != null;
        MultiplayerPlugin.Logger?.LogInfo(
            $"[ClientBallRenderer] OnOrbDrawn '{orbName}' playerTransform={pt != null} " +
            $"pos=({_ballObject.transform.position.x:F1},{_ballObject.transform.position.y:F1},{_ballObject.transform.position.z:F1}) " +
            $"scale=({_ballObject.transform.localScale.x:F2},{_ballObject.transform.localScale.y:F2}) " +
            $"hasSprite={hasSprite} layer={_ballRenderer?.sortingLayerName} order={_ballRenderer?.sortingOrder} " +
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
                    _ballObject.transform.localScale = orbGo.transform.localScale * 0.8f;
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
        renderer.sortingLayerName = "PegBoardMain";
        renderer.sortingOrder = 100;

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
            _ballObject.transform.position = new Vector3(_spawnPos.x, _spawnPos.y, -1f);

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
        _ballRenderer.sortingLayerName = "PegBoardMain";
        _ballRenderer.sortingOrder = 100; // Above pegs

        // Try to get the orb sprite from the current orb
        try
        {
            var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
            var dm = dms.Length > 0 ? dms[0] : null;
            if (dm?.shuffledDeck != null && dm.shuffledDeck.Count > 0)
            {
                var orb = dm.shuffledDeck.Peek();
                var orbRenderer = orb?.GetComponentInChildren<SpriteRenderer>();
                if (orbRenderer != null)
                {
                    _ballRenderer.sprite = orbRenderer.sprite;
                    _ballObject.transform.localScale = orb.transform.localScale;
                }
            }
        }
        catch { }

        // Fallback: create a simple circle if no sprite found
        if (_ballRenderer.sprite == null)
        {
            _ballRenderer.sprite = CreateCircleSprite();
            _ballRenderer.color = Color.white;
            _ballObject.transform.localScale = Vector3.one * 0.5f;
        }

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
