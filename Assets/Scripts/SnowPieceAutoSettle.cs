using UnityEngine;

/// <summary>SnowPiece の残留物理を抑える安全停止</summary>
public class SnowPieceAutoSettle : MonoBehaviour
{
    Rigidbody _rb;
    float _groundedTime = -1f;
    float _spawnTime;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _spawnTime = Time.time;
    }

    void Update()
    {
        if (_rb == null) return;

        if (transform.position.y < 0.6f)
        {
            if (_groundedTime < 0f) _groundedTime = Time.time;
            if (_rb.linearVelocity.sqrMagnitude < 0.01f || Time.time - _groundedTime > 0.35f)
            {
                StopAndDispose();
                return;
            }
        }
        else
        {
            _groundedTime = -1f;
        }

        if (Time.time - _spawnTime > 3f)
            StopAndDispose();
    }

    void StopAndDispose()
    {
        var falling = GetComponent<SnowPackFallingPiece>();
        if (falling != null) return;
        if (_rb != null)
        {
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            _rb.isKinematic = true;
            _rb.Sleep();
        }

        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
        SnowDespawnLogger.RequestDespawn("StopAndDispose", SnowDespawnLogger.SnowState.Unknown, transform.position, gameObject);
        Destroy(gameObject, 0.05f);
    }
}

