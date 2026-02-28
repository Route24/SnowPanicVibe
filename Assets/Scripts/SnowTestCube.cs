using UnityEngine;

/// <summary>雪テスト用の立方体。叩くと落下・粉々になる</summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BoxCollider))]
public class SnowTestCube : MonoBehaviour
{
    public float hitForce = 3f;
    [HideInInspector] public Vector3 slideDirection = Vector3.down;
    public bool canBreak = true;
    public int breakIntoPieces = 4;

    Rigidbody _rb;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    void OnCollisionEnter(Collision col)
    {
        if (_rb == null || !_rb.isKinematic) return;
        var otherRb = col.rigidbody;
        if (otherRb == null) return;
        if (otherRb.linearVelocity.sqrMagnitude < 0.35f) return;

        var dir = (slideDirection.sqrMagnitude > 0.01f ? slideDirection : Vector3.down).normalized;
        float strength = Mathf.Clamp(col.relativeVelocity.magnitude * 0.04f, 0.2f, 0.8f);
        _rb.isKinematic = false;
        _rb.AddForce(dir * strength, ForceMode.Impulse);
    }

    /// <summary>叩かれたとき。Raycast の hit から呼ばれる</summary>
    public void Hit(Vector3 hitPoint, Vector3 hitNormal)
    {
        if (_rb == null) return;

        var dir = (slideDirection.sqrMagnitude > 0.01f ? slideDirection : Vector3.down).normalized;

        _rb.isKinematic = false;

        if (canBreak && breakIntoPieces > 1)
        {
            BreakAndFall(hitPoint, hitNormal);
        }
        else
        {
            _rb.AddForce(dir * hitForce, ForceMode.Impulse);
        }
    }

    void BreakAndFall(Vector3 hitPoint, Vector3 hitNormal)
    {
        float scale = transform.localScale.x;
        float pieceScale = scale * 0.5f;

        for (int i = 0; i < breakIntoPieces; i++)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = "SnowPiece";
            piece.transform.position = hitPoint + slideDirection.normalized * scale * 0.1f * (i - 1.5f) + Random.insideUnitSphere * scale * 0.06f;
            piece.transform.localScale = Vector3.one * Mathf.Max(pieceScale * 0.7f, 0.03f);
            piece.transform.rotation = Random.rotation;

            var srcRend = GetComponent<Renderer>();
            if (srcRend != null && srcRend.sharedMaterial != null)
                piece.GetComponent<Renderer>().sharedMaterial = srcRend.sharedMaterial;

            var rb = piece.AddComponent<Rigidbody>();
            rb.mass = _rb.mass / breakIntoPieces;
            rb.linearDamping = 2f;
            var dir = slideDirection.sqrMagnitude > 0.01f ? slideDirection.normalized : Vector3.down;
            rb.AddForce(dir * hitForce * 0.2f, ForceMode.Impulse);

            var col = piece.GetComponent<Collider>();
            var srcCol = GetComponent<Collider>();
            if (col != null && srcCol != null && srcCol.material != null)
                col.material = srcCol.material;
        }

        Destroy(gameObject);
    }
}
