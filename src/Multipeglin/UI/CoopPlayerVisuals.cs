using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Multipeglin.DI;
using Multipeglin.GameState;
using Multipeglin.GameState.Snapshots;
using Multipeglin.Multiplayer;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Multipeglin.UI;

/// <summary>
/// Manages player sprites and HUD in battle scenes for co-op multiplayer.
/// For each co-op player, clones the player sprite with an offset and shows
/// separate name, HP, and turn-arrow labels as screen-space overlays.
/// </summary>
public class CoopPlayerVisuals : MonoBehaviour
{
    private static ManualLogSource Log => MultiplayerPlugin.Logger;

    /// <summary>Latest player summaries from the host heartbeat.</summary>
    public static List<CoopPlayerSummary> LatestPlayerSummaries { get; set; }

    /// <summary>Active player slot index from the host heartbeat.</summary>
    public static int LatestActiveSlot { get; set; } = -1;

    // Per-player visual data — name, HP, arrow, and status icons are separate panels
    private class PlayerVisual
    {
        public int SlotIndex;
        public GameObject SpriteClone; // null for slot 0 (host uses real player)
        public GameObject NamePanel;
        public TextMeshProUGUI NameText;
        public GameObject HpPanel;
        public TextMeshProUGUI HpText;
        public GameObject ArrowPanel;
        public TextMeshProUGUI ArrowText;
        public GameObject StatusIconContainer; // horizontal row of native-style icons
        public Dictionary<int, StatusIconEntry> StatusIcons = new Dictionary<int, StatusIconEntry>();
    }

    private class StatusIconEntry
    {
        public GameObject Root;
        public Image IconImage;
        public TextMeshProUGUI IntensityText;
        public StatusIconHoverHandler Hover;
    }

    /// <summary>
    /// Pointer hover handler that shows the native StatusEffect tooltip on
    /// mouse-over and hides it on mouse-exit. The world anchor position is
    /// updated every frame by UpdateStatusIcons so the tooltip appears next
    /// to the correct character (host vs. clone).
    /// </summary>
    internal class StatusIconHoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Battle.StatusEffects.StatusEffectType EffectType;
        public Vector3 WorldAnchor;
        private bool _tooltipShowing;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_tooltipShowing)
            {
                return;
            }

            var mgr = TooltipManager.Instance;
            if (mgr == null)
            {
                return;
            }

            try
            {
                mgr.ShowTooltipStatusEffect(EffectType, WorldAnchor, new Vector3(1f, -1f), isOnPlayer: true);
                _tooltipShowing = true;
            }
            catch { }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_tooltipShowing)
            {
                return;
            }

            try
            { TooltipManager.Instance?.HideTooltip(); }
            catch { }

            _tooltipShowing = false;
        }

        private void OnDisable()
        {
            if (_tooltipShowing)
            {
                try
                { TooltipManager.Instance?.HideTooltip(); }
                catch { }

                _tooltipShowing = false;
            }
        }
    }

    // Cache the game's StatusEffectData ScriptableObject for icon sprites
    private static Battle.StatusEffects.StatusEffectData _statusEffectData;
    private static bool _statusEffectDataSearched;

    private readonly List<PlayerVisual> _visuals = new List<PlayerVisual>();
    private bool _inBattle;
    private string _lastScene = "";
    private GameObject _playerRef;
    // Labels for slot 0 (host) which always occupies the "main" position
    // (_playerRef). Clones (in _visuals) are used for all slots > 0.
    private PlayerVisual _hostLabel;
    private bool _updateErrorLogged; // throttle: log the NRE once, not every frame

    // Perf: cache expensive FindObjectOfType lookups, invalidated on scene change.
    private Battle.StatusEffects.PlayerStatusEffectController _cachedStatusCtrl;

    // Perf: throttle per-frame host summary build + native icon hide. Running
    // FindObjectOfType<PlayerStatusEffectController>() every frame was the
    // dominant source of client lag during non-host turns.
    private float _lastHeavyTickTime;
    private const float HeavyTickInterval = 0.2f; // 200ms

    // Perf: reusable scratch collections to avoid per-frame GC allocations in
    // UpdateStatusIcons (called every frame per player).
    private readonly HashSet<int> _activeTypesScratch = new HashSet<int>();
    private readonly List<int> _removeScratch = new List<int>();

    // Shared screen-space overlay canvas
    private static GameObject _overlayCanvasObj;
    private static Canvas _overlayCanvas;
    private static RectTransform _overlayCanvasRect;

    // Cache the game's TMP font once
    private static TMP_FontAsset _gameFont;
    private static bool _gameFontSearched;
    private static TMP_FontAsset _numberFont;
    private static bool _numberFontSearched;

    private void Update()
    {
        try
        {
            var scene = SceneManager.GetActiveScene().name;

            if (scene != _lastScene)
            {
                _lastScene = scene;
                CleanupVisuals();
                _playerRef = null;
                _inBattle = false;
                _updateErrorLogged = false;
                _cachedStatusCtrl = null; // invalidate cache on scene change
                _lastHeavyTickTime = 0f;
            }

            var services = MultiplayerPlugin.Services;
            if (services == null)
            {
                return;
            }

            if (!services.TryResolve<IMultiplayerMode>(out var mode))
            {
                return;
            }

            if (!mode.IsHosting && !mode.IsSpectating)
            {
                return;
            }

            if (scene != "Battle")
            {
                if (_inBattle)
                { CleanupVisuals(); _inBattle = false; }

                return;
            }

            _inBattle = true;

            // Perf: the heavy work (FindObjectOfType for PlayerStatusEffectController,
            // building host summaries with allocations, hiding the native icon bar)
            // only needs to happen at UI-refresh rate, not every frame. Throttle to
            // ~5Hz. Per-frame label positioning/scaling below still runs every frame
            // so visuals remain smooth.
            var now = Time.unscaledTime;
            if (now - _lastHeavyTickTime >= HeavyTickInterval)
            {
                _lastHeavyTickTime = now;

                // In coop, the native StatusEffectIconManager draws icons above
                // the single real Player GameObject — but in coop the real Player
                // visually represents slot 0 and other slots are sprite clones.
                // Showing the active slot's effects above slot 0's visual is
                // wrong on the client (slot 1's effects appear over host) and
                // transiently wrong on host (during non-host turns). Hide the
                // native icon container and rely on the per-slot icons rendered
                // by this component.
                HideNativeStatusIcons();

                if (mode.IsHosting)
                {
                    BuildHostSummaries(services);
                }
            }

            var summaries = LatestPlayerSummaries;
            if (summaries == null || summaries.Count <= 1)
            {
                return;
            }

            // Re-find the player ref if it was destroyed (e.g. during attack animations)
            if (_playerRef == null)
            {
                _playerRef = GameObject.FindGameObjectWithTag("Player");
                if (_playerRef == null)
                {
                    return;
                }
            }

            // Camera.main can be null during transitions
            if (Camera.main == null)
            {
                return;
            }

            EnsureVisuals(summaries);
            UpdateVisuals(summaries);
            _updateErrorLogged = false; // reset throttle on success
        }
        catch (Exception ex)
        {
            if (!_updateErrorLogged)
            {
                Log?.LogWarning($"[CoopPlayerVisuals] Update error (will suppress repeats): {ex}");
                _updateErrorLogged = true;
            }
        }
    }

    private void BuildHostSummaries(IServiceContainer services)
    {
        try
        {
            if (!services.TryResolve<CoopStateManager>(out var coopState))
            {
                return;
            }

            if (coopState.TotalPlayerCount < 2)
            {
                return;
            }

            // Read live status effects from the singleton for the active player
            List<StatusEffectEntry> liveEffects = null;
            try
            {
                var statusCtrl = GetCachedStatusController();
                if (statusCtrl != null)
                {
                    var effectsList = HarmonyLib.AccessTools.Field(
                        typeof(Battle.StatusEffects.PlayerStatusEffectController), "_statusEffects")
                        ?.GetValue(statusCtrl) as System.Collections.IList;
                    if (effectsList != null && effectsList.Count > 0)
                    {
                        liveEffects = new List<StatusEffectEntry>();
                        foreach (var effect in effectsList)
                        {
                            var typeField = HarmonyLib.AccessTools.Field(effect.GetType(), "EffectType");
                            var intensityField = HarmonyLib.AccessTools.Field(effect.GetType(), "Intensity");
                            if (typeField == null)
                            {
                                continue;
                            }

                            var effectType = typeField.GetValue(effect);
                            var intensity = (int)(intensityField?.GetValue(effect) ?? 0);
                            if (intensity <= 0)
                            {
                                continue;
                            }

                            liveEffects.Add(new StatusEffectEntry
                            {
                                EffectType = (int)effectType,
                                EffectName = effectType.ToString(),
                                Intensity = intensity,
                            });
                        }
                    }
                }
            }
            catch { }

            // Read live health from the PlayerHealthController singleton for the
            // currently active slot. CoopPlayerState.CurrentHealth only refreshes
            // on SaveActivePlayerState() (end of turn), so without this the host's
            // own HP label lags until the turn ends.
            float liveHp = -1f, liveMaxHp = -1f;
            try
            {
                var hpCtrl = UnityEngine.Object.FindObjectOfType<global::Battle.PlayerHealthController>();
                if (hpCtrl != null)
                {
                    liveHp = hpCtrl.CurrentHealth;
                    liveMaxHp = hpCtrl.MaxHealth;
                }
            }
            catch { }

            var summaries = new List<CoopPlayerSummary>();
            foreach (var kvp in coopState.PlayerStates)
            {
                var ps = kvp.Value;
                var isActive = ps.SlotIndex == coopState.ActivePlayerSlot;
                var summaryHp = (isActive && liveHp >= 0f) ? liveHp : ps.CurrentHealth;
                var summaryMaxHp = (isActive && liveMaxHp > 0f) ? liveMaxHp : ps.MaxHealth;

                var summary = new CoopPlayerSummary
                {
                    SlotIndex = ps.SlotIndex,
                    PlayerName = ps.PlayerName,
                    ChosenClass = ps.ChosenClass,
                    CurrentHealth = summaryHp,
                    MaxHealth = summaryMaxHp,
                    Gold = ps.Gold,
                    HasShotThisRound = ps.HasShotThisRound,
                };

                // Active player: read live from singleton (includes buffs gained this turn)
                // Inactive players: read from saved CoopPlayerState
                if (isActive && liveEffects != null)
                {
                    summary.StatusEffects = liveEffects;
                }
                else if (ps.StatusEffects != null)
                {
                    foreach (var e in ps.StatusEffects)
                    {
                        summary.StatusEffects.Add(new StatusEffectEntry
                        {
                            EffectType = e.EffectType,
                            EffectName = ((Battle.StatusEffects.StatusEffectType)e.EffectType).ToString(),
                            Intensity = e.Intensity,
                        });
                    }
                }

                summaries.Add(summary);
            }

            LatestPlayerSummaries = summaries;
            LatestActiveSlot = coopState.ActivePlayerSlot;
        }
        catch (Exception ex)
        {
            Log?.LogError($"[CoopPlayerVisuals] BuildHostSummaries failed: {ex.Message}");
        }
    }

    private void OnDestroy() => CleanupVisuals();

    private const int HostSlot = 0;

    private void EnsureVisuals(List<CoopPlayerSummary> summaries)
    {
        var localSlot = GetLocalSlotIndex();

        CoopPlayerSummary hostSummary = null;
        foreach (var s in summaries)
        {
            if (s.SlotIndex == HostSlot)
            { hostSummary = s; break; }
        }

        // Remove stale clones. Slot 0 (host) never has a clone — it uses
        // _playerRef at the "main" position.
        for (var i = _visuals.Count - 1; i >= 0; i--)
        {
            var v = _visuals[i];
            var found = false;
            foreach (var s in summaries)
            {
                if (s.SlotIndex == v.SlotIndex)
                { found = true; break; }
            }

            if (!found || v.SpriteClone == null || v.SlotIndex == HostSlot)
            {
                DestroyVisual(v);
                _visuals.RemoveAt(i);
            }
        }

        // Host label — attaches to the real scene Player GameObject (_playerRef),
        // which occupies the rightmost "main" position. On the host that's already
        // the host's own character; on the client we override _playerRef's sprite
        // below so it also shows the host's class.
        if (_hostLabel != null && _hostLabel.NamePanel == null)
        {
            _hostLabel = null;
        }

        if (_hostLabel == null && _playerRef != null && hostSummary != null)
        {
            _hostLabel = CreatePlayerLabels(hostSummary, spriteClone: null);
        }

        // On client (localSlot != 0), override the main Player's sprite AND
        // animator controller so it displays the host's class. Setting the
        // sprite alone is not enough — the Animator on the Player GO plays
        // class-specific animation clips every frame that overwrite the
        // SpriteRenderer's sprite. Swapping the runtimeAnimatorController to
        // the host's class makes the animator drive the correct per-frame
        // sprites. Retry each Ensure tick so a transient lookup miss self-corrects.
        if (localSlot != HostSlot && hostSummary != null && _playerRef != null)
        {
            ApplyHostClassToPlayerRef(hostSummary.ChosenClass);
        }

        // Clones + labels for every slot > 0 (host is the only one that uses
        // _playerRef). This keeps slot 0 at the main position and every
        // higher-numbered slot fanning out to the left on every machine.
        foreach (var summary in summaries)
        {
            if (summary.SlotIndex == HostSlot)
            {
                continue;
            }

            var exists = false;
            foreach (var v in _visuals)
            {
                if (v.SlotIndex == summary.SlotIndex)
                { exists = true; break; }
            }

            if (exists)
            {
                continue;
            }

            var visual = CreatePlayerVisual(summary);
            if (visual != null)
            {
                _visuals.Add(visual);
            }
        }
    }

    private static int GetLocalSlotIndex()
    {
        try
        {
            return Events.Handlers.Coop.CoopSlotHelper.GetLocalSlotIndex(MultiplayerPlugin.Services);
        }
        catch
        {
            return -1;
        }
    }

    private void EnsureOverlayCanvas()
    {
        if (_overlayCanvasObj != null)
        {
            return;
        }

        _overlayCanvasObj = new GameObject("CoopPlayerLabelsOverlay");
        DontDestroyOnLoad(_overlayCanvasObj);
        _overlayCanvas = _overlayCanvasObj.AddComponent<Canvas>();
        _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _overlayCanvas.sortingOrder = 9000;
        var scaler = _overlayCanvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        // Raycaster is required so pointer events reach the status icon
        // hover handlers (for tooltip display on hover).
        _overlayCanvasObj.AddComponent<GraphicRaycaster>();
        _overlayCanvasRect = _overlayCanvasObj.GetComponent<RectTransform>();
    }

    private static TMP_FontAsset GetGameFont()
    {
        if (_gameFontSearched)
        {
            return _gameFont;
        }

        _gameFontSearched = true;
        try
        {
            foreach (var tmp in UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>())
            {
                if (tmp.font != null)
                { _gameFont = tmp.font; break; }
            }
        }
        catch { }

        return _gameFont;
    }

    /// <summary>
    /// Font used for status-effect intensity numbers. The generic game font picked
    /// up by GetGameFont is a thin display font that renders poorly for small
    /// digits above player heads. Use the same font the base game puts on enemy
    /// status icons: StatusEffectIcon._intensityText.font.
    /// </summary>
    private static TMP_FontAsset GetStatusEffectNumberFont()
    {
        if (_numberFontSearched)
        {
            return _numberFont ?? GetGameFont();
        }

        _numberFontSearched = true;
        try
        {
            var intensityField = HarmonyLib.AccessTools.Field(
                typeof(Battle.StatusEffects.StatusEffectIcon), "_intensityText");
            if (intensityField != null)
            {
                // Prefer a live enemy-side icon instance; fall back to the manager's prefab.
                foreach (var icon in Resources.FindObjectsOfTypeAll<Battle.StatusEffects.StatusEffectIcon>())
                {
                    if (icon == null)
                    {
                        continue;
                    }

                    var tmp = intensityField.GetValue(icon) as TextMeshProUGUI;
                    if (tmp?.font != null)
                    { _numberFont = tmp.font; break; }
                }
            }
        }
        catch { }

        return _numberFont ?? GetGameFont();
    }

    /// <summary>Format a name: max 14 chars, 7 per line, max 2 lines.</summary>
    private static string FormatName(string name, int slot)
    {
        if (string.IsNullOrEmpty(name))
        {
            name = $"P{slot}";
        }

        if (name.Length > 14)
        {
            name = name.Substring(0, 14);
        }

        if (name.Length > 7)
        {
            name = name.Substring(0, 7) + "\n" + name.Substring(7);
        }

        return name;
    }

    /// <summary>Create a single text panel (background + text).</summary>
    private GameObject CreateTextPanel(string goName, float width, float height,
        string text, float fontSize, Color textColor, Color bgColor,
        out TextMeshProUGUI tmpText)
    {
        EnsureOverlayCanvas();

        var panel = new GameObject(goName);
        panel.transform.SetParent(_overlayCanvasObj.transform, false);
        var panelRect = panel.AddComponent<RectTransform>();
        panelRect.sizeDelta = new Vector2(width, height);
        panelRect.pivot = new Vector2(0.5f, 0.5f);

        // Background
        var bgImg = panel.AddComponent<Image>();
        bgImg.color = bgColor;
        // Name/HP/arrow panels must not steal clicks from the game underneath.
        bgImg.raycastTarget = false;

        // Text
        var textObj = new GameObject("Text");
        textObj.transform.SetParent(panel.transform, false);
        tmpText = textObj.AddComponent<TextMeshProUGUI>();
        tmpText.text = text;
        tmpText.fontSize = fontSize;
        tmpText.fontStyle = FontStyles.Bold;
        var font = GetGameFont();
        if (font != null)
        {
            tmpText.font = font;
        }

        tmpText.alignment = TextAlignmentOptions.Center;
        tmpText.color = textColor;
        try
        { tmpText.outlineWidth = 0.3f; }
        catch { }

        try
        { tmpText.outlineColor = Color.black; }
        catch { }

        tmpText.enableWordWrapping = false;
        tmpText.overflowMode = TextOverflowModes.Overflow;
        tmpText.lineSpacing = -25f; // tighter line spacing for 2-line names
        tmpText.raycastTarget = false;

        var textRect = tmpText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return panel;
    }

    /// <summary>Create name, HP, and arrow panels for a player.</summary>
    private PlayerVisual CreatePlayerLabels(CoopPlayerSummary summary, GameObject spriteClone)
    {
        try
        {
            var nameColor = new Color(0.9f, 0.5f, 0.1f); // dark orange
            var hpColor = new Color(0.6f, 1f, 0.6f);      // green
            var arrowColor = new Color(1f, 0.9f, 0.3f);    // yellow
            var bgColor = new Color(0, 0, 0, 0.3f);

            var formattedName = FormatName(summary.PlayerName, summary.SlotIndex);
            var twoLines = formattedName.Contains("\n");

            var namePanel = CreateTextPanel(
                $"CoopName_Slot{summary.SlotIndex}",
                187, twoLines ? 80 : 50,
                formattedName, 45, nameColor, bgColor,
                out var nameText);

            var hpPanel = CreateTextPanel(
                $"CoopHP_Slot{summary.SlotIndex}",
                160, 45,
                $"{summary.CurrentHealth:F0}/{summary.MaxHealth:F0}",
                38, hpColor, bgColor,
                out var hpText);

            var arrowPanel = CreateTextPanel(
                $"CoopArrow_Slot{summary.SlotIndex}",
                80, 100,
                "\u25C0", 64, arrowColor, new Color(0, 0, 0, 0),
                out var arrowText);
            try
            { arrowText.outlineWidth = 0.4f; }
            catch { }

            arrowPanel.SetActive(false); // only visible for active player

            // Status icon container — horizontal row positioned above the name
            EnsureOverlayCanvas();
            var iconContainer = new GameObject($"CoopStatusIcons_Slot{summary.SlotIndex}");
            iconContainer.transform.SetParent(_overlayCanvasObj.transform, false);
            var containerRect = iconContainer.AddComponent<RectTransform>();
            containerRect.sizeDelta = new Vector2(380, 56);
            containerRect.pivot = new Vector2(0.5f, 0.5f);
            var layout = iconContainer.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            iconContainer.SetActive(false);

            return new PlayerVisual
            {
                SlotIndex = summary.SlotIndex,
                SpriteClone = spriteClone,
                NamePanel = namePanel,
                NameText = nameText,
                HpPanel = hpPanel,
                HpText = hpText,
                ArrowPanel = arrowPanel,
                ArrowText = arrowText,
                StatusIconContainer = iconContainer,
            };
        }
        catch (Exception ex)
        {
            Log?.LogError($"[CoopPlayerVisuals] CreatePlayerLabels failed for slot {summary.SlotIndex}: {ex.Message}");
            return null;
        }
    }

    private PlayerVisual CreatePlayerVisual(CoopPlayerSummary summary)
    {
        if (_playerRef == null)
        {
            return null;
        }

        try
        {
            var clone = new GameObject($"CoopPlayer_Slot{summary.SlotIndex}");
            clone.transform.position = _playerRef.transform.position;
            // Match the Player's Unity layer so the camera's culling mask renders
            // the clone. Without this the clone can be on Default layer while
            // Main Camera filters out Default, producing an invisible clone.
            clone.layer = _playerRef.layer;

            var originalRenderer = FindPlayerSpriteRenderer();
            if (originalRenderer != null)
            {
                var sr = clone.AddComponent<SpriteRenderer>();
                // Leave sprite null if the switcher lookup fails — UpdateVisuals
                // retries every frame so a transient miss self-corrects.
                var classSprite = GetClassBaseSprite(summary.ChosenClass);
                sr.sprite = classSprite;
                if (classSprite == null)
                {
                    Log?.LogWarning($"[CoopPlayerVisuals] GetClassBaseSprite returned null for slot {summary.SlotIndex} class {summary.ChosenClass}");
                }

                sr.material = originalRenderer.material;
                sr.sortingLayerID = originalRenderer.sortingLayerID;
                sr.sortingOrder = originalRenderer.sortingOrder - 1;
                sr.color = GetSlotColor(summary.SlotIndex);
                // Preserve flipX in case the real Player renders flipped.
                sr.flipX = originalRenderer.flipX;
                sr.flipY = originalRenderer.flipY;

                // Drive the clone's sprite with the class-specific Animator so it
                // plays the same idle animation as the host's real Player (breathing,
                // blink, etc). UpdateVisuals retries the controller lookup each frame
                // so a transient miss self-corrects.
                var animator = clone.AddComponent<Animator>();
                var ctrl = GetClassAnimationController(summary.ChosenClass);
                if (ctrl != null)
                {
                    animator.runtimeAnimatorController = ctrl;
                }
            }
            else
            {
                Log?.LogWarning($"[CoopPlayerVisuals] No SpriteRenderer found on _playerRef for slot {summary.SlotIndex}");
            }

            return CreatePlayerLabels(summary, clone);
        }
        catch (Exception ex)
        {
            Log?.LogError($"[CoopPlayerVisuals] Failed to create visual for slot {summary.SlotIndex}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Find the Player's SpriteRenderer robustly. The PeglinClassAnimationSwitcher
    /// is on the same GO as the SpriteRenderer, but the "Player" tag might be on a
    /// parent or a different child, so we check the root first, then children
    /// (including inactive ones).
    /// </summary>
    private SpriteRenderer FindPlayerSpriteRenderer()
    {
        if (_playerRef == null)
        {
            return null;
        }

        var sr = _playerRef.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            return sr;
        }

        return _playerRef.GetComponentInChildren<SpriteRenderer>(true);
    }

    /// <summary>
    /// Rewrite the live Player GameObject so it visually represents the host's
    /// class on this client machine. The Animator on the Player drives its
    /// sprite every frame via a class-specific RuntimeAnimatorController, so
    /// setting SpriteRenderer.sprite alone is immediately overwritten. We swap
    /// both the sprite and the controller to the host's class.
    /// </summary>
    private void ApplyHostClassToPlayerRef(int hostClass)
    {
        try
        {
            var sr = FindPlayerSpriteRenderer();
            var switcher = FindClassAnimationSwitcher();
            if (switcher == null)
            {
                return;
            }

            Sprite targetSprite;
            RuntimeAnimatorController targetController;
            switch ((Peglin.ClassSystem.Class)hostClass)
            {
                case Peglin.ClassSystem.Class.Balladin:
                    targetSprite = switcher.balladinBaseSprite;
                    targetController = switcher.balladinAnimationController;
                    break;
                case Peglin.ClassSystem.Class.Roundrel:
                    targetSprite = switcher.roundrelBaseSprite;
                    targetController = switcher.roundrelAnimationController;
                    break;
                case Peglin.ClassSystem.Class.Spinventor:
                    targetSprite = switcher.spinventorBaseSprite;
                    targetController = switcher.spinventorAnimationController;
                    break;
                default:
                    targetSprite = switcher.peglinBaseSprite;
                    targetController = switcher.peglinAnimationController;
                    break;
            }

            var animator = switcher.GetComponent<Animator>();
            if (animator != null && targetController != null &&
                animator.runtimeAnimatorController != targetController)
            {
                animator.runtimeAnimatorController = targetController;
            }

            if (sr != null && targetSprite != null && sr.sprite != targetSprite)
            {
                sr.sprite = targetSprite;
            }
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[CoopPlayerVisuals] ApplyHostClassToPlayerRef failed: {ex.Message}");
        }
    }

    private static Peglin.PeglinClassAnimationSwitcher FindClassAnimationSwitcher()
    {
        try
        {
            // Prefer the switcher on the live scene Player — any other instance
            // (e.g. an inactive prefab asset in memory) may not have every class
            // sprite field wired, which silently returns null for non-active classes.
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                var onPlayer = player.GetComponentInChildren<Peglin.PeglinClassAnimationSwitcher>(true)
                    ?? player.GetComponentInParent<Peglin.PeglinClassAnimationSwitcher>();
                if (onPlayer != null)
                {
                    return onPlayer;
                }
            }

            var switcher = UnityEngine.Object.FindObjectOfType<Peglin.PeglinClassAnimationSwitcher>();
            if (switcher != null)
            {
                return switcher;
            }

            var all = Resources.FindObjectsOfTypeAll<Peglin.PeglinClassAnimationSwitcher>();
            if (all != null && all.Length > 0)
            {
                return all[0];
            }
        }
        catch { }

        return null;
    }

    private void UpdateVisuals(List<CoopPlayerSummary> summaries)
    {
        if (_playerRef == null)
        {
            return;
        }

        var cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        var basePos = _playerRef.transform.position;
        var activeSlot = LatestActiveSlot;

        // Scale the "main" (slot 0) position when the host is active.
        try
        {
            var localScale = (activeSlot == HostSlot) ? 1.15f : 1f;
            _playerRef.transform.localScale = Vector3.Lerp(
                _playerRef.transform.localScale, Vector3.one * localScale, Time.deltaTime * 5f);
        }
        catch { }

        // Update host label (attached to _playerRef = main position).
        if (_hostLabel != null)
        {
            CoopPlayerSummary hostSummary = null;
            foreach (var s in summaries)
            {
                if (s.SlotIndex == HostSlot)
                { hostSummary = s; break; }
            }

            if (hostSummary != null)
            {
                UpdatePlayerLabel(_hostLabel, hostSummary, basePos, activeSlot, cam);
            }
        }

        // Update clone labels — slot N sits -2.5*N units to the left of host.
        foreach (var visual in _visuals)
        {
            if (visual.SpriteClone == null)
            {
                continue;
            }

            CoopPlayerSummary summary = null;
            foreach (var s in summaries)
            {
                if (s.SlotIndex == visual.SlotIndex)
                { summary = s; break; }
            }

            if (summary == null)
            {
                continue;
            }

            // Static position offset — the clone's Animator handles any idle
            // animation (breathing, blink, etc). No procedural bob.
            var offset = new Vector3(-2.5f * visual.SlotIndex, -0.2f * visual.SlotIndex, 0);
            visual.SpriteClone.transform.position = basePos + offset;

            var clonePos = visual.SpriteClone.transform.position;
            UpdatePlayerLabel(visual, summary, clonePos, activeSlot, cam);

            // Scale highlight for the active player.
            var targetScale = (activeSlot == visual.SlotIndex) ? 1.15f : 1f;
            visual.SpriteClone.transform.localScale = Vector3.Lerp(
                visual.SpriteClone.transform.localScale,
                Vector3.one * targetScale, Time.deltaTime * 5f);

            // Tint + sprite (retry sprite lookup each frame until the switcher is
            // findable, so a clone created before the Player became active still
            // converges to the correct class sprite).
            var sr = visual.SpriteClone.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                if (sr.sprite == null)
                {
                    var classSprite = GetClassBaseSprite(summary.ChosenClass);
                    if (classSprite != null)
                    {
                        sr.sprite = classSprite;
                    }
                }

                var baseColor = GetSlotColor(visual.SlotIndex);
                sr.color = (activeSlot == visual.SlotIndex)
                    ? Color.Lerp(sr.color, Color.white, Time.deltaTime * 3f)
                    : Color.Lerp(sr.color, baseColor, Time.deltaTime * 3f);
            }

            // Retry animator controller assignment each frame until found. The
            // switcher may not have every class's controller wired at clone-
            // creation time, so we harvest lazily.
            var anim = visual.SpriteClone.GetComponent<Animator>();
            if (anim != null && anim.runtimeAnimatorController == null)
            {
                var ctrl = GetClassAnimationController(summary.ChosenClass);
                if (ctrl != null)
                {
                    anim.runtimeAnimatorController = ctrl;
                }
            }
        }
    }

    // Colors for name pulse animation
    private static readonly Color _nameColorBase = new Color(0.9f, 0.5f, 0.1f); // dark orange
    private static readonly Color _nameColorActive = new Color(1f, 0.95f, 0.4f); // bright yellow
    private static readonly Color _arrowColorDim = new Color(1f, 0.9f, 0.3f, 0.5f);
    private static readonly Color _arrowColorBright = new Color(1f, 1f, 0.6f, 1f);

    private void UpdatePlayerLabel(PlayerVisual visual, CoopPlayerSummary summary,
        Vector3 charPos, int activeSlot, Camera cam)
    {
        if (visual == null || cam == null)
        {
            return;
        }

        // Update texts
        visual.HpText?.text = $"{summary.CurrentHealth:F0}/{summary.MaxHealth:F0}";

        visual.NameText?.text = FormatName(summary.PlayerName, summary.SlotIndex);

        // Position name above character, HP below, arrow to the right-middle
        PositionPanelAtWorld(visual.NamePanel, charPos + new Vector3(0, 2.0f, 0), cam);
        PositionPanelAtWorld(visual.HpPanel, charPos + new Vector3(0, -0.6f, 0), cam);

        var isActive = (activeSlot == visual.SlotIndex);

        // Pulsate name color when this player is active
        if (visual.NameText != null)
        {
            if (isActive)
            {
                var t = (Mathf.Sin(Time.unscaledTime * 3f) + 1f) * 0.5f; // 0..1 at ~3Hz
                visual.NameText.color = Color.Lerp(_nameColorBase, _nameColorActive, t);
            }
            else
            {
                visual.NameText.color = _nameColorBase;
            }
        }

        // Arrow indicator — pulse scale and color
        if (visual.ArrowPanel != null)
        {
            visual.ArrowPanel.SetActive(isActive);
            if (isActive)
            {
                PositionPanelAtWorld(visual.ArrowPanel, charPos + new Vector3(1.0f, 0.5f, 0), cam);

                var pulse = (Mathf.Sin(Time.unscaledTime * 4f) + 1f) * 0.5f; // 0..1 at ~4Hz
                var scale = Mathf.Lerp(0.85f, 1.15f, pulse);
                visual.ArrowPanel.transform.localScale = new Vector3(scale, scale, 1f);

                visual.ArrowText?.color = Color.Lerp(_arrowColorDim, _arrowColorBright, pulse);
            }
        }

        // Status effects — native-style icons above the player's head
        UpdateStatusIcons(visual, summary.StatusEffects, charPos, cam);
    }

    private void UpdateStatusIcons(PlayerVisual visual, List<StatusEffectEntry> effects,
        Vector3 charPos, Camera cam)
    {
        if (visual.StatusIconContainer == null)
        {
            return;
        }

        var hasEffects = effects != null && effects.Count > 0;

        // Perf: reuse instance scratch collections instead of allocating each frame.
        _activeTypesScratch.Clear();
        _removeScratch.Clear();

        if (hasEffects)
        {
            foreach (var e in effects)
            {
                if (e.Intensity > 0)
                {
                    _activeTypesScratch.Add(e.EffectType);
                }
            }
        }

        // Remove icons for effects that are no longer active
        foreach (var kvp in visual.StatusIcons)
        {
            if (!_activeTypesScratch.Contains(kvp.Key))
            {
                try
                { if (kvp.Value.Root != null)
                    {
                        Destroy(kvp.Value.Root);
                    }
                }
                catch { }

                _removeScratch.Add(kvp.Key);
            }
        }

        foreach (var key in _removeScratch)
        {
            visual.StatusIcons.Remove(key);
        }

        if (!hasEffects)
        {
            visual.StatusIconContainer.SetActive(false);
            return;
        }

        // Add or update icons for each active effect
        foreach (var e in effects)
        {
            if (e.Intensity <= 0)
            {
                continue;
            }

            if (visual.StatusIcons.TryGetValue(e.EffectType, out var existing))
            {
                // Update intensity text
                existing.IntensityText?.text = e.Intensity > 999
                        ? (e.Intensity / 1000) + "K"
                        : e.Intensity.ToString();
            }
            else
            {
                // Create new icon
                var entry = CreateStatusIcon(e.EffectType, e.Intensity,
                    visual.StatusIconContainer.transform);
                if (entry != null)
                {
                    visual.StatusIcons[e.EffectType] = entry;
                }
            }
        }

        visual.StatusIconContainer.SetActive(true);
        PositionPanelAtWorld(visual.StatusIconContainer, charPos + new Vector3(0, 2.9f, 0), cam);

        // Update the hover world-anchor for each icon so the tooltip appears
        // next to the correct character (host vs. clone). charPos comes from
        // the real player GO / clone, which can move during animations.
        var tooltipAnchor = charPos + new Vector3(0, 2.2f, 0);
        foreach (var kvp in visual.StatusIcons)
        {
            kvp.Value.Hover?.WorldAnchor = tooltipAnchor;
        }
    }

    private StatusIconEntry CreateStatusIcon(int effectType, int intensity, Transform parent)
    {
        try
        {
            // Create GO with RectTransform from the start. Using
            // `new GameObject(name, typeof(RectTransform))` ensures the GO has
            // a RectTransform (not a plain Transform) before any UI components
            // are added, which is required for TextMeshProUGUI and Image.
            var icon = new GameObject($"StatusIcon_{effectType}", typeof(RectTransform));
            icon.transform.SetParent(parent, false);

            var rect = icon.GetComponent<RectTransform>();
            rect?.sizeDelta = new Vector2(50, 50);

            // Add LayoutElement so HorizontalLayoutGroup respects preferred size
            var le = icon.AddComponent<LayoutElement>();
            if (le != null)
            {
                le.preferredWidth = 50;
                le.preferredHeight = 50;
            }

            // Icon image (status effect sprite). raycastTarget = true so the
            // hover handler receives pointer events for the tooltip.
            var img = icon.AddComponent<Image>();
            if (img != null)
            {
                var sprite = GetStatusEffectSprite(effectType);
                if (sprite != null)
                {
                    img.sprite = sprite;
                    img.preserveAspect = true;
                }
                else
                {
                    // Fallback: tinted square with effect abbreviation
                    img.color = new Color(0.4f, 0.3f, 0.6f, 0.8f);
                }

                img.raycastTarget = true;
            }

            // Hover handler — shows the native tooltip on pointer-enter.
            var hover = icon.AddComponent<StatusIconHoverHandler>();
            hover.EffectType = (Battle.StatusEffects.StatusEffectType)effectType;

            // Intensity number overlaid at bottom-center. Create with
            // RectTransform up-front so AddComponent<TextMeshProUGUI> doesn't
            // silently fail on a plain Transform.
            var textObj = new GameObject("Intensity", typeof(RectTransform));
            textObj.transform.SetParent(icon.transform, false);
            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            if (tmp == null)
            {
                Log?.LogWarning($"[CoopPlayerVisuals] AddComponent<TextMeshProUGUI> returned null for effect {effectType}");
                return new StatusIconEntry { Root = icon, IconImage = img, IntensityText = null };
            }

            tmp.text = intensity > 999 ? (intensity / 1000) + "K" : intensity.ToString();
            tmp.fontSize = 32;
            tmp.fontStyle = FontStyles.Normal;
            var font = GetStatusEffectNumberFont();
            if (font != null)
            {
                tmp.font = font;
            }

            tmp.alignment = TextAlignmentOptions.BottomRight;
            tmp.color = Color.white;
            // TMP outline width setter internally dereferences a material that may
            // not be initialized on a freshly-created TMP_Text, throwing NRE from
            // SetOutlineThickness. Wrap separately so the rest of the icon survives.
            try
            { tmp.outlineWidth = 0.35f; }
            catch { }

            try
            { tmp.outlineColor = Color.black; }
            catch { }

            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;

            // Anchor the intensity number to the bottom-right corner and let
            // it overflow slightly outside the icon so it sits at the corner
            // rather than inset deep into the icon.
            var textRect = tmp.rectTransform;
            if (textRect != null)
            {
                textRect.anchorMin = new Vector2(1f, 0f);
                textRect.anchorMax = new Vector2(1f, 0f);
                textRect.pivot = new Vector2(1f, 0f);
                textRect.sizeDelta = new Vector2(44, 30);
                textRect.anchoredPosition = new Vector2(8, -6);
            }

            return new StatusIconEntry
            {
                Root = icon,
                IconImage = img,
                IntensityText = tmp,
                Hover = hover,
            };
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[CoopPlayerVisuals] CreateStatusIcon({effectType}, {intensity}) failed: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Disable the native player StatusEffectIconManager's horizontal icon
    /// container so native icons don't render above the real Player GameObject
    /// (which visually represents slot 0 in coop). Per-slot icons are drawn
    /// by CoopPlayerVisuals instead.
    /// </summary>
    private void HideNativeStatusIcons()
    {
        try
        {
            var statusCtrl = GetCachedStatusController();
            if (statusCtrl == null)
            {
                return;
            }

            var uiField = HarmonyLib.AccessTools.Field(
                typeof(Battle.StatusEffects.PlayerStatusEffectController), "_statusEffectUI");
            var ui = uiField?.GetValue(statusCtrl) as Battle.StatusEffects.StatusEffectIconManager;
            if (ui == null)
            {
                return;
            }

            if (ui.horizontalContainer != null && ui.horizontalContainer.gameObject.activeSelf)
            {
                ui.horizontalContainer.gameObject.SetActive(false);
            }
        }
        catch { }
    }

    /// <summary>
    /// Cached lookup for PlayerStatusEffectController. FindObjectOfType walks the
    /// full scene hierarchy and was measurably expensive when called every frame
    /// from both HideNativeStatusIcons and BuildHostSummaries. The cache is
    /// invalidated on scene change in Update (above).
    /// </summary>
    private Battle.StatusEffects.PlayerStatusEffectController GetCachedStatusController()
    {
        if (_cachedStatusCtrl == null)
        {
            _cachedStatusCtrl = FindObjectOfType<Battle.StatusEffects.PlayerStatusEffectController>();
        }

        return _cachedStatusCtrl;
    }

    private static Battle.StatusEffects.StatusEffectData GetStatusEffectData()
    {
        if (_statusEffectDataSearched)
        {
            return _statusEffectData;
        }

        _statusEffectDataSearched = true;
        try
        {
            var all = Resources.FindObjectsOfTypeAll<Battle.StatusEffects.StatusEffectData>();
            if (all.Length > 0)
            {
                _statusEffectData = all[0];
            }
        }
        catch { }

        return _statusEffectData;
    }

    private static Sprite GetStatusEffectSprite(int effectType)
    {
        var data = GetStatusEffectData();
        if (data == null)
        {
            return null;
        }

        return data.GetStatusEffectIcon((Battle.StatusEffects.StatusEffectType)effectType);
    }

    /// <summary>
    /// Position a screen-space overlay panel at a world point.
    /// Uses RectTransformUtility to convert properly with CanvasScaler,
    /// ensuring consistent sizing across different screen resolutions.
    /// </summary>
    private void PositionPanelAtWorld(GameObject panel, Vector3 worldPos, Camera cam)
    {
        if (panel == null || cam == null)
        {
            return;
        }

        var screenPos = cam.WorldToScreenPoint(worldPos);
        if (screenPos.z < 0)
        { panel.SetActive(false); return; }

        if (!panel.activeSelf)
        {
            panel.SetActive(true);
        }

        var rect = panel.GetComponent<RectTransform>();
        if (rect == null)
        {
            return;
        }

        // Convert screen pixels to canvas local coords (accounts for CanvasScaler)
        if (_overlayCanvasRect != null)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _overlayCanvasRect, screenPos, null, out var localPoint);
            rect.localPosition = localPoint;
        }
        else
        {
            rect.position = screenPos;
        }
    }

    private void CleanupVisuals()
    {
        foreach (var v in _visuals)
        {
            DestroyVisual(v);
        }

        _visuals.Clear();

        if (_hostLabel != null)
        {
            DestroyPanels(_hostLabel);
            _hostLabel = null;
        }
    }

    private void DestroyVisual(PlayerVisual v)
    {
        try
        { if (v.SpriteClone != null)
            {
                Destroy(v.SpriteClone);
            }
        }
        catch { }

        DestroyPanels(v);
    }

    private void DestroyPanels(PlayerVisual v)
    {
        try
        { if (v.NamePanel != null)
            {
                Destroy(v.NamePanel);
            }
        }
        catch { }

        try
        { if (v.HpPanel != null)
            {
                Destroy(v.HpPanel);
            }
        }
        catch { }

        try
        { if (v.ArrowPanel != null)
            {
                Destroy(v.ArrowPanel);
            }
        }
        catch { }

        try
        { if (v.StatusIconContainer != null)
            {
                Destroy(v.StatusIconContainer);
            }
        }
        catch { }

        v.StatusIcons?.Clear();
    }

    private static Color GetSlotColor(int slot)
    {
        switch (slot)
        {
            case 1:
                return new Color(0.7f, 0.85f, 1f);
            case 2:
                return new Color(1f, 0.8f, 0.7f);
            case 3:
                return new Color(0.8f, 1f, 0.7f);
            default:
                return new Color(0.9f, 0.9f, 0.9f);
        }
    }

    // The scene's real Player GameObject carries a PeglinClassAnimationSwitcher,
    // but on the live instance only the classes actually loaded for the run have
    // their sprite fields wired — the other three are null. We search ALL loaded
    // switchers (scene + in-memory prefab variants) and take the first non-null
    // sprite for the requested class. Falls back to ClassInfo.classSprite.
    private static readonly HashSet<int> _loggedMissingClassSprite = new HashSet<int>();
    private static Sprite _cachedPeglinSprite, _cachedBalladinSprite, _cachedRoundrelSprite, _cachedSpinventorSprite;
    private static RuntimeAnimatorController _cachedPeglinCtrl, _cachedBalladinCtrl, _cachedRoundrelCtrl, _cachedSpinventorCtrl;
    private static readonly HashSet<int> _loggedMissingClassCtrl = new HashSet<int>();

    /// <summary>
    /// Look up the class-specific RuntimeAnimatorController so the clone's
    /// Animator plays the same idle animation as the host's live Player.
    /// Harvests from every loaded PeglinClassAnimationSwitcher's serialized
    /// fields, then falls back to a name search across all loaded controllers.
    /// </summary>
    private static RuntimeAnimatorController GetClassAnimationController(int chosenClass)
    {
        try
        {
            var cached = GetCachedController(chosenClass);
            if (cached != null)
            {
                return cached;
            }

            var allSwitchers = Resources.FindObjectsOfTypeAll<Peglin.PeglinClassAnimationSwitcher>();
            if (allSwitchers != null)
            {
                foreach (var sw in allSwitchers)
                {
                    if (sw == null)
                    {
                        continue;
                    }

                    if (_cachedPeglinCtrl == null && sw.peglinAnimationController != null)
                    {
                        _cachedPeglinCtrl = sw.peglinAnimationController;
                    }

                    if (_cachedBalladinCtrl == null && sw.balladinAnimationController != null)
                    {
                        _cachedBalladinCtrl = sw.balladinAnimationController;
                    }

                    if (_cachedRoundrelCtrl == null && sw.roundrelAnimationController != null)
                    {
                        _cachedRoundrelCtrl = sw.roundrelAnimationController;
                    }

                    if (_cachedSpinventorCtrl == null && sw.spinventorAnimationController != null)
                    {
                        _cachedSpinventorCtrl = sw.spinventorAnimationController;
                    }
                }
            }

            cached = GetCachedController(chosenClass);
            if (cached != null)
            {
                return cached;
            }

            // Fallback: match loaded controllers by name.
            var needle = ((Peglin.ClassSystem.Class)chosenClass) switch
            {
                Peglin.ClassSystem.Class.Balladin => "balladin",
                Peglin.ClassSystem.Class.Roundrel => "roundrel",
                Peglin.ClassSystem.Class.Spinventor => "spinventor",
                _ => "peglin",
            };
            var allCtrls = Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>();
            if (allCtrls != null)
            {
                foreach (var c in allCtrls)
                {
                    if (c == null || string.IsNullOrEmpty(c.name))
                    {
                        continue;
                    }

                    var lname = c.name.ToLowerInvariant();
                    if (!lname.Contains(needle))
                    {
                        continue;
                    }
                    // Skip controllers that also match OTHER class names to avoid
                    // cross-contamination (some naming could be ambiguous).
                    if (lname.Contains("peg_") || lname.Contains("bomb") || lname.Contains("slime"))
                    {
                        continue;
                    }

                    SetCachedController(chosenClass, c);
                    return c;
                }
            }

            if (_loggedMissingClassCtrl.Add(chosenClass))
            {
                Log?.LogWarning($"[CoopPlayerVisuals] No animation controller for class {chosenClass} — clone will show static sprite");
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static RuntimeAnimatorController GetCachedController(int chosenClass) =>
        (Peglin.ClassSystem.Class)chosenClass switch
        {
            Peglin.ClassSystem.Class.Balladin => _cachedBalladinCtrl,
            Peglin.ClassSystem.Class.Roundrel => _cachedRoundrelCtrl,
            Peglin.ClassSystem.Class.Spinventor => _cachedSpinventorCtrl,
            _ => _cachedPeglinCtrl,
        };

    private static void SetCachedController(int chosenClass, RuntimeAnimatorController ctrl)
    {
        switch ((Peglin.ClassSystem.Class)chosenClass)
        {
            case Peglin.ClassSystem.Class.Balladin:
                _cachedBalladinCtrl = ctrl;
                break;
            case Peglin.ClassSystem.Class.Roundrel:
                _cachedRoundrelCtrl = ctrl;
                break;
            case Peglin.ClassSystem.Class.Spinventor:
                _cachedSpinventorCtrl = ctrl;
                break;
            default:
                _cachedPeglinCtrl = ctrl;
                break;
        }
    }

    private static Sprite GetClassBaseSprite(int chosenClass)
    {
        try
        {
            var cached = GetCachedSprite(chosenClass);
            if (cached != null)
            {
                return cached;
            }

            // Opportunistically populate the cache from every sprite field on every
            // loaded switcher — not just the requested class. The Peglin Player
            // prefab in the scene typically has only the active class's sprite
            // wired, but SelectableCharacter prefabs (main menu) and other variants
            // may still be in memory and wired for other classes. Harvest them all.
            var all = Resources.FindObjectsOfTypeAll<Peglin.PeglinClassAnimationSwitcher>();
            if (all != null)
            {
                foreach (var switcher in all)
                {
                    if (switcher == null)
                    {
                        continue;
                    }

                    if (_cachedPeglinSprite == null && switcher.peglinBaseSprite != null)
                    {
                        _cachedPeglinSprite = switcher.peglinBaseSprite;
                    }

                    if (_cachedBalladinSprite == null && switcher.balladinBaseSprite != null)
                    {
                        _cachedBalladinSprite = switcher.balladinBaseSprite;
                    }

                    if (_cachedRoundrelSprite == null && switcher.roundrelBaseSprite != null)
                    {
                        _cachedRoundrelSprite = switcher.roundrelBaseSprite;
                    }

                    if (_cachedSpinventorSprite == null && switcher.spinventorBaseSprite != null)
                    {
                        _cachedSpinventorSprite = switcher.spinventorBaseSprite;
                    }
                }
            }

            cached = GetCachedSprite(chosenClass);
            if (cached != null)
            {
                return cached;
            }

            // Fallback: ClassInfo.classSprite (main menu character select asset).
            var ci = GetClassBaseSpriteFromClassInfo(chosenClass);
            if (ci != null)
            {
                SetCachedSprite(chosenClass, ci);
                return ci;
            }

            // Fallback: SelectableCharacter.idleSprite (main menu character prefab).
            var sc = GetClassBaseSpriteFromSelectableCharacter(chosenClass);
            if (sc != null)
            {
                SetCachedSprite(chosenClass, sc);
                return sc;
            }

            // Fallback: load the class-specific Peglin player prefab via Addressables,
            // search its SpriteRenderer + Animator for the class's idle sprite, and
            // cache. Runs once per class on a miss.
            var ap = GetClassBaseSpriteFromAddressable(chosenClass);
            if (ap != null)
            {
                SetCachedSprite(chosenClass, ap);
                return ap;
            }

            if (_loggedMissingClassSprite.Add(chosenClass))
            {
                Log?.LogWarning($"[CoopPlayerVisuals] No sprite for class {chosenClass} — switchers={all?.Length ?? 0}, caches: P={(_cachedPeglinSprite != null)} B={(_cachedBalladinSprite != null)} R={(_cachedRoundrelSprite != null)} S={(_cachedSpinventorSprite != null)}");
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static Sprite GetCachedSprite(int chosenClass) =>
        (Peglin.ClassSystem.Class)chosenClass switch
        {
            Peglin.ClassSystem.Class.Balladin => _cachedBalladinSprite,
            Peglin.ClassSystem.Class.Roundrel => _cachedRoundrelSprite,
            Peglin.ClassSystem.Class.Spinventor => _cachedSpinventorSprite,
            _ => _cachedPeglinSprite,
        };

    private static void SetCachedSprite(int chosenClass, Sprite sprite)
    {
        switch ((Peglin.ClassSystem.Class)chosenClass)
        {
            case Peglin.ClassSystem.Class.Balladin:
                _cachedBalladinSprite = sprite;
                break;
            case Peglin.ClassSystem.Class.Roundrel:
                _cachedRoundrelSprite = sprite;
                break;
            case Peglin.ClassSystem.Class.Spinventor:
                _cachedSpinventorSprite = sprite;
                break;
            default:
                _cachedPeglinSprite = sprite;
                break;
        }
    }

    /// <summary>
    /// Fallback: look up the class sprite via ClassInfo ScriptableObjects, which
    /// carry a `classSprite` field per class and are always loaded (used by the
    /// main menu character select). Used when no PeglinClassAnimationSwitcher
    /// has the requested class field wired.
    /// </summary>
    private static Sprite GetClassBaseSpriteFromClassInfo(int chosenClass)
    {
        try
        {
            var all = Resources.FindObjectsOfTypeAll<ClassSystem.ClassInfo>();
            if (all == null)
            {
                return null;
            }

            foreach (var info in all)
            {
                if (info != null && (int)info.characterClass == chosenClass && info.classSprite != null)
                {
                    return info.classSprite;
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Fallback: SelectableCharacter.idleSprite (main menu character prefabs,
    /// one per class). Typically loaded when the user has passed through the
    /// main menu during this session.
    /// </summary>
    private static Sprite GetClassBaseSpriteFromSelectableCharacter(int chosenClass)
    {
        try
        {
            var all = Resources.FindObjectsOfTypeAll<PeglinUI.MainMenu.SelectableCharacter>();
            if (all == null)
            {
                return null;
            }

            foreach (var sc in all)
            {
                if (sc != null && (int)sc.characterClass == chosenClass && sc.idleSprite != null)
                {
                    return sc.idleSprite;
                }
            }
        }
        catch { }

        return null;
    }

    private static readonly HashSet<int> _addressableAttempted = new HashSet<int>();

    /// <summary>
    /// Last-ditch fallback: load the class sprite directly via Addressables.
    /// Inspection of the game's catalog.json shows Roundrel and Spinventor ship
    /// their base sprites as addressable .png assets (Peglin/Balladin are scene-
    /// embedded and typically come from already-loaded switchers/ClassInfos, so
    /// this path is really about covering Roundrel and Spinventor). We try the
    /// full asset path first; LoadAssetAsync returns a Texture2D for .png keys
    /// unless we ask for a Sprite sub-asset, so we also fall back to Texture2D.
    /// Runs once per class.
    /// </summary>
    private static Sprite GetClassBaseSpriteFromAddressable(int chosenClass)
    {
        try
        {
            if (!_addressableAttempted.Add(chosenClass))
            {
                return null;
            }

            // Known addressable sprite paths (from the game's catalog.json).
            var addresses = (Peglin.ClassSystem.Class)chosenClass switch
            {
                Peglin.ClassSystem.Class.Roundrel => new[] { "Assets/Art/Peglin/Roundrel/Sprites/roundrel.png" },
                Peglin.ClassSystem.Class.Spinventor => new[] { "Assets/Art/Peglin/Spinventor/spinventor.png" },
                _ => new string[0],
            };

            foreach (var addr in addresses)
            {
                Sprite sprite = null;
                try
                {
                    // PNGs ship as Texture2D in Unity Addressables. LoadAssetAsync<Sprite>
                    // works only if the importer marked it as a Sprite; try both.
                    var spriteHandle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<Sprite>(addr);
                    sprite = spriteHandle.WaitForCompletion();
                }
                catch { sprite = null; }

                if (sprite != null)
                {
                    Log?.LogInfo($"[CoopPlayerVisuals] Loaded class {chosenClass} sprite from Addressable '{addr}'");
                    return sprite;
                }

                try
                {
                    var texHandle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<Texture2D>(addr);
                    var tex = texHandle.WaitForCompletion();
                    if (tex != null)
                    {
                        var rect = new Rect(0f, 0f, tex.width, tex.height);
                        var s = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 16f);
                        if (s != null)
                        {
                            Log?.LogInfo($"[CoopPlayerVisuals] Built class {chosenClass} sprite from Addressable Texture2D '{addr}' (size={tex.width}x{tex.height})");
                            return s;
                        }
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[CoopPlayerVisuals] Addressable sprite fallback failed for class {chosenClass}: {ex.Message}");
        }

        return null;
    }

    public static void Reset()
    {
        LatestPlayerSummaries = null;
        LatestActiveSlot = -1;
        _statusEffectDataSearched = false;
        _statusEffectData = null;
        _loggedMissingClassSprite.Clear();
        _addressableAttempted.Clear();
        _cachedPeglinSprite = _cachedBalladinSprite = _cachedRoundrelSprite = _cachedSpinventorSprite = null;
        _cachedPeglinCtrl = _cachedBalladinCtrl = _cachedRoundrelCtrl = _cachedSpinventorCtrl = null;
        _loggedMissingClassCtrl.Clear();
    }
}
