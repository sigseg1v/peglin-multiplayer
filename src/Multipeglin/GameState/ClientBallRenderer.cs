using System.Collections.Generic;
using Multipeglin.GameState.Snapshots;
using UnityEngine;

namespace Multipeglin.GameState;

/// <summary>
/// Visual-only ball renderer for the spectating client. Two phases:
///   1) AIMING — a single orb sprite sits at the spawn point and rotates with
///      the aim vector (driven by BallUsed / ShotFired / AimUpdate events).
///   2) FLIGHT — the host dispatches a <see cref="BallStateSnapshot"/> at 20 Hz
///      listing every in-flight ball with GUID / position / velocity / orb name.
///      We reconcile visuals: spawn any GUID we've never seen, update positions
///      for known GUIDs, destroy any visual whose GUID is no longer in the snapshot.
/// No physics, no game logic — purely visual. One visual per host ball.
/// </summary>
public class ClientBallRenderer : MonoBehaviour
{
    public static ClientBallRenderer Instance { get; private set; }

    public string CurrentOrbName => _currentOrbName;

    // --- Aiming visual ---
    private GameObject _aimingBall;
    private SpriteRenderer _aimingRenderer;
    private Vector3 _spawnPos;
    private Vector2 _aimDirection;
    private bool _isAiming;
    private string _currentOrbName;

    // --- Flight visuals, one per host-assigned GUID ---
    private class FlightVisual
    {
        public GameObject GameObject;
        public SpriteRenderer Renderer;
        public TrailRenderer Trail;
        public string OrbName;
        public Vector2 TargetPos;
        public Vector2 Velocity;
        public float LastUpdateTime;
        public bool HasReceivedPosition;
    }
    private readonly Dictionary<string, FlightVisual> _flightBalls = new Dictionary<string, FlightVisual>();

    // Higher = snappier, lower = smoother. ~25 hides 50 ms-spaced packets.
    private const float PositionSmoothRate = 25f;

    private void Awake() { Instance = this; }
    private void OnDestroy() { if (Instance == this)
        {
            Instance = null;
        }
    }

    // =========================================================================
    // AIMING PHASE
    // =========================================================================

    public void OnOrbDrawn(string orbName)
    {
        EnsureAimingBall();
        _currentOrbName = orbName?.Replace("(Clone)", "").Trim();
        ApplyOrbSprite(_aimingRenderer, _aimingBall, orbName, scaleFactor: 0.8f, wantTrail: false);

        var bc = Object.FindObjectOfType<Battle.BattleController>();
        Transform pt = null;
        if (bc != null)
        {
            var playerField = HarmonyLib.AccessTools.Field(typeof(Battle.BattleController), "_playerTransform");
            pt = playerField?.GetValue(bc) as Transform;
            if (pt != null)
            {
                _spawnPos = pt.position;
                _aimingBall.transform.position = new Vector3(_spawnPos.x, _spawnPos.y, -1f);
            }
        }

        _isAiming = true;
        _aimingBall.SetActive(true);

        MultiplayerPlugin.Logger?.LogInfo(
            $"[ClientBallRenderer] OnOrbDrawn '{orbName}' playerTransform={pt != null} " +
            $"pos=({_aimingBall.transform.position.x:F1},{_aimingBall.transform.position.y:F1})");
    }

    public void UpdateAimDirection(float aimX, float aimY)
    {
        _aimDirection = new Vector2(aimX, aimY).normalized;
    }

    public void UpdateSpawnPosition(float spawnX, float spawnY)
    {
        _spawnPos = new Vector3(spawnX, spawnY, 0f);
    }

    public void OnShotFired(float aimX, float aimY, string orbName = null)
    {
        // Leaving aiming → flight. The snapshot will spawn the actual flight
        // visual. Hide the aiming orb so it doesn't double with the spawn.
        _aimingBall?.SetActive(false);

        _isAiming = false;

        // Clear any stale flight visuals from a previous shot that never got
        // cleaned up (e.g. because the host dropped their "empty snapshot" tick).
        foreach (var v in _flightBalls.Values)
        {
            if (v?.GameObject != null)
            {
                Destroy(v.GameObject);
            }
        }

        _flightBalls.Clear();
    }

    // =========================================================================
    // FLIGHT PHASE — driven entirely by BallStateSnapshot at 20 Hz
    // =========================================================================

    public void ApplyBallSnapshot(BallStateSnapshot snap)
    {
        if (snap == null)
        {
            return;
        }

        // Hide the aiming visual while flight balls exist.
        if (snap.Balls != null && snap.Balls.Count > 0 && _aimingBall != null && _aimingBall.activeSelf)
        {
            _aimingBall.SetActive(false);
            _isAiming = false;
        }

        var seen = new HashSet<string>();
        if (snap.Balls != null)
        {
            foreach (var entry in snap.Balls)
            {
                if (string.IsNullOrEmpty(entry.Guid))
                {
                    continue;
                }

                seen.Add(entry.Guid);

                if (!_flightBalls.TryGetValue(entry.Guid, out var v))
                {
                    v = SpawnFlightVisual(entry);
                    _flightBalls[entry.Guid] = v;
                }
                else if (v.OrbName != entry.OrbName && !string.IsNullOrEmpty(entry.OrbName))
                {
                    // Orb identity swapped (rare — e.g. convert-to-gold): refresh sprite.
                    ApplyOrbSprite(v.Renderer, v.GameObject, entry.OrbName, scaleFactor: 1f, wantTrail: true, existingTrail: v.Trail);
                    v.OrbName = entry.OrbName;
                }

                v.TargetPos = new Vector2(entry.PosX, entry.PosY);
                v.Velocity = new Vector2(entry.VelX, entry.VelY);
                v.LastUpdateTime = Time.time;
                v.GameObject?.transform.localScale = new Vector3(entry.ScaleX, entry.ScaleY, 1f);

                if (!v.HasReceivedPosition)
                {
                    v.HasReceivedPosition = true;
                    v.GameObject?.transform.position = new Vector3(entry.PosX, entry.PosY, -1f);

                    v.Trail?.Clear();
                }
            }
        }

        // Destroy any flight visuals that the host didn't include this tick.
        if (_flightBalls.Count != seen.Count)
        {
            var toRemove = new List<string>();
            foreach (var kvp in _flightBalls)
            {
                if (!seen.Contains(kvp.Key))
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var guid in toRemove)
            {
                if (_flightBalls.TryGetValue(guid, out var v) && v.GameObject != null)
                {
                    Destroy(v.GameObject);
                }

                _flightBalls.Remove(guid);
            }
        }
    }

    private FlightVisual SpawnFlightVisual(BallEntry entry)
    {
        var go = new GameObject($"ClientBall_{entry.Guid}")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        DontDestroyOnLoad(go);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 100;

        var v = new FlightVisual
        {
            GameObject = go,
            Renderer = sr,
            OrbName = entry.OrbName,
            TargetPos = new Vector2(entry.PosX, entry.PosY),
            Velocity = new Vector2(entry.VelX, entry.VelY),
            LastUpdateTime = Time.time,
            HasReceivedPosition = false,
        };

        ApplyOrbSprite(sr, go, entry.OrbName, scaleFactor: 1f, wantTrail: true);
        // ApplyOrbSprite may have added a TrailRenderer.
        v.Trail = go.GetComponent<TrailRenderer>();

        go.transform.position = new Vector3(entry.PosX, entry.PosY, -1f);
        go.transform.localScale = new Vector3(entry.ScaleX, entry.ScaleY, 1f);
        return v;
    }

    // =========================================================================
    // SHARED SPRITE LOOKUP
    // =========================================================================

    /// <summary>
    /// Resolve the orb prefab by name and copy its sprite / material / sorting
    /// layer to the given renderer. Optionally mirror its trail settings.
    /// </summary>
    private void ApplyOrbSprite(SpriteRenderer target, GameObject targetGO, string orbName,
        float scaleFactor, bool wantTrail, TrailRenderer existingTrail = null)
    {
        if (target == null)
        {
            return;
        }

        try
        {
            var cleanName = orbName?.Replace("(Clone)", "").Trim();
            GameObject orbGo = FindOrbPrefab(cleanName);

            if (orbGo != null)
            {
                var orbRenderer = orbGo.GetComponentInChildren<SpriteRenderer>();
                if (orbRenderer?.sprite != null)
                {
                    target.sprite = orbRenderer.sprite;
                    if (orbRenderer.sharedMaterial != null)
                    {
                        target.material = orbRenderer.sharedMaterial;
                    }

                    target.sortingLayerID = orbRenderer.sortingLayerID;
                    target.sortingOrder = 100;
                    targetGO.transform.localScale = orbGo.transform.localScale * scaleFactor;
                }

                if (wantTrail)
                {
                    var orbTrail = orbGo.GetComponentInChildren<TrailRenderer>();
                    var trail = existingTrail ?? targetGO.GetComponent<TrailRenderer>() ?? targetGO.AddComponent<TrailRenderer>();
                    CopyTrailSettings(trail, orbTrail);
                }
            }
            else
            {
                // Unknown orb — fallback so we at least draw *something*.
                target.sprite = CreateCircleSprite();
                targetGO.transform.localScale = Vector3.one * 0.5f;
                var anyRenderer = Object.FindObjectOfType<SpriteRenderer>();
                if (anyRenderer?.sharedMaterial != null)
                {
                    target.material = anyRenderer.sharedMaterial;
                    target.sortingLayerID = anyRenderer.sortingLayerID;
                }
            }
        }
        catch { }
    }

    private static GameObject FindOrbPrefab(string cleanName)
    {
        if (string.IsNullOrEmpty(cleanName))
        {
            return null;
        }

        var loader = Loading.AssetLoading.Instance;
        var orbGo = loader?.GetOrbPrefab(cleanName);
        if (orbGo != null)
        {
            return orbGo;
        }

        if (cleanName == "NavigationOrb")
        {
            var bc = Object.FindObjectOfType<Battle.BattleController>();
            if (bc != null)
            {
                var navField = HarmonyLib.AccessTools.Field(typeof(Battle.BattleController), "_navigationOrb");
                orbGo = navField?.GetValue(bc) as GameObject;
                if (orbGo != null)
                {
                    return orbGo;
                }
            }
        }

        var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
        var dm = dms.Length > 0 ? dms[0] : null;
        if (dm?.battleDeck != null)
        {
            foreach (var orb in dm.battleDeck)
            {
                if (orb != null && orb.name.Replace("(Clone)", "").Trim() == cleanName)
                {
                    return orb;
                }
            }
        }

        return null;
    }

    private static void CopyTrailSettings(TrailRenderer dst, TrailRenderer src)
    {
        if (dst == null)
        {
            return;
        }

        if (src == null)
        {
            dst.emitting = true;
            return;
        }

        dst.time = src.time;
        dst.minVertexDistance = src.minVertexDistance;
        dst.widthCurve = src.widthCurve;
        dst.widthMultiplier = src.widthMultiplier;
        dst.colorGradient = src.colorGradient;
        dst.startColor = src.startColor;
        dst.endColor = src.endColor;
        dst.startWidth = src.startWidth;
        dst.endWidth = src.endWidth;
        dst.textureMode = src.textureMode;
        dst.alignment = src.alignment;
        dst.numCornerVertices = src.numCornerVertices;
        dst.numCapVertices = src.numCapVertices;
        dst.shadowCastingMode = src.shadowCastingMode;
        dst.receiveShadows = src.receiveShadows;
        dst.sortingLayerID = src.sortingLayerID;
        dst.sortingOrder = src.sortingOrder + 1;
        dst.autodestruct = false;
        dst.emitting = true;
        if (src.sharedMaterial != null)
        {
            dst.sharedMaterial = src.sharedMaterial;
        }
    }

    // =========================================================================
    // UPDATE — aim rotation + flight smoothing
    // =========================================================================

    private void Update()
    {
        if (_isAiming && _aimingBall != null && _aimingBall.activeSelf)
        {
            _aimingBall.transform.position = new Vector3(_spawnPos.x, _spawnPos.y, -0.5f);
            if (_aimDirection.sqrMagnitude > 0.01f)
            {
                var angle = Mathf.Atan2(_aimDirection.y, _aimDirection.x) * Mathf.Rad2Deg;
                _aimingBall.transform.rotation = Quaternion.Euler(0, 0, angle);
            }
        }

        if (_flightBalls.Count == 0)
        {
            return;
        }

        foreach (var v in _flightBalls.Values)
        {
            if (v?.GameObject == null || !v.HasReceivedPosition)
            {
                continue;
            }

            var dt = Time.time - v.LastUpdateTime;
            if (dt > 0.25f)
            {
                dt = 0.25f;
            }

            var predicted = v.TargetPos + v.Velocity * dt;
            predicted.y += -9.81f * dt * dt * 0.5f;

            var current = (Vector2)v.GameObject.transform.position;
            var t = 1f - Mathf.Exp(-PositionSmoothRate * Time.deltaTime);
            var lerped = Vector2.Lerp(current, predicted, t);
            v.GameObject.transform.position = new Vector3(lerped.x, lerped.y, -1f);
        }
    }

    // =========================================================================
    // AIMING BALL CREATION
    // =========================================================================

    private void EnsureAimingBall()
    {
        if (_aimingBall != null)
        {
            return;
        }

        _aimingBall = new GameObject("ClientBall_Aiming")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        DontDestroyOnLoad(_aimingBall);
        _aimingRenderer = _aimingBall.AddComponent<SpriteRenderer>();
        _aimingRenderer.sortingOrder = 100;

        try
        {
            var anyRenderer = Object.FindObjectOfType<SpriteRenderer>();
            if (anyRenderer?.sharedMaterial != null)
            {
                _aimingRenderer.material = anyRenderer.sharedMaterial;
                _aimingRenderer.sortingLayerID = anyRenderer.sortingLayerID;
            }
        }
        catch { }

        _aimingRenderer.sprite = CreateCircleSprite();
        _aimingRenderer.color = Color.white;
        _aimingBall.transform.localScale = Vector3.one * 0.5f;
        _aimingBall.SetActive(false);
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
                var dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                tex.SetPixel(x, y, dist <= radius ? Color.white : Color.clear);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
