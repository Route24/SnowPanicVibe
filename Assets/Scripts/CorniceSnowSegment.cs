using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BoxCollider))]
public class CorniceSnowSegment : MonoBehaviour
{
    [HideInInspector] public int index;
    [HideInInspector] public float normalizedPosition;
    [HideInInspector] public CorniceSnowManager manager;
    /// <summary>屋根雪用：ずり落ち方向（0なら従来の落下）</summary>
    [HideInInspector] public Vector3 slideDownDirection;

    Rigidbody _rb;
    bool _hasCollapsed;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.isKinematic = true; // 叩くまで物理演算しない
    }

    public void Hit(float power)
    {
        if (manager != null)
        {
            manager.OnSegmentHit(this, power);
        }
        else
        {
            Collapse();
        }
    }

    public void Collapse()
    {
        if (_hasCollapsed) return;
        _hasCollapsed = true;

        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.constraints = RigidbodyConstraints.None;

        if (manager != null)
        {
            Vector3 outward = manager.GetOutwardDirection();
            _rb.AddForce((Vector3.down + outward).normalized * manager.fallImpulse, ForceMode.Impulse);
        }
        else if (slideDownDirection.sqrMagnitude > 0.01f)
        {
            // 屋根雪：親から切り離して物理演算を安定させ、斜度に沿ってずり落ちる
            transform.SetParent(null);
            float impulse = 2f;
            _rb.AddForce(slideDownDirection.normalized * impulse, ForceMode.Impulse);
        }
    }
}

