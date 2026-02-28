using UnityEngine;

/// <summary>雪テスト用の立方体。叩くと落下・粉々になる</summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BoxCollider))]
public class SnowTestCube : MonoBehaviour
{
    public float hitForce = 5f;
    public bool canBreak = true;
    public int breakIntoPieces = 4;

    Rigidbody _rb;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    /// <summary>叩かれたとき。Raycast の hit から呼ばれる</summary>
    public void Hit(Vector3 hitPoint, Vector3 hitNormal)
    {
        if (_rb == null) return;

        _rb.isKinematic = false;

        if (canBreak && breakIntoPieces > 1)
        {
            BreakAndFall(hitPoint, hitNormal);
        }
        else
        {
            var dir = (transform.position - hitPoint).normalized + Vector3.up * 0.3f;
            _rb.AddForceAtPosition(dir * hitForce, hitPoint, ForceMode.Impulse);
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
            piece.transform.position = hitPoint + Random.insideUnitSphere * scale * 0.2f;
            piece.transform.localScale = Vector3.one * Mathf.Max(pieceScale * 0.7f, 0.03f);
            piece.transform.rotation = Random.rotation;

            var srcRend = GetComponent<Renderer>();
            if (srcRend != null && srcRend.sharedMaterial != null)
                piece.GetComponent<Renderer>().sharedMaterial = srcRend.sharedMaterial;

            var rb = piece.AddComponent<Rigidbody>();
            rb.mass = _rb.mass / breakIntoPieces;
            rb.AddExplosionForce(hitForce * 2f, hitPoint, scale * 2f, 0.3f);

            var col = piece.GetComponent<Collider>();
            var srcCol = GetComponent<Collider>();
            if (col != null && srcCol != null && srcCol.material != null)
                col.material = srcCol.material;
        }

        Destroy(gameObject);
    }
}
