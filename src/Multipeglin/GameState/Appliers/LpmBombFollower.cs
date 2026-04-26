using UnityEngine;

namespace Multipeglin.GameState.Appliers;

/// <summary>
/// Drives a synthesized bomb's world position from a LinearPegMovement parent
/// every frame. Without this, the bomb's own Rigidbody2D keeps it pinned to its
/// instantiate-time position while the LPM parent's physics-driven transform
/// moves out from under it — bombs visibly teleport every heartbeat instead of
/// tweening with the row.
/// </summary>
public class LpmBombFollower : MonoBehaviour
{
    public Transform LpmParent;
    public Vector3 LocalOffset;

    private Rigidbody2D _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
    }

    private void LateUpdate()
    {
        if (LpmParent == null)
        {
            return;
        }

        var target = LpmParent.position + LocalOffset;
        target.z = transform.position.z;
        if (_rb != null)
        {
            _rb.position = new Vector2(target.x, target.y);
            if (_rb.bodyType != RigidbodyType2D.Static)
            {
                _rb.velocity = Vector2.zero;
            }
        }

        transform.position = target;
    }
}
