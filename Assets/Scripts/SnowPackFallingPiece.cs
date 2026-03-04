using UnityEngine;

/// <summary>屋根端を越えた雪ブロック用。Rigidbodyで重力落下、地面接触でReturnToPool。2秒経過でフォールバック消滅。</summary>
public class SnowPackFallingPiece : MonoBehaviour
{
    public SnowPackSpawner spawner;
    public LayerMask groundMask = ~0;
    [Tooltip("Min visible lifetime before timeout despawn (do not pool too quickly).")]
    public float fallTimeoutSeconds = 2.5f;

    Rigidbody _rb;
    float _startTime;
    static int _groundHitCount;
    static int _fallingTriggeredCount;

    /// <summary>落下開始時に呼ぶ。Rigidbodyを設定し、初速を適用。</summary>
    public void ActivateFalling(Vector3 initialVelocity)
    {
        _startTime = Time.time;
        _rb = GetComponent<Rigidbody>();
        if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.linearVelocity = initialVelocity;
        _rb.constraints = RigidbodyConstraints.None;
        _fallingTriggeredCount++;
        SnowLoopLogCapture.AppendToAssiReport($"=== FallingTriggered === count={_fallingTriggeredCount}");
    }

    void Update()
    {
        if (Time.time - _startTime >= fallTimeoutSeconds)
        {
            ReturnFromFall("Timeout2s");
            return;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (this == null || gameObject == null || collision == null || collision.collider == null) return;
        int layer = collision.gameObject.layer;
        if (((1 << layer) & groundMask.value) != 0)
        {
            _groundHitCount++;
            CoreGameplayManager.Instance?.AddMoneyFromFallingPiece();
            SnowLoopLogCapture.AppendToAssiReport($"=== GroundHit === count={_groundHitCount}");
            ReturnFromFall("GroundHit");
            return;
        }
        string name = collision.collider.name ?? "";
        if (name.Contains("Ground") || name.Contains("Plane") || name.Contains("Porch"))
        {
            _groundHitCount++;
            CoreGameplayManager.Instance?.AddMoneyFromFallingPiece();
            SnowLoopLogCapture.AppendToAssiReport($"=== GroundHit === count={_groundHitCount}");
            ReturnFromFall("GroundHit");
        }
    }

    void ReturnFromFall(string reason)
    {
        if (this == null || gameObject == null) return;
        var rb = GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);
        var comp = GetComponent<SnowPackFallingPiece>();
        if (comp != null) Destroy(comp);
        if (spawner != null && spawner.gameObject != null)
            spawner.ReturnToPoolFromFalling(transform, reason);
    }

    public static int GroundHitCount => _groundHitCount;
    public static int FallingTriggeredCount => _fallingTriggeredCount;
}
