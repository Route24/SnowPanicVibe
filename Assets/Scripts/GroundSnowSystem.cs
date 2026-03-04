using UnityEngine;

/// <summary>
/// Lightweight ground accumulation source of truth + visual (広がって増える).
/// </summary>
public class GroundSnowSystem : MonoBehaviour
{
    public float totalSnowAmount;
    public float maxVisualHeight = 0.12f;
    public float amountToRadiusScale = 0.8f;
    public float baseRadius = 3f;
    public Color snowColor = new Color(0.92f, 0.95f, 1f, 1f);
    public bool logEverySecond = true;

    [Header("Resolved targets")]
    public Collider groundCollider;

    Transform _groundLayer;

    /// <summary>DebugSnowVisibility用。GroundSnowLayerのRenderer。</summary>
    public Renderer GetGroundLayerRenderer()
    {
        if (_groundLayer == null) EnsureVisual();
        return _groundLayer != null ? _groundLayer.GetComponent<Renderer>() : null;
    }
    float _nextLogTime;
    float _nextVisualLogTime;
    Material _sharedMat;
    string _visualMode = "Plane";

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

        float radius = baseRadius + totalSnowAmount * amountToRadiusScale;
        radius = Mathf.Clamp(radius, 0.5f, Mathf.Max(b.size.x, b.size.z) * 0.8f);
        float h = Mathf.Clamp(totalSnowAmount * 0.02f, 0.01f, maxVisualHeight);

        _groundLayer.position = new Vector3(b.center.x, b.max.y + h * 0.5f, b.center.z);
        _groundLayer.rotation = Quaternion.identity;
        _groundLayer.localScale = new Vector3(radius * 2f, h, radius * 2f);

        if (Time.time >= _nextVisualLogTime)
        {
            _nextVisualLogTime = Time.time + 1f;
            Debug.Log($"[GroundVisual] total={totalSnowAmount:F3} radius={radius:F2} height={h:F3} mode={_visualMode}");
        }
    }
}
