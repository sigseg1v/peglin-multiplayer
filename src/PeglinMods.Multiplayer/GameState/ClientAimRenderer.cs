using UnityEngine;

namespace PeglinMods.Multiplayer.GameState;

/// <summary>
/// Renders a simple aim line on the spectating client showing the host's aim direction.
/// Uses a LineRenderer to draw a short line from the ball spawn position.
/// </summary>
public class ClientAimRenderer : MonoBehaviour
{
    public static ClientAimRenderer Instance { get; private set; }

    private GameObject _lineObject;
    private LineRenderer _lineRenderer;
    private bool _isVisible;
    private const float LineLength = 3f;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_lineObject != null) Destroy(_lineObject);
    }

    public void UpdateAim(float aimX, float aimY, float spawnX, float spawnY)
    {
        if (_lineObject == null)
            CreateLine();

        var origin = new Vector3(spawnX, spawnY, -0.5f);
        var dir = new Vector3(aimX, aimY, 0f).normalized;
        var end = origin + dir * LineLength;

        _lineRenderer.SetPosition(0, origin);
        _lineRenderer.SetPosition(1, end);

        if (!_isVisible)
        {
            _lineObject.SetActive(true);
            _isVisible = true;
        }

        // Also update the orb sprite rotation at the aimer base
        ClientBallRenderer.Instance?.UpdateAimDirection(aimX, aimY);
    }

    public void HideAim()
    {
        _isVisible = false;
        if (_lineObject != null)
            _lineObject.SetActive(false);
    }

    private void CreateLine()
    {
        _lineObject = new GameObject("ClientAimLine");
        _lineObject.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(_lineObject);

        _lineRenderer = _lineObject.AddComponent<LineRenderer>();
        _lineRenderer.positionCount = 2;
        _lineRenderer.startWidth = 0.08f;
        _lineRenderer.endWidth = 0.04f;
        _lineRenderer.startColor = new Color(1f, 1f, 1f, 0.8f);
        _lineRenderer.endColor = new Color(1f, 1f, 1f, 0.2f);
        _lineRenderer.sortingOrder = 99;
        _lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        _lineRenderer.useWorldSpace = true;

        _lineObject.SetActive(false);
    }
}
