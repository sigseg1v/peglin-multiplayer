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
    private TrailRenderer _trailRenderer;
    private Vector2 _targetPos;
    private Vector2 _velocity;
    private float _lastUpdateTime;
    private bool _hasReceivedPosition;
    private bool _isActive;
    private bool _isAiming; // True between BallUsed (orb drawn) and ShotFired
    private Vector2 _aimDirection;
    private Vector3 _spawnPos;
    private bool _renderCopied; // True once material+sorting copied from a real renderer
    private string _currentOrbName; // Name of the orb currently being displayed (for stale detection)

    // Higher = snappier, lower = smoother. ~25 gives ~40ms time-constant, which hides
    // 50ms-spaced updates while still converging before they feel laggy.
    private const float PositionSmoothRate = 25f;

    // Additional multiball visuals keyed by host-assigned GUID.
    private class MultiballState
    {
        public GameObject GameObject;
        public Vector2 TargetPos;
        public Vector2 Velocity;
        public float LastUpdateTime;
        public bool HasReceivedPosition;
    }
    private readonly Dictionary<string, MultiballState> _multiballs = new Dictionary<string, MultiballState>();

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
        foreach (var mb in _multiballs.Values)
        {
            if (mb?.GameObject != null) Destroy(mb.GameObject);
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
        _hasReceivedPosition = false;
        _isAiming = false;
        _isActive = true;
        _ballObject.SetActive(true);

        if (_trailRenderer != null)
        {
            _trailRenderer.Clear();
            _trailRenderer.enabled = true;
            _trailRenderer.emitting = true;
        }
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
                }

                var orbTrail = orbGo.GetComponentInChildren<TrailRenderer>();
                ApplyTrailFromPrefab(orbTrail);
            }
        }
        catch { }
    }

    private void ApplyTrailFromPrefab(TrailRenderer prefabTrail)
    {
        if (_ballObject == null) return;
        if (_trailRenderer == null)
        {
            _trailRenderer = _ballObject.AddComponent<TrailRenderer>();
            _trailRenderer.emitting = false;
        }
        if (prefabTrail == null) return;

        _trailRenderer.time = prefabTrail.time;
        _trailRenderer.minVertexDistance = prefabTrail.minVertexDistance;
        _trailRenderer.widthCurve = prefabTrail.widthCurve;
        _trailRenderer.widthMultiplier = prefabTrail.widthMultiplier;
        _trailRenderer.colorGradient = prefabTrail.colorGradient;
        _trailRenderer.startColor = prefabTrail.startColor;
        _trailRenderer.endColor = prefabTrail.endColor;
        _trailRenderer.startWidth = prefabTrail.startWidth;
        _trailRenderer.endWidth = prefabTrail.endWidth;
        _trailRenderer.textureMode = prefabTrail.textureMode;
        _trailRenderer.alignment = prefabTrail.alignment;
        _trailRenderer.numCornerVertices = prefabTrail.numCornerVertices;
        _trailRenderer.numCapVertices = prefabTrail.numCapVertices;
        _trailRenderer.shadowCastingMode = prefabTrail.shadowCastingMode;
        _trailRenderer.receiveShadows = prefabTrail.receiveShadows;
        _trailRenderer.sortingLayerID = prefabTrail.sortingLayerID;
        _trailRenderer.sortingOrder = prefabTrail.sortingOrder + 1;
        _trailRenderer.autodestruct = false;
        if (prefabTrail.sharedMaterial != null)
            _trailRenderer.sharedMaterial = prefabTrail.sharedMaterial;
    }

    public void UpdateBallPosition(float posX, float posY, float velX, float velY, float timestamp)
    {
        if (!_isActive || _ballObject == null) return;

        _targetPos = new Vector2(posX, posY);
        _velocity = new Vector2(velX, velY);
        _lastUpdateTime = Time.time;

        // First update after launch seeds the visible position so the trail starts
        // at the host's first reported sample instead of spawn + a long gap.
        if (!_hasReceivedPosition)
        {
            _hasReceivedPosition = true;
            _ballObject.transform.position = new Vector3(posX, posY, -1f);
            if (_trailRenderer != null) _trailRenderer.Clear();
        }
    }

    public void OnMultiballSpawned(string guid, float posX, float posY, float velX, float velY, string orbName)
    {
        if (string.IsNullOrEmpty(guid)) return;
        if (_multiballs.ContainsKey(guid)) return;

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

        _multiballs[guid] = new MultiballState
        {
            GameObject = ball,
            TargetPos = new Vector2(posX, posY),
            Velocity = new Vector2(velX, velY),
            LastUpdateTime = Time.time,
            HasReceivedPosition = false,
        };
    }

    public void UpdateMultiballPosition(string guid, float posX, float posY, float velX, float velY, float timestamp)
    {
        if (string.IsNullOrEmpty(guid)) return;
        if (!_multiballs.TryGetValue(guid, out var state) || state.GameObject == null) return;

        state.TargetPos = new Vector2(posX, posY);
        state.Velocity = new Vector2(velX, velY);
        state.LastUpdateTime = Time.time;

        if (!state.HasReceivedPosition)
        {
            state.HasReceivedPosition = true;
            state.GameObject.transform.position = new Vector3(posX, posY, -1f);
        }
    }

    public void OnMultiballDestroyed(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return;
        if (!_multiballs.TryGetValue(guid, out var state)) return;
        if (state.GameObject != null) Destroy(state.GameObject);
        _multiballs.Remove(guid);
    }

    public void OnBallDestroyed()
    {
        _isActive = false;
        _hasReceivedPosition = false;
        _currentOrbName = null;
        if (_trailRenderer != null)
        {
            _trailRenderer.emitting = false;
            _trailRenderer.Clear();
            _trailRenderer.enabled = false;
        }
        if (_ballObject != null)
            _ballObject.SetActive(false);

        // Clean up multiball visuals
        foreach (var mb in _multiballs.Values)
        {
            if (mb?.GameObject != null) Destroy(mb.GameObject);
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

        if (_isActive && _hasReceivedPosition)
        {
            // Flight phase: extrapolate from the last host sample using velocity + gravity,
            // then smoothly lerp the visible position toward it. Snap-free; this hides
            // 50ms-spaced packets and tolerates the occasional late/dropped update.
            float dt = Time.time - _lastUpdateTime;
            if (dt > 0.25f) dt = 0.25f; // clamp so stalls don't launch the ball off-screen

            var predicted = _targetPos + _velocity * dt;
            predicted.y += -9.81f * dt * dt * 0.5f;

            var current = (Vector2)_ballObject.transform.position;
            float t = 1f - Mathf.Exp(-PositionSmoothRate * Time.deltaTime);
            var lerped = Vector2.Lerp(current, predicted, t);
            _ballObject.transform.position = new Vector3(lerped.x, lerped.y, -1f);
        }

        // Smooth multiball visuals toward the latest host sample using the same
        // extrapolation pattern as the primary ball.
        if (_multiballs.Count > 0)
        {
            foreach (var state in _multiballs.Values)
            {
                if (state?.GameObject == null || !state.HasReceivedPosition) continue;
                float dt = Time.time - state.LastUpdateTime;
                if (dt > 0.25f) dt = 0.25f;

                var predicted = state.TargetPos + state.Velocity * dt;
                predicted.y += -9.81f * dt * dt * 0.5f;

                var current = (Vector2)state.GameObject.transform.position;
                float t = 1f - Mathf.Exp(-PositionSmoothRate * Time.deltaTime);
                var lerped = Vector2.Lerp(current, predicted, t);
                state.GameObject.transform.position = new Vector3(lerped.x, lerped.y, -1f);
            }
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
