namespace Multipeglin.UI;

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders persistent damage preview text above enemies during coop battles.
/// Shows each player's accumulating damage above their targeted enemy as pegs
/// are hit, persisting across turns so all players' pending damage is visible
/// before the attack phase resolves.
/// </summary>
public sealed class PendingDamageOverlay : MonoBehaviour
{
    private static PendingDamageOverlay _instance;
    public static PendingDamageOverlay Instance => _instance;

    private GameObject _canvasObj;
    private Canvas _canvas;
    private RectTransform _canvasRect;

    /// <summary>Per-slot player damage state.</summary>
    private readonly Dictionary<int, PlayerDamageInfo> _playerData = new();

    /// <summary>Per-enemy rendered panels keyed by enemy GUID.</summary>
    private readonly Dictionary<string, EnemyPanel> _panels = new();

    private static TMP_FontAsset _gameFont;
    private static bool _gameFontSearched;

    private class PlayerDamageInfo
    {
        public string PlayerName;
        public long Damage;
        public string TargetEnemyGuid;
        public bool IsAoE;
    }

    private class EnemyPanel
    {
        public GameObject Root;
        public TextMeshProUGUI Label;
    }

    void Awake()
    {
        _instance = this;
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
        DestroyCanvas();
    }

    void LateUpdate()
    {
        if (_panels.Count == 0) return;
        RepositionPanels();
    }

    // =====================================================================
    // Public API
    // =====================================================================

    /// <summary>
    /// Update a player's pending damage entry. Called on each peg hit
    /// with the running damage total.
    /// </summary>
    public static void SetPlayerDamage(int slotIndex, string playerName, long damage,
        string targetEnemyGuid, bool isAoE)
    {
        if (_instance == null) return;
        _instance.SetPlayerDamageInternal(slotIndex, playerName, damage, targetEnemyGuid, isAoE);
    }

    /// <summary>Clear all overlay panels (e.g. after attack resolves).</summary>
    public static void ClearAll()
    {
        if (_instance == null) return;
        _instance.ClearAllInternal();
    }

    // =====================================================================
    // Internal implementation
    // =====================================================================

    private void SetPlayerDamageInternal(int slotIndex, string playerName, long damage,
        string targetGuid, bool isAoE)
    {
        _playerData[slotIndex] = new PlayerDamageInfo
        {
            PlayerName = playerName,
            Damage = damage,
            TargetEnemyGuid = targetGuid,
            IsAoE = isAoE,
        };

        RebuildPanels();
    }

    private void ClearAllInternal()
    {
        _playerData.Clear();
        foreach (var panel in _panels.Values)
        {
            if (panel.Root != null) Destroy(panel.Root);
        }
        _panels.Clear();
    }

    /// <summary>
    /// Rebuild text panels based on current player data.
    /// For each enemy, compute which players' damage applies, then create/update labels.
    /// </summary>
    private void RebuildPanels()
    {
        EnsureCanvas();

        // Collect enemy GUIDs → summed damage across all players
        var enemyTotals = new Dictionary<string, long>();

        // Resolve EnemyIdentifier + EnemyManager for GUID lookups
        Utility.EnemyIdentifier enemyId = null;
        EnemyManager em = null;
        try
        {
            var services = MultiplayerPlugin.Services;
            services?.TryResolve(out enemyId);
            em = FindObjectOfType<EnemyManager>();
        }
        catch { }

        foreach (var kvp in _playerData)
        {
            var data = kvp.Value;
            if (data.Damage <= 0) continue;

            if (data.IsAoE && em != null && enemyId != null)
            {
                // AoE: add to every living enemy
                foreach (var enemy in em.Enemies)
                {
                    if (enemy == null || enemy.CurrentHealth <= 0f) continue;
                    var guid = enemyId.GetGuid(enemy);
                    if (string.IsNullOrEmpty(guid)) continue;
                    enemyTotals.TryGetValue(guid, out var existing);
                    enemyTotals[guid] = existing + data.Damage;
                }
            }
            else if (!string.IsNullOrEmpty(data.TargetEnemyGuid))
            {
                var guid = data.TargetEnemyGuid;
                enemyTotals.TryGetValue(guid, out var existing);
                enemyTotals[guid] = existing + data.Damage;
            }
        }

        // Remove panels for enemies no longer targeted
        var toRemove = new List<string>();
        foreach (var guid in _panels.Keys)
        {
            if (!enemyTotals.ContainsKey(guid))
                toRemove.Add(guid);
        }
        foreach (var guid in toRemove)
        {
            if (_panels[guid].Root != null) Destroy(_panels[guid].Root);
            _panels.Remove(guid);
        }

        // Create/update one label per enemy with summed damage
        foreach (var kvp in enemyTotals)
        {
            var guid = kvp.Key;
            var totalDmg = kvp.Value;

            if (!_panels.TryGetValue(guid, out var panel))
            {
                panel = new EnemyPanel { Root = new GameObject($"PendingDmg_{guid}") };
                panel.Root.transform.SetParent(_canvasObj.transform, false);
                panel.Root.AddComponent<RectTransform>();
                panel.Label = CreateLabel(panel.Root.transform);
                _panels[guid] = panel;
            }

            panel.Label.text = $"-{DamageCountDisplay.FormatDamageNumberAsString(totalDmg)}";
        }
    }

    private void RepositionPanels()
    {
        var cam = Camera.main;
        if (cam == null) return;

        Utility.EnemyIdentifier enemyId = null;
        try
        {
            MultiplayerPlugin.Services?.TryResolve(out enemyId);
        }
        catch { }

        if (enemyId == null) return;

        foreach (var kvp in _panels)
        {
            var panel = kvp.Value;
            if (panel.Root == null) continue;

            var enemy = enemyId.Find(kvp.Key);
            if (enemy == null || enemy.CurrentHealth <= 0f)
            {
                panel.Root.SetActive(false);
                continue;
            }

            var worldPos = enemy.transform.position + new Vector3(0f, 1.8f, 0f);
            var screenPos = cam.WorldToScreenPoint(worldPos);
            if (screenPos.z < 0)
            {
                panel.Root.SetActive(false);
                continue;
            }

            if (!panel.Root.activeSelf) panel.Root.SetActive(true);

            var rect = panel.Root.GetComponent<RectTransform>();
            if (rect != null && _canvasRect != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, screenPos, null, out var localPoint);
                rect.localPosition = localPoint;
            }
        }
    }

    // =====================================================================
    // UI helpers
    // =====================================================================

    private void EnsureCanvas()
    {
        if (_canvasObj != null) return;
        _canvasObj = new GameObject("PendingDamageOverlayCanvas");
        DontDestroyOnLoad(_canvasObj);
        _canvas = _canvasObj.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 9500; // above CoopPlayerVisuals (9000)
        var scaler = _canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasRect = _canvasObj.GetComponent<RectTransform>();
    }

    private TextMeshProUGUI CreateLabel(Transform parent)
    {
        var obj = new GameObject("Label_Damage");
        obj.transform.SetParent(parent, false);
        var tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 104;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 0.2f, 0.2f);
        tmp.outlineWidth = 0.35f;
        tmp.outlineColor = Color.black;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;

        var font = GetGameFont();
        if (font != null) tmp.font = font;

        var rect = tmp.rectTransform;
        rect.sizeDelta = new Vector2(600, 120);
        rect.pivot = new Vector2(0.5f, 1f); // anchor at top-center

        return tmp;
    }

private static TMP_FontAsset GetGameFont()
    {
        if (_gameFontSearched) return _gameFont;
        _gameFontSearched = true;
        try
        {
            foreach (var tmp in FindObjectsOfType<TextMeshProUGUI>())
                if (tmp.font != null) { _gameFont = tmp.font; break; }
        }
        catch { }
        return _gameFont;
    }

    private void DestroyCanvas()
    {
        ClearAllInternal();
        if (_canvasObj != null) Destroy(_canvasObj);
        _canvasObj = null;
        _canvas = null;
        _canvasRect = null;
    }
}
