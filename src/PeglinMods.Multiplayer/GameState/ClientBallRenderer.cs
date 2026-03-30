using UnityEngine;

namespace PeglinMods.Multiplayer.GameState;

/// <summary>
/// Visual-only ball renderer for the spectating client.
/// Receives position updates from the host and interpolates for smooth display.
/// No physics — just a sprite that follows the host's ball.
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

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void OnShotFired(float aimX, float aimY)
    {
        if (_ballObject == null)
            CreateBall();

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

    public void UpdateBallPosition(float posX, float posY, float velX, float velY, float timestamp)
    {
        if (!_isActive || _ballObject == null) return;

        _targetPos = new Vector2(posX, posY);
        _velocity = new Vector2(velX, velY);
        _lastUpdateTime = Time.time;

        // Snap to position (with slight smoothing in Update)
        _ballObject.transform.position = new Vector3(posX, posY, -1f);
    }

    public void OnBallDestroyed()
    {
        _isActive = false;
        if (_ballObject != null)
            _ballObject.SetActive(false);
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
