using UnityEngine;

/// <summary>
/// Lightweight ground accumulation source of truth + simple visual layer.
/// </summary>
public class GroundSnowSystem : MonoBehaviour
{
    public float totalSnowAmount;
    public float maxVisualHeight = 1.2f;
    public float amountToHeightScale = 0.25f;
    public Color snowColor = new Color(0.92f, 0.95f, 1f, 1f);
    public bool logEverySecond = true;

    [Header("Resolved targets")]
    public Collider groundCollider;

    Transform _groundLayer;
    float _nextLogTime;
    Material _sharedMat;

    public void AddSnow(float amount)
    {
        if (amount <= 0f) return;
        totalSnowAmount = Mathf.Max(0f, totalSnowAmount + amount);
        UpdateVisual();
    }

    void Start()
    {
        ResolveGround();
        EnsureVisual();
        UpdateVisual();
    }

    void Update()
    {
        if (!logEverySecond) return;
        if (Time.time < _nextLogTime) return;
        _nextLogTime = Time.time + 1f;
        Debug.Log($"[GroundSnow] total={totalSnowAmount:F3}");
    }

    void ResolveGround()
    {
        if (groundCollider != null) return;
        var plane = GameObject.Find("Plane");
        if (plane != null) groundCollider = plane.GetComponent<Collider>();
        if (groundCollider != null) return;
        var ground = GameObject.Find("Ground");
        if (ground != null) groundCollider = ground.GetComponent<Collider>();
    }

    void EnsureVisual()
    {
        var child = transform.Find("GroundSnowLayer");
        if (child != null)
        {
            _groundLayer = child;
            return;
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "GroundSnowLayer";
        go.transform.SetParent(transform, false);
        var c = go.GetComponent<Collider>();
        if (c != null) c.enabled = false;
        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _sharedMat = sh != null ? new Material(sh) : null;
            if (_sharedMat != null)
            {
                _sharedMat.color = snowColor;
                r.sharedMaterial = _sharedMat;
            }
        }
        _groundLayer = go.transform;
    }

    void UpdateVisual()
    {
        if (_groundLayer == null) EnsureVisual();
        if (_groundLayer == null) return;

        Bounds b = groundCollider != null
            ? groundCollider.bounds
            : new Bounds(Vector3.zero, new Vector3(20f, 0.2f, 20f));

        float h = Mathf.Clamp(totalSnowAmount * amountToHeightScale, 0.01f, maxVisualHeight);
        _groundLayer.position = new Vector3(b.center.x, b.max.y + h * 0.5f, b.center.z);
        _groundLayer.rotation = Quaternion.identity;
        _groundLayer.localScale = new Vector3(Mathf.Max(0.1f, b.size.x), h, Mathf.Max(0.1f, b.size.z));
    }
}
