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

    public bool IsAiming => _isAiming;

    public GameObject AimingBall => _aimingBall;

    public SpriteRenderer AimingRenderer => _aimingRenderer;

    // --- Aiming visual ---
    private GameObject _aimingBall;
    private SpriteRenderer _aimingRenderer;
    private Vector3 _spawnPos;
    private Vector2 _aimDirection;
    private bool _isAiming;
    private string _currentOrbName;

    // --- Flight visuals, one per host-assigned GUID ---
    private struct HistoryEntry
    {
        public float Timestamp;     // local Time.time when the snapshot for this entry arrived
        public Vector2 Pos;
        public float ScaleX;
        public float ScaleY;
    }

    private class FlightVisual
    {
        public GameObject GameObject;
        public SpriteRenderer Renderer;
        public TrailRenderer Trail;
        public string OrbName;

        // Sorted ASC by Timestamp. Update() interpolates between two entries
        // straddling (Time.time - RenderDelay), so the client always plays
        // back motion that already happened on the host instead of dead-
        // reckoning ahead. ~10 Hz host snapshots × 200 ms delay → 2 entries
        // always available to lerp between, which absorbs latency jitter.
        public readonly List<HistoryEntry> History = new List<HistoryEntry>();

        // True once the host stops including this GUID in snapshots. The
        // visual keeps replaying buffered history (so it doesn't pop out of
        // existence 200 ms early) and is destroyed when render-time passes
        // the final entry's timestamp.
        public bool Departing;
    }

    private readonly Dictionary<string, FlightVisual> _flightBalls = new Dictionary<string, FlightVisual>();

    // Render the client visual this many seconds behind real time, scaled by
    // observed RTT. With host snapshots at 20 Hz (50 ms cadence):
    //   RTT ≤ 100 ms  → 200 ms (2-entry buffer, smooth on a clean LAN)
    //   100 < RTT ≤ 200 → 500 ms (10-entry buffer, absorbs jitter)
    //   RTT > 200 ms  → 1000 ms (20-entry buffer, survives stalls)
    // The buffer always covers RenderDelay + a small grace window so we can
    // always lerp between two known host states even when packets bunch.
    private float _currentRenderDelay = 0.2f;
    private float _currentHistoryRetention = 0.5f;
    private const float HistoryGrace = 0.3f;
    private float _nextRttPollAt;
    private const float RttPollInterval = 1.0f;

    private void Awake() { Instance = this; }

    private void OnDestroy()
    {
        if (Instance == this)
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
        _currentOrbName = orbName?.Replace("(Clone)", string.Empty).Trim();
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

        var now = Time.time;
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

                // If this GUID had previously departed and is now back (very
                // rare — host destroyed and recreated under the same GUID),
                // clear the buffer so we don't lerp across the gap.
                if (v.Departing)
                {
                    v.Departing = false;
                    v.History.Clear();
                    v.Trail?.Clear();
                }

                v.History.Add(new HistoryEntry
                {
                    Timestamp = now,
                    Pos = new Vector2(entry.PosX, entry.PosY),
                    ScaleX = entry.ScaleX,
                    ScaleY = entry.ScaleY,
                });
            }
        }

        // Mark visuals not in this tick as departing. They keep replaying
        // their buffered history for ~RenderDelay more so the visual's last
        // motion lands at the host-authoritative final position before it
        // disappears (no teleport, no premature pop).
        foreach (var kvp in _flightBalls)
        {
            if (!seen.Contains(kvp.Key))
            {
                kvp.Value.Departing = true;
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
        };

        ApplyOrbSprite(sr, go, entry.OrbName, scaleFactor: 1f, wantTrail: true);
        // ApplyOrbSprite may have added a TrailRenderer.
        v.Trail = go.GetComponent<TrailRenderer>();

        // Place the visual at the entry's position so the spawn frame draws
        // it correctly — Update() will start interpolating once the second
        // history entry arrives (after one snapshot interval).
        go.transform.position = new Vector3(entry.PosX, entry.PosY, -1f);
        go.transform.localScale = new Vector3(entry.ScaleX, entry.ScaleY, 1f);
        v.Trail?.Clear();
        return v;
    }

    // =========================================================================
    // SHARED SPRITE LOOKUP
    // =========================================================================

    /// <summary>
    /// Resolve the orb prefab by name and copy its sprite / material / sorting
    /// layer to the given renderer. Optionally mirror its trail settings.
    /// </summary>
    private void ApplyOrbSprite(
        SpriteRenderer target,
        GameObject targetGO,
        string orbName,
        float scaleFactor,
        bool wantTrail,
        TrailRenderer existingTrail = null)
    {
        if (target == null)
        {
            return;
        }

        try
        {
            var cleanName = orbName?.Replace("(Clone)", string.Empty).Trim();
            var orbGo = FindOrbPrefab(cleanName);

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
        catch
        {
        }
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
                if (orb != null && orb.name.Replace("(Clone)", string.Empty).Trim() == cleanName)
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

        UpdateRenderDelayFromRtt();

        var renderTime = Time.time - _currentRenderDelay;
        var prunedBefore = renderTime - _currentHistoryRetention;
        List<string> toDestroy = null;

        foreach (var kvp in _flightBalls)
        {
            var v = kvp.Value;
            if (v?.GameObject == null)
            {
                continue;
            }

            // Drop history older than the retention window — but always keep at
            // least one entry so a visual stuck waiting for renderTime to catch
            // up to its first snapshot has something to anchor to.
            while (v.History.Count > 1 && v.History[0].Timestamp < prunedBefore
                   && v.History[1].Timestamp < prunedBefore)
            {
                v.History.RemoveAt(0);
            }

            if (v.History.Count == 0)
            {
                if (v.Departing)
                {
                    (toDestroy ??= new List<string>()).Add(kvp.Key);
                }

                continue;
            }

            HistoryEntry rendered;
            var last = v.History[v.History.Count - 1];

            if (renderTime <= v.History[0].Timestamp)
            {
                // Render-time hasn't reached the buffer yet (just spawned —
                // sit at the first known host position until the delay fills).
                rendered = v.History[0];
            }
            else if (renderTime >= last.Timestamp)
            {
                // No future entry to interpolate toward. Snap to the most
                // recent host-authoritative state — this is the convergence
                // point that prevents drift after a ball stops updating.
                rendered = last;

                // Departing balls hold here for one extra renderDelay window
                // (covered by HistoryRetention pruning) before being cleaned
                // up so the final motion lands cleanly at the last position.
                if (v.Departing && renderTime > last.Timestamp + 0.05f)
                {
                    (toDestroy ??= new List<string>()).Add(kvp.Key);
                }
            }
            else
            {
                // Bracket renderTime between two history entries and lerp.
                var i = 1;
                while (i < v.History.Count && v.History[i].Timestamp < renderTime)
                {
                    i++;
                }

                var a = v.History[i - 1];
                var b = v.History[i];
                var span = b.Timestamp - a.Timestamp;
                var t = span > 1e-5f ? (renderTime - a.Timestamp) / span : 0f;
                rendered = new HistoryEntry
                {
                    Pos = Vector2.Lerp(a.Pos, b.Pos, t),
                    ScaleX = Mathf.Lerp(a.ScaleX, b.ScaleX, t),
                    ScaleY = Mathf.Lerp(a.ScaleY, b.ScaleY, t),
                };
            }

            v.GameObject.transform.position = new Vector3(rendered.Pos.x, rendered.Pos.y, -1f);
            if (rendered.ScaleX > 0f && rendered.ScaleY > 0f)
            {
                v.GameObject.transform.localScale = new Vector3(rendered.ScaleX, rendered.ScaleY, 1f);
            }
        }

        if (toDestroy != null)
        {
            foreach (var guid in toDestroy)
            {
                if (_flightBalls.TryGetValue(guid, out var v) && v.GameObject != null)
                {
                    Destroy(v.GameObject);
                }

                _flightBalls.Remove(guid);
            }
        }
    }

    // =========================================================================
    // ADAPTIVE RENDER DELAY
    // =========================================================================

    /// <summary>
    /// Tier the render delay against the LiteNetLib peer RTT. We poll once per
    /// second — RTT changes slowly relative to per-frame Update() and reading
    /// the DI container every frame would be wasteful. Steam transport doesn't
    /// expose RTT today, so we get the LiteNet path's number; a Steam-only
    /// session keeps the default 200 ms (still fine for typical Steam P2P).
    /// </summary>
    private void UpdateRenderDelayFromRtt()
    {
        var now = Time.time;
        if (now < _nextRttPollAt)
        {
            return;
        }

        _nextRttPollAt = now + RttPollInterval;

        var services = MultiplayerPlugin.Services;
        if (services == null
            || !services.TryResolve<Network.IRttProvider>(out var rtt))
        {
            return;
        }

        var rttMs = rtt.CurrentRttMs;
        if (rttMs <= 0)
        {
            return; // no measurement yet — keep current delay
        }

        float delay;
        if (rttMs <= 100)
        {
            delay = 0.2f;
        }
        else if (rttMs <= 200)
        {
            delay = 0.5f;
        }
        else
        {
            delay = 1.0f;
        }

        if (Mathf.Abs(delay - _currentRenderDelay) < 0.001f)
        {
            return;
        }

        _currentRenderDelay = delay;
        _currentHistoryRetention = delay + HistoryGrace;
        MultiplayerPlugin.Logger?.LogInfo(
            $"[ClientBallRenderer] RTT={rttMs}ms → renderDelay={delay:F2}s, retention={_currentHistoryRetention:F2}s");
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
        catch
        {
        }

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
