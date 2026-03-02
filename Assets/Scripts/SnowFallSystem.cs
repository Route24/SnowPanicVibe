using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Continuous lightweight snowfall without rigidbodies.
/// </summary>
public class SnowFallSystem : MonoBehaviour
{
    [Header("Spawn")]
    public float spawnIntervalSeconds = 0.06f;
    public int spawnPerTick = 2;
    public int maxActivePieces = 240;
    public Vector2 pieceSizeRange = new Vector2(0.05f, 0.1f);

    [Header("Motion")]
    public float fallSpeed = 2.4f;
    public float gravity = 3.0f;
    public float windStrength = 0.35f;

    [Header("Accumulation")]
    public float addPerLandingMeters = 0.01f;
    public float addPerGroundHit = 0.01f;

    [Header("References")]
    public RoofSnowSystem roofSnowSystem;
    public GroundSnowSystem groundSnowSystem;
    public Collider roofSlideCollider;
    public LayerMask groundMask = ~0;

    struct Piece
    {
        public Transform t;
        public Vector3 vel;
        public bool active;
    }

    readonly List<Piece> _pieces = new List<Piece>();
    float _spawnTimer;
    float _nextLogTime;
    int _spawned;
    int _roofHits;
    int _groundHits;

    void Start()
    {
        ResolveRefs();
        EnsurePool(maxActivePieces);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        _spawnTimer += dt;
        while (_spawnTimer >= Mathf.Max(0.01f, spawnIntervalSeconds))
        {
            _spawnTimer -= Mathf.Max(0.01f, spawnIntervalSeconds);
            for (int i = 0; i < spawnPerTick; i++) SpawnOne();
        }

        for (int i = 0; i < _pieces.Count; i++)
        {
            if (!_pieces[i].active || _pieces[i].t == null) continue;
            Piece p = _pieces[i];
            Vector3 prev = p.t.position;
            Vector3 wind = new Vector3(
                (Mathf.PerlinNoise(Time.time * 0.7f, i * 0.01f) - 0.5f) * 2f * windStrength,
                0f,
                (Mathf.PerlinNoise(i * 0.01f, Time.time * 0.7f) - 0.5f) * 2f * windStrength);
            p.vel += (Vector3.down * gravity + wind) * dt;
            Vector3 next = prev + p.vel * dt;

            Vector3 dir = next - prev;
            float dist = dir.magnitude;
            if (dist > 0.0001f && Physics.Raycast(prev, dir / dist, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                if (roofSlideCollider != null && hit.collider == roofSlideCollider)
                {
                    if (roofSnowSystem != null) roofSnowSystem.AddRoofSnow(addPerLandingMeters);
                    _roofHits++;
                    Deactivate(ref p);
                }
                else if (((1 << hit.collider.gameObject.layer) & groundMask.value) != 0 || hit.collider.name.Contains("Ground") || hit.collider.name.Contains("Plane"))
                {
                    if (groundSnowSystem != null) groundSnowSystem.AddSnow(addPerGroundHit);
                    _groundHits++;
                    Deactivate(ref p);
                }
                else
                {
                    p.t.position = next;
                }
            }
            else
            {
                p.t.position = next;
                if (p.t.position.y < -3f) Deactivate(ref p);
            }
            _pieces[i] = p;
        }

        if (Time.time >= _nextLogTime)
        {
            _nextLogTime = Time.time + 1f;
            Debug.Log($"[SnowFall] spawned={_spawned} roofHits={_roofHits} groundHits={_groundHits}");
            _spawned = 0;
            _roofHits = 0;
            _groundHits = 0;
        }
    }

    void SpawnOne()
    {
        int idx = FindInactive();
        if (idx < 0) return;
        if (roofSlideCollider == null) return;

        Piece p = _pieces[idx];
        Bounds b = roofSlideCollider.bounds;
        Vector3 pos = new Vector3(
            Random.Range(b.min.x, b.max.x),
            b.max.y + Random.Range(0.6f, 1.6f),
            Random.Range(b.min.z, b.max.z));
        p.t.position = pos;
        p.vel = Vector3.down * fallSpeed;
        p.active = true;
        float s = Random.Range(pieceSizeRange.x, pieceSizeRange.y);
        p.t.localScale = Vector3.one * s;
        p.t.gameObject.SetActive(true);
        _pieces[idx] = p;
        _spawned++;
    }

    int FindInactive()
    {
        for (int i = 0; i < _pieces.Count; i++)
            if (!_pieces[i].active && _pieces[i].t != null) return i;
        return -1;
    }

    void Deactivate(ref Piece p)
    {
        p.active = false;
        if (p.t != null) p.t.gameObject.SetActive(false);
    }

    void ResolveRefs()
    {
        if (roofSnowSystem == null) roofSnowSystem = FindFirstObjectByType<RoofSnowSystem>();
        if (groundSnowSystem == null) groundSnowSystem = FindFirstObjectByType<GroundSnowSystem>();
        if (roofSlideCollider == null && roofSnowSystem != null) roofSlideCollider = roofSnowSystem.roofSlideCollider;
    }

    void EnsurePool(int n)
    {
        while (_pieces.Count < n)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"SnowFallPiece_{_pieces.Count}";
            go.transform.SetParent(transform, false);
            var c = go.GetComponent<Collider>();
            if (c != null) c.enabled = false;
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var mat = sh != null ? new Material(sh) : null;
                if (mat != null)
                {
                    mat.color = new Color(0.95f, 0.97f, 1f, 1f);
                    r.sharedMaterial = mat;
                }
            }
            go.SetActive(false);
            _pieces.Add(new Piece { t = go.transform, active = false, vel = Vector3.zero });
        }
    }
}
