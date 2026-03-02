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
    [Tooltip("直接ヒット時に周囲へ波及する半径（0で無効）")]
    public float spreadRadius = 0.18f;
    [Tooltip("クリティカルヒット時に使う拡大半径")]
    public float avalancheSpreadRadius = 0.55f;
    [Tooltip(" true=クリティカルスポット（叩くと一気に雪崩）")]
    public bool isCriticalSpot = false;

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

        float vSq = otherRb.linearVelocity.sqrMagnitude;
        float massRatio = otherRb.mass / Mathf.Max(_rb.mass, 0.01f);
        float velThreshold = massRatio >= 2f ? 0.02f : (massRatio >= 1.2f ? 0.05f : 0.12f);
        if (vSq < velThreshold * velThreshold) return;

        var dir = (slideDirection.sqrMagnitude > 0.01f ? slideDirection : Vector3.down).normalized;
        float strength = Mathf.Clamp(col.relativeVelocity.magnitude * (0.08f + massRatio * 0.02f), 0.4f, 1.5f);
        _rb.isKinematic = false;
        _rb.AddForce(dir * strength, ForceMode.Impulse);
    }

    /// <summary>動いた雪を叩いたとき。追加の力を加える</summary>
    public void PushFromHit(Vector3 hitPoint)
    {
        if (_rb == null || _rb.isKinematic) return;
        var dir = (slideDirection.sqrMagnitude > 0.01f ? slideDirection : Vector3.down).normalized;
        _rb.AddForce(dir * hitForce * 0.5f, ForceMode.Impulse);
    }

    /// <summary>叩かれたとき。Raycast の hit から呼ばれる</summary>
    public void Hit(Vector3 hitPoint, Vector3 hitNormal, bool spreadToNearby = true)
    {
        if (_rb == null) return;
        if (!_rb.isKinematic) return;

        bool isCritical = spreadToNearby && isCriticalSpot;
        if (isCritical)
            AvalancheFeedback.Trigger();
        float radius = isCritical ? avalancheSpreadRadius : spreadRadius;
        if (spreadToNearby && radius > 0.01f)
        {
            var hits = Physics.OverlapSphere(transform.position, radius);
            foreach (var col in hits)
            {
                var other = col.GetComponent<SnowTestCube>();
                if (other != null && other != this)
                {
                    var or = other.GetComponent<Rigidbody>();
                    if (or != null && or.isKinematic)
                        other.Hit(other.transform.position, Vector3.down, false);
                }
            }
        }

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
            piece.AddComponent<SnowPieceAutoSettle>();

            var col = piece.GetComponent<Collider>();
            var srcCol = GetComponent<Collider>();
            if (col != null && srcCol != null && srcCol.material != null)
                col.material = srcCol.material;
        }

        Destroy(gameObject);
    }
}
