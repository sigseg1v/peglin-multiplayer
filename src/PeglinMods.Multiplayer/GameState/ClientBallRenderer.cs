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
        if (!_isActive || _ballObject == null) return;

        // Dead-reckoning: extrapolate position using velocity between network updates
        float dt = Time.time - _lastUpdateTime;
        if (dt > 0f && dt < 0.2f) // Don't extrapolate too far
        {
            var extrapolated = _targetPos + _velocity * dt;
            // Apply gravity
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
