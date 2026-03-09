using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MVP: Ground stays static. Snow piles spawn at hit points, stay briefly, blink, despawn.
/// No terrain expansion.
/// </summary>
public class GroundSnowSystem : MonoBehaviour
{
    public float totalSnowAmount;
    public float maxVisualHeight = 0.12f;
    public float amountToRadiusScale = 0.8f;
    public float baseRadius = 3f;
    public Color snowColor = new Color(0.92f, 0.95f, 1f, 1f);
    public bool logEverySecond = true;

    [Header("Ground pile (MVP: no expansion)")]
    public float groundPileLifetimeSec = 1.2f;
    public float groundPileBlinkDurationSec = 0.4f;
    public int maxGroundPiles = 32;
    public float pileScalePerAmount = 0.15f;

    [Header("Resolved targets")]
    public Collider groundCollider;

    Transform _groundLayer;
    readonly List<GroundSnowPile> _piles = new List<GroundSnowPile>();

    /// <summary>現在アクティブな地面雪のピール数（ログ用）。</summary>
    public int GetActivePileCount() => _piles.Count;

    /// <summary>DebugSnowVisibility用。GroundSnowLayerのRenderer。</summary>
    public Renderer GetGroundLayerRenderer()
    {
        if (_groundLayer == null) EnsureVisual();
        return _groundLayer != null ? _groundLayer.GetComponent<Renderer>() : null;
    }
    float _nextLogTime;
    float _nextVisualLogTime;
    Material _sharedMat;

    /// <summary>Legacy. MVP uses SpawnPileAt. Kept for compatibility; does not expand ground.</summary>
    public void AddSnow(float amount)
    {
        if (amount <= 0f) return;
        totalSnowAmount = Mathf.Max(0f, totalSnowAmount + amount);
        UpdateVisual();
    }

    /// <summary>MVP: Spawn a pile at hit point. Stays briefly, blinks, despawns. Ground does NOT expand.</summary>
    public void SpawnPileAt(Vector3 position, float amount)
    {
        if (amount <= 0f) return;
        TrimOldestPilesIfNeeded();
        var pile = GroundSnowPile.Create(transform, position, amount, snowColor, pileScalePerAmount, groundPileLifetimeSec, groundPileBlinkDurationSec);
        if (pile != null)
        {
            _piles.Add(pile);
            if (_piles.Count <= 3 || _piles.Count % 10 == 0)
                Debug.Log($"[GroundPile] spawned at=({position.x:F2},{position.y:F2},{position.z:F2}) amount={amount:F3} piles={_piles.Count}");
        }
    }

    void TrimOldestPilesIfNeeded()
    {
        while (_piles.Count >= maxGroundPiles && _piles.Count > 0)
        {
            var p = _piles[0];
            _piles.RemoveAt(0);
            if (p != null && p.gameObject != null) Object.Destroy(p.gameObject);
        }
    }

    void Update()
    {
        for (int i = _piles.Count - 1; i >= 0; i--)
        {
            if (_piles[i] == null || !_piles[i].gameObject.activeSelf)
                _piles.RemoveAt(i);
        }
        if (!logEverySecond) return;
        if (Time.time < _nextLogTime) return;
        _nextLogTime = Time.time + 1f;
        Debug.Log($"[GroundSnow] total={totalSnowAmount:F3} piles={_piles.Count}");
    }

    void Start()
    {
        ResolveGround();
        EnsureVisual();
        UpdateVisual();
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
                MaterialColorHelper.SetColorSafe(_sharedMat, snowColor);
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

        float radius = baseRadius;
        float h = 0.02f;
        _groundLayer.position = new Vector3(b.center.x, b.max.y + h * 0.5f, b.center.z);
        _groundLayer.rotation = Quaternion.identity;
        _groundLayer.localScale = new Vector3(radius * 2f, h, radius * 2f);

        if (Time.time >= _nextVisualLogTime)
        {
            _nextVisualLogTime = Time.time + 1f;
            Debug.Log($"[GroundVisual] static radius={radius:F2} height={h:F3} (no expansion) piles={_piles.Count}");
        }
    }
}
