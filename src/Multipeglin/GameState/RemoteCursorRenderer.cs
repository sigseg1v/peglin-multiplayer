using System.Collections.Generic;
using Multipeglin.Multiplayer;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Multipeglin.GameState;

/// <summary>
/// Renders a translucent cursor with a "{slot+1}" label for each remote
/// player. World-space target position is stored per slot; each frame the
/// visible screen-space position is smoothly lerped toward the host camera's
/// projection of that target.
/// </summary>
public class RemoteCursorRenderer : MonoBehaviour
{
    public static RemoteCursorRenderer Instance { get; private set; }

    // ~15 gives a ~65ms time-constant — smooth without feeling laggy when the
    // remote peer is moving quickly.
    private const float SmoothRate = 15f;
    // After this long without an update from a slot, hide its cursor so we
    // don't keep stale indicators around on disconnect / alt-tab / etc.
    private const float StaleTimeout = 5f;
    private const float Alpha = 0.5f;

    private class CursorVisual
    {
        public GameObject Root;
        public RectTransform Rect;
        public Image Icon;
        public TextMeshProUGUI Label;
        public Vector2 TargetWorld;
        public Vector2 ScreenPos;
        public float LastUpdateTime;
        public bool Seeded;
    }

    private readonly Dictionary<int, CursorVisual> _visuals = new Dictionary<int, CursorVisual>();
    private Canvas _canvas;
    private Sprite _cursorSprite;
    private IMultiplayerMode _mode;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (MultiplayerPlugin.Services != null)
            MultiplayerPlugin.Services.TryResolve(out _mode);
        EnsureCanvas();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Called by the network handler when a peer's cursor moves.</summary>
    public void SetRemoteCursor(int slotIndex, float worldX, float worldY)
    {
        if (!_visuals.TryGetValue(slotIndex, out var v))
        {
            v = CreateVisual(slotIndex);
            _visuals[slotIndex] = v;
        }
        v.TargetWorld = new Vector2(worldX, worldY);
        v.LastUpdateTime = Time.unscaledTime;
        if (v.Root != null && !v.Root.activeSelf) v.Root.SetActive(true);
    }

    /// <summary>Clear all remote cursors (e.g. on disconnect).</summary>
    public void ClearAll()
    {
        foreach (var v in _visuals.Values)
        {
            if (v?.Root != null) Destroy(v.Root);
        }
        _visuals.Clear();
    }

    private void Update()
    {
        // No multiplayer → no cursors.
        bool active = _mode != null && (_mode.IsHosting || _mode.IsSpectating);
        if (!active)
        {
            if (_visuals.Count > 0) ClearAll();
            return;
        }

        var cam = Camera.main;
        float now = Time.unscaledTime;
        float t = 1f - Mathf.Exp(-SmoothRate * Time.unscaledDeltaTime);

        foreach (var kv in _visuals)
        {
            var v = kv.Value;
            if (v?.Root == null) continue;

            // Hide stale.
            if (now - v.LastUpdateTime > StaleTimeout)
            {
                if (v.Root.activeSelf) v.Root.SetActive(false);
                continue;
            }

            if (cam == null) continue;

            var targetScreen = cam.WorldToScreenPoint(new Vector3(v.TargetWorld.x, v.TargetWorld.y, 0f));
            // Behind the camera (z<0) → hide.
            if (targetScreen.z < 0f) { v.Root.SetActive(false); continue; }
            if (!v.Root.activeSelf) v.Root.SetActive(true);

            Vector2 target2D = new Vector2(targetScreen.x, targetScreen.y);
            if (!v.Seeded)
            {
                v.ScreenPos = target2D;
                v.Seeded = true;
            }
            else
            {
                v.ScreenPos = Vector2.Lerp(v.ScreenPos, target2D, t);
            }

            v.Rect.position = new Vector3(v.ScreenPos.x, v.ScreenPos.y, 0f);
        }
    }

    private void EnsureCanvas()
    {
        if (_canvas != null) return;
        var go = new GameObject("RemoteCursorCanvas");
        go.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(go);
        go.transform.SetParent(transform, worldPositionStays: false);

        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Draw above everything else in-game. Game canvases typically use low
        // values; 32000 leaves room for anything that wants to be higher.
        _canvas.sortingOrder = 32000;

        go.AddComponent<CanvasScaler>();
        go.AddComponent<GraphicRaycaster>().enabled = false;
    }

    private CursorVisual CreateVisual(int slotIndex)
    {
        EnsureCanvas();
        if (_cursorSprite == null) _cursorSprite = BuildCursorSprite();

        var root = new GameObject($"RemoteCursor_Slot{slotIndex}");
        root.transform.SetParent(_canvas.transform, worldPositionStays: false);

        var rect = root.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(32f, 32f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        // Icon
        var iconGo = new GameObject("Icon");
        iconGo.transform.SetParent(root.transform, worldPositionStays: false);
        var iconRect = iconGo.AddComponent<RectTransform>();
        iconRect.sizeDelta = new Vector2(24f, 32f);
        // Pivot at top-left so the sprite "tip" lands at the cursor position.
        iconRect.pivot = new Vector2(0f, 1f);
        iconRect.anchoredPosition = Vector2.zero;
        var icon = iconGo.AddComponent<Image>();
        icon.sprite = _cursorSprite;
        icon.raycastTarget = false;
        icon.color = WithAlpha(SlotColor(slotIndex), Alpha);

        // Number label above-left of the cursor tip.
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(root.transform, worldPositionStays: false);
        var labelRect = labelGo.AddComponent<RectTransform>();
        labelRect.sizeDelta = new Vector2(40f, 24f);
        labelRect.pivot = new Vector2(1f, 0.5f);
        labelRect.anchoredPosition = new Vector2(-4f, -17f);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = (slotIndex + 1).ToString();
        label.fontSize = 22f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.MidlineRight;
        label.raycastTarget = false;
        label.color = WithAlpha(SlotColor(slotIndex), Alpha);
        // Outline via a shadow so the number stays readable against busy scenes.
        label.enableVertexGradient = false;
        label.outlineWidth = 0.2f;
        label.outlineColor = new Color32(0, 0, 0, (byte)(255 * Alpha));

        return new CursorVisual
        {
            Root = root,
            Rect = rect,
            Icon = icon,
            Label = label,
        };
    }

    private static Color WithAlpha(Color c, float a) { c.a = a; return c; }

    private static Color SlotColor(int slot)
    {
        // Stable, visually distinct palette keyed on slot index.
        switch (slot % 6)
        {
            case 0: return new Color(1.00f, 1.00f, 1.00f);
            case 1: return new Color(0.35f, 0.85f, 1.00f);
            case 2: return new Color(1.00f, 0.70f, 0.30f);
            case 3: return new Color(0.55f, 1.00f, 0.55f);
            case 4: return new Color(1.00f, 0.55f, 0.85f);
            default: return new Color(1.00f, 1.00f, 0.40f);
        }
    }

    // Narrow classic-cursor-shaped triangle, pointing up-left.
    // Large texture + 4x supersampled coverage = smooth edges, no chunky stairs.
    // Vertices (in normalized [0,1] coords, y-up with (0,1) = top):
    //   A = (0.00, 1.00)  tip
    //   B = (0.10, 0.00)  tail bottom
    //   C = (0.80, 0.25)  side flare
    private static Sprite BuildCursorSprite()
    {
        const int size = 64;
        const int ss = 4; // 4x4 supersampling per pixel for anti-aliasing

        var tipNorm = new Vector2(0.00f, 1.00f);
        var tailNorm = new Vector2(0.10f, 0.00f);
        var flareNorm = new Vector2(0.80f, 0.25f);

        var a = new Vector2(tipNorm.x * (size - 1), tipNorm.y * (size - 1));
        var b = new Vector2(tailNorm.x * (size - 1), tailNorm.y * (size - 1));
        var c = new Vector2(flareNorm.x * (size - 1), flareNorm.y * (size - 1));

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var clear = new Color32(0, 0, 0, 0);

        float invSs = 1f / ss;
        float halfInvSs = invSs * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int hits = 0;
            for (int sy = 0; sy < ss; sy++)
            for (int sx = 0; sx < ss; sx++)
            {
                var p = new Vector2(x + halfInvSs + sx * invSs, y + halfInvSs + sy * invSs);
                if (PointInTriangle(p, a, b, c)) hits++;
            }
            if (hits == 0) { tex.SetPixel(x, y, clear); continue; }
            byte alpha = (byte)((hits * 255) / (ss * ss));
            tex.SetPixel(x, y, new Color32(255, 255, 255, alpha));
        }

        tex.Apply();
        // Pivot at (0, 1) = top-left so positioning the rect at the target
        // screen point lands the tip on the cursor position.
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0f, 1f), 100f);
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Sign(p, a, b);
        float d2 = Sign(p, b, c);
        float d3 = Sign(p, c, a);
        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
        => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
}
