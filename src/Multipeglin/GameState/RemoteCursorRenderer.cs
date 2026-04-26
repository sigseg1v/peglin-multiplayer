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
        public Vector2 TargetWorld;
        public Vector2 ScreenPos;
        public float LastUpdateTime;
        public bool Seeded;
    }

    private readonly Dictionary<int, CursorVisual> _visuals = new Dictionary<int, CursorVisual>();
    private Canvas _canvas;
    private IMultiplayerMode _mode;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        MultiplayerPlugin.Services?.TryResolve(out _mode);

        EnsureCanvas();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
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
        if (v.Root != null && !v.Root.activeSelf)
        {
            v.Root.SetActive(true);
        }
    }

    /// <summary>Clear all remote cursors (e.g. on disconnect).</summary>
    public void ClearAll()
    {
        foreach (var v in _visuals.Values)
        {
            if (v?.Root != null)
            {
                Destroy(v.Root);
            }
        }

        _visuals.Clear();
    }

    private void Update()
    {
        // No multiplayer → no cursors.
        var active = _mode != null && (_mode.IsHosting || _mode.IsSpectating);
        if (!active)
        {
            if (_visuals.Count > 0)
            {
                ClearAll();
            }

            return;
        }

        var cam = Camera.main;
        var now = Time.unscaledTime;
        var t = 1f - Mathf.Exp(-SmoothRate * Time.unscaledDeltaTime);

        foreach (var kv in _visuals)
        {
            var v = kv.Value;
            if (v?.Root == null)
            {
                continue;
            }

            // Hide stale.
            if (now - v.LastUpdateTime > StaleTimeout)
            {
                if (v.Root.activeSelf)
                {
                    v.Root.SetActive(false);
                }

                continue;
            }

            if (cam == null)
            {
                continue;
            }

            var targetScreen = cam.WorldToScreenPoint(new Vector3(v.TargetWorld.x, v.TargetWorld.y, 0f));
            // Behind the camera (z<0) → hide.
            if (targetScreen.z < 0f)
            {
                v.Root.SetActive(false);
                continue;
            }

            if (!v.Root.activeSelf)
            {
                v.Root.SetActive(true);
            }

            var target2D = new Vector2(targetScreen.x, targetScreen.y);
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
        if (_canvas != null)
        {
            return;
        }

        var go = new GameObject("RemoteCursorCanvas")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
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

        var root = new GameObject($"RemoteCursor_Slot{slotIndex}");
        root.transform.SetParent(_canvas.transform, worldPositionStays: false);

        var rect = root.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(32f, 32f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        var color = WithAlpha(SlotColor(slotIndex), Alpha);

        // Arrow glyph as the cursor — top-left tip aligned with the rect's top-left.
        var iconGo = new GameObject("Icon");
        iconGo.transform.SetParent(root.transform, worldPositionStays: false);
        var iconRect = iconGo.AddComponent<RectTransform>();
        iconRect.sizeDelta = new Vector2(40f, 40f);
        iconRect.pivot = new Vector2(0f, 1f);
        iconRect.anchoredPosition = Vector2.zero;
        var icon = iconGo.AddComponent<TextMeshProUGUI>();
        icon.text = "\u2196"; // ↖ NORTH WEST ARROW
        icon.fontSize = 36f;
        icon.fontStyle = FontStyles.Bold;
        icon.alignment = TextAlignmentOptions.TopLeft;
        icon.raycastTarget = false;
        icon.color = color;

        // Number label below-left of the cursor tip.
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
        label.color = color;
        label.outlineWidth = 0.2f;
        label.outlineColor = new Color32(0, 0, 0, (byte)(255 * Alpha));

        return new CursorVisual { Root = root, Rect = rect };
    }

    private static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }

    private static Color SlotColor(int slot)
    {
        // Stable, visually distinct palette keyed on slot index.
        switch (slot % 6)
        {
            case 0:
                return new Color(1.00f, 1.00f, 1.00f);
            case 1:
                return new Color(0.35f, 0.85f, 1.00f);
            case 2:
                return new Color(1.00f, 0.70f, 0.30f);
            case 3:
                return new Color(0.55f, 1.00f, 0.55f);
            case 4:
                return new Color(1.00f, 0.55f, 0.85f);
            default:
                return new Color(1.00f, 1.00f, 0.40f);
        }
    }
}
