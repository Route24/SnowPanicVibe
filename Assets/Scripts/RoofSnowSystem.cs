using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Source-of-truth roof accumulation + auto avalanche trigger.
/// </summary>
public class RoofSnowSystem : MonoBehaviour
{
    [Header("Source of truth")]
    public float roofSnowDepthMeters = 0.5f;
    public float baseThresholdMeters = 0.30f;
    public float slopeFactor = 0.0006f;
    public float minThresholdMeters = 0.10f;
    public float avalancheRetainRatio = 0.30f;
    public float avalancheCooldownSeconds = 1.5f;

    [Header("Visual")]
    public Collider roofSlideCollider;
    public Color roofSnowColor = new Color(0.92f, 0.95f, 1f, 1f);

    [Header("Burst visual")]
    public int burstChunkCount = 24;
    public float burstChunkLife = 1.8f;
    public float burstChunkSpeed = 2.2f;
    public float burstSpread = 0.8f;
    public float burstGroundDepositPerChunk = 0.015f;

    [Header("References")]
    public GroundSnowSystem groundSnowSystem;
    public SnowPackSpawner snowPackSpawner;
    public LayerMask groundMask = ~0;

    [Header("Avalanche visual")]
    public float avalancheSlideDuration = 0.6f;
    public float avalancheSlideOffset = 1.2f;
    public float avalancheGraceSeconds = 2.0f;

    Transform _roofLayer;
    float _startTime;
    float _lastSuppressedLogTime;
    Material _roofLayerMat;
    float _nextRoofLogTime;
    float _nextAvalancheTime;
    readonly List<MvpSnowChunkMotion> _chunkPool = new List<MvpSnowChunkMotion>();

    public float ComputedThreshold { get; private set; }
    public float AngleDeg { get; private set; }

    void Start()
    {
        _startTime = Time.time;
        ResolveDefaults();
        EnsureRoofVisual();
        UpdateRoofVisual();
    }

    void Update()
    {
        if (roofSlideCollider == null) return;
        Vector3 roofUp = roofSlideCollider.transform.up.normalized;
        AngleDeg = Vector3.Angle(roofUp, Vector3.up);
        ComputedThreshold = Mathf.Max(minThresholdMeters, baseThresholdMeters - AngleDeg * slopeFactor);

        if (Time.time >= _nextRoofLogTime)
        {
            _nextRoofLogTime = Time.time + 1f;
            Debug.Log($"[RoofSnow] depth={roofSnowDepthMeters:F3} threshold={ComputedThreshold:F3} angleDeg={AngleDeg:F1}");
            string state = Time.time < _nextAvalancheTime ? "Avalanche" : "Freeze";
            float groundTotal = groundSnowSystem != null ? groundSnowSystem.totalSnowAmount : 0f;
            Debug.Log($"[AvalancheAudit1s] frame={Time.frameCount} t={Time.time:F2} state={state} roofDepth={roofSnowDepthMeters:F3} groundTotal={groundTotal:F3}");
        }

        if (Time.time >= _nextAvalancheTime && roofSnowDepthMeters >= ComputedThreshold)
        {
            if (Time.time - _startTime < avalancheGraceSeconds)
            {
                if (Time.time >= _lastSuppressedLogTime)
                {
                    _lastSuppressedLogTime = Time.time + 0.5f;
                    Debug.Log($"[Avalanche] suppressed reason=Grace t={Time.time:F2} load={roofSnowDepthMeters:F3} threshold={ComputedThreshold:F3}");
                }
            }
            else
            {
                TriggerAvalanche();
            }
        }
    }

    public void AddRoofSnow(float amount)
    {
        if (amount <= 0f) return;
        roofSnowDepthMeters = Mathf.Max(0f, roofSnowDepthMeters + amount);
        UpdateRoofVisual();
    }

    void TriggerAvalanche()
    {
        float before = roofSnowDepthMeters;
        float after = Mathf.Max(0f, before * Mathf.Clamp01(avalancheRetainRatio));
        float burstAmount = Mathf.Max(0f, before - after);

        Vector3 roofUp = roofSlideCollider.transform.up.normalized;
        Vector3 slopeDir = Vector3.ProjectOnPlane(Vector3.down, roofUp).normalized;
        if (slopeDir.sqrMagnitude < 0.0001f) slopeDir = -roofSlideCollider.transform.forward.normalized;

        if (snowPackSpawner != null)
            snowPackSpawner.PlayAvalancheSlideVisual(burstAmount, slopeDir * avalancheSlideOffset, avalancheSlideDuration);

        roofSnowDepthMeters = after;
        _nextAvalancheTime = Time.time + Mathf.Max(0.2f, avalancheCooldownSeconds);
        UpdateRoofVisual();

        SpawnAvalancheBurstVisual(burstAmount);
        if (groundSnowSystem != null)
            groundSnowSystem.AddSnow(burstAmount * 0.35f);

        Vector3 burstVel = slopeDir * burstChunkSpeed;
        Debug.Log($"[Avalanche] fired depthBefore={before:F3} depthAfter={after:F3} burstAmount={burstAmount:F3} burstVel={burstVel}");
    }

    void ResolveDefaults()
    {
        if (roofSlideCollider == null)
        {
            var t = GameObject.Find("RoofSlideCollider");
            if (t != null) roofSlideCollider = t.GetComponent<Collider>();
        }
        if (snowPackSpawner == null) snowPackSpawner = FindFirstObjectByType<SnowPackSpawner>();
    }

    void EnsureRoofVisual()
    {
        if (roofSlideCollider == null) return;
        var child = roofSlideCollider.transform.Find("RoofSnowLayer");
        if (child != null)
        {
            _roofLayer = child;
            return;
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "RoofSnowLayer";
        go.transform.SetParent(roofSlideCollider.transform, false);
        var c = go.GetComponent<Collider>();
        if (c != null) c.enabled = false;
        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _roofLayerMat = sh != null ? new Material(sh) : null;
            if (_roofLayerMat != null)
            {
                _roofLayerMat.color = roofSnowColor;
                r.sharedMaterial = _roofLayerMat;
            }
        }
        _roofLayer = go.transform;
    }

    void UpdateRoofVisual()
    {
        if (roofSlideCollider == null) return;
        if (_roofLayer == null) EnsureRoofVisual();
        if (_roofLayer == null) return;

        if (roofSlideCollider is BoxCollider box)
        {
            float h = Mathf.Max(0.02f, roofSnowDepthMeters);
            Vector3 size = new Vector3(Mathf.Max(0.1f, box.size.x), h, Mathf.Max(0.1f, box.size.z));
            Vector3 center = box.center + Vector3.up * (box.size.y * 0.5f + h * 0.5f);
            _roofLayer.localPosition = center;
            _roofLayer.localRotation = Quaternion.identity;
            _roofLayer.localScale = size;
        }
        else
        {
            Bounds b = roofSlideCollider.bounds;
            float h = Mathf.Max(0.02f, roofSnowDepthMeters);
            _roofLayer.position = b.center + roofSlideCollider.transform.up * (b.extents.y + h * 0.5f);
            _roofLayer.rotation = roofSlideCollider.transform.rotation;
            _roofLayer.localScale = new Vector3(Mathf.Max(0.1f, b.size.x), h, Mathf.Max(0.1f, b.size.z));
        }
    }

    void SpawnAvalancheBurstVisual(float burstAmount)
    {
        if (roofSlideCollider == null) return;
        Vector3 roofUp = roofSlideCollider.transform.up.normalized;
        Vector3 slopeDir = Vector3.ProjectOnPlane(Vector3.down, roofUp).normalized;
        if (slopeDir.sqrMagnitude < 0.0001f) slopeDir = -roofSlideCollider.transform.forward.normalized;
        Bounds b = roofSlideCollider.bounds;
        int count = Mathf.Max(6, burstChunkCount);
        for (int i = 0; i < count; i++)
        {
            var chunk = AcquireChunk();
            Vector3 p = new Vector3(
                Random.Range(b.min.x, b.max.x),
                b.max.y + 0.05f,
                Random.Range(b.min.z, b.max.z));
            Vector3 jitter = Vector3.ProjectOnPlane(Random.insideUnitSphere, roofUp) * burstSpread;
            Vector3 vel = (slopeDir + jitter * 0.25f).normalized * burstChunkSpeed;
            float perChunkDeposit = Mathf.Max(0.001f, burstGroundDepositPerChunk + burstAmount * 0.005f / count);
            chunk.Activate(p, vel, burstChunkLife, groundSnowSystem, groundMask, perChunkDeposit);
        }
    }

    MvpSnowChunkMotion AcquireChunk()
    {
        for (int i = 0; i < _chunkPool.Count; i++)
        {
            if (_chunkPool[i] != null && !_chunkPool[i].gameObject.activeSelf)
                return _chunkPool[i];
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "AvalancheChunk";
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * 0.07f;
        var col = go.GetComponent<Collider>();
        if (col != null) col.enabled = false;
        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = sh != null ? new Material(sh) : null;
            if (mat != null)
            {
                mat.color = roofSnowColor;
                r.sharedMaterial = mat;
            }
        }
        var motion = go.AddComponent<MvpSnowChunkMotion>();
        motion.onFinished = _ => { };
        go.SetActive(false);
        _chunkPool.Add(motion);
        return motion;
    }
}
