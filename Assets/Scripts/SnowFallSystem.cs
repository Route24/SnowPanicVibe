using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Continuous lightweight snowfall without rigidbodies.
/// </summary>
public class SnowFallSystem : MonoBehaviour
{
    [Header("Spawn")]
    [Tooltip("false=Play開始時に降雪停止（最小ゲーム用）")]
    public bool snowfallEnabledAtStart = false;
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
    public SnowPackSpawner snowPackSpawner; // サイズ統一用（未設定時は roofSnowSystem から解決）
    public Collider roofSlideCollider;
    public LayerMask groundMask = ~0;

    struct Piece
    {
        public Transform t;
        public Vector3 vel;
        public bool active;
        public string lastContact;
    }

    readonly List<Piece> _pieces = new List<Piece>();
    float _spawnTimer;
    float _nextLogTime;
    float _lastScaleLogTime;
    int _spawned;
    int _roofHits;
    int _groundHits;
    int _spawnedTotal;
    int _destroyedTotal;
    float _nextBurstLogTime;

    void Start()
    {
        ResolveRefs();
        EnsurePool(maxActivePieces);
        float s = ResolveUnifiedScale();
        Debug.Log($"[SnowPieceScale] kind=Falling scale=({s:F3},{s:F3},{s:F3})");

        if (!snowfallEnabledAtStart)
        {
            enabled = false;
            SnowLoopLogCapture.AppendToAssiReport("=== SNOWFALL STOP ===");
            SnowLoopLogCapture.AppendToAssiReport("snowfallEnabled=false");
            SnowLoopLogCapture.AppendToAssiReport($"rate=0 stoppedBy=PlayStart");
        }
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
            float speed = p.vel.magnitude;
            if (speed > 10f && Time.time >= _nextBurstLogTime)
            {
                _nextBurstLogTime = Time.time + 0.2f;
                Debug.Log($"[SnowFallBurst] frame={Time.frameCount} t={Time.time:F2} speed={speed:F2} pos={p.t.position} vel={p.vel} reason=unknown lastContact={p.lastContact}");
            }

            Vector3 dir = next - prev;
            float dist = dir.magnitude;
            if (dist > 0.0001f && Physics.Raycast(prev, dir / dist, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                p.lastContact = hit.collider != null ? hit.collider.name : "None";
                if (speed > 10f && Time.time >= _nextBurstLogTime)
                {
                    _nextBurstLogTime = Time.time + 0.2f;
                    Debug.Log($"[SnowFallBurst] frame={Time.frameCount} t={Time.time:F2} speed={speed:F2} pos={p.t.position} vel={p.vel} reason=collision lastContact={p.lastContact}");
                }
                if (roofSlideCollider != null && hit.collider == roofSlideCollider)
                {
                    if (roofSnowSystem != null) roofSnowSystem.AddRoofSnow(addPerLandingMeters);
                    _roofHits++;
                    Deactivate(ref p);
                }
                else if (((1 << hit.collider.gameObject.layer) & groundMask.value) != 0 || hit.collider.name.Contains("Ground") || hit.collider.name.Contains("Plane"))
                {
                    // ── GROUND PIPE: tier 別 ground Y に差し替えて配置 ──
                    string tier = GroundPipeTier.GetTierByPosition(hit.point);
                    float groundY = GroundPipeTier.GetGroundY(tier);
                    Vector3 resolvedPos = float.IsNegativeInfinity(groundY)
                        ? hit.point
                        : new Vector3(hit.point.x, groundY, hit.point.z);

                    string inputLog = "[GROUND_PIPE_INPUT] tier=" + tier
                        + " resolved_pos=(" + resolvedPos.x.ToString("F3") + "," + resolvedPos.y.ToString("F3") + "," + resolvedPos.z.ToString("F3") + ")"
                        + " amount=" + addPerGroundHit.ToString("F3");
                    Debug.Log(inputLog);
                    SnowLoopLogCapture.AppendToAssiReport(inputLog);

                    if (groundSnowSystem != null)
                    {
                        bool wasEnabled = groundSnowSystem.enabled;
                        groundSnowSystem.enabled = true;
                        float origLife  = groundSnowSystem.groundPileLifetimeSec;
                        float origBlink = groundSnowSystem.groundPileBlinkDurationSec;
                        groundSnowSystem.groundPileLifetimeSec      = 99999f;
                        groundSnowSystem.groundPileBlinkDurationSec = 0f;
                        groundSnowSystem.SpawnPileAt(resolvedPos, addPerGroundHit);
                        groundSnowSystem.groundPileLifetimeSec      = origLife;
                        groundSnowSystem.groundPileBlinkDurationSec = origBlink;
                        groundSnowSystem.enabled = wasEnabled;

                        int vc = groundSnowSystem.GetActivePileCount();
                        string applyLog = "[GROUND_PIPE_APPLY] tier=" + tier
                            + " spawn_pos=(" + resolvedPos.x.ToString("F3") + "," + resolvedPos.y.ToString("F3") + "," + resolvedPos.z.ToString("F3") + ")"
                            + " forceSnowIndex=pile visibleCount=" + vc;
                        Debug.Log(applyLog);
                        SnowLoopLogCapture.AppendToAssiReport(applyLog);

                        string upperOk = (tier == "upper") ? "YES" : "N/A";
                        string lowerOk = (tier == "lower") ? "YES" : "N/A";
                        string resultLog = "[GROUND_PIPE_RESULT] upper_visible=" + upperOk
                            + " lower_visible=" + lowerOk
                            + " river_respawn=NO offscreen_fall=NO";
                        Debug.Log(resultLog);
                        SnowLoopLogCapture.AppendToAssiReport(resultLog);
                    }
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
            int active = 0;
            float maxSpeed = 0f;
            Vector3 maxVel = Vector3.zero;
            Vector3 maxPos = Vector3.zero;
            for (int i = 0; i < _pieces.Count; i++)
            {
                if (!_pieces[i].active || _pieces[i].t == null) continue;
                active++;
                float s = _pieces[i].vel.magnitude;
                if (s > maxSpeed)
                {
                    maxSpeed = s;
                    maxVel = _pieces[i].vel;
                    maxPos = _pieces[i].t.position;
                }
            }
            Debug.Log($"[SnowFallAudit1s] frame={Time.frameCount} t={Time.time:F2} active={active} spawnedTotal={_spawnedTotal} destroyedTotal={_destroyedTotal} maxSpeed1s={maxSpeed:F2}");
            Debug.Log($"[SnowFallMax1s] maxSpeed={maxSpeed:F2} maxVel={maxVel} atPos={maxPos}");
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
        float unifiedScale = ResolveUnifiedScale();
        p.t.localScale = Vector3.one * unifiedScale; // Packed/Burstと同一
        if (Time.time - _lastScaleLogTime >= 1f) { _lastScaleLogTime = Time.time; Debug.Log($"[SnowPieceScale] kind=Falling scale=({unifiedScale:F3},{unifiedScale:F3},{unifiedScale:F3})"); }
        p.t.gameObject.SetActive(true);
        _pieces[idx] = p;
        _spawned++;
        _spawnedTotal++;
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
        _destroyedTotal++;
        if (p.t != null) p.t.gameObject.SetActive(false);
    }

    void ResolveRefs()
    {
        if (roofSnowSystem == null) roofSnowSystem = FindFirstObjectByType<RoofSnowSystem>();
        if (groundSnowSystem == null) groundSnowSystem = FindFirstObjectByType<GroundSnowSystem>();
        if (roofSlideCollider == null && roofSnowSystem != null) roofSlideCollider = roofSnowSystem.roofSlideCollider;
        if (snowPackSpawner == null && roofSnowSystem != null) snowPackSpawner = roofSnowSystem.snowPackSpawner;
    }

    float ResolveUnifiedScale()
    {
        if (snowPackSpawner != null) return snowPackSpawner.pieceSize;
        if (roofSnowSystem?.snowPackSpawner != null) return roofSnowSystem.snowPackSpawner.pieceSize;
        return 0.11f;
    }

    /// <summary>スケール統一確認用。Packed/Burstと同一であるべき。</summary>
    public float GetFallingScale() => ResolveUnifiedScale();

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
                    MaterialColorHelper.SetColorSafe(mat, new Color(0.95f, 0.97f, 1f));
                    r.sharedMaterial = mat;
                }
            }
            go.SetActive(false);
            _pieces.Add(new Piece { t = go.transform, active = false, vel = Vector3.zero });
        }
    }
}
