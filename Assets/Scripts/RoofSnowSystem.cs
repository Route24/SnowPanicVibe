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
    [Tooltip("RoofSnowLayer描画厚みスケール。0.5=半分")]
    [Range(0.01f, 1f)] public float roofSnowVisualThicknessScale = 0.5f;

    [Header("Burst visual")]
    public int burstChunkCount = 48;
    public float burstChunkLife = 1.8f;
    public float burstChunkSpeed = 2.2f;
    [Tooltip("Slower = think & watch tempo.")]
    public float localAvalancheSlideSpeed = 0.9f;
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
    bool _snowRenderThicknessLogOnce;
    float _lastSuppressedLogTime;
    float _visualDepthRoof;
    bool _visualDepthRoofInitialized;
    int _packedCountAtFull = -1;
    float _baseDepthAtFull = 0.5f;
    [Tooltip("Smoothing: no sudden thickness pop. Higher = faster response.")]
    public float roofVisualSmoothSpeed = 3f;
    Material _roofLayerMat;
    float _nextRoofLogTime;
    float _nextAvalancheTime;
    readonly List<MvpSnowChunkMotion> _chunkPool = new List<MvpSnowChunkMotion>();
    bool _burstScaleLogOnce;

    public float ComputedThreshold { get; private set; }
    public float AngleDeg { get; private set; }
    /// <summary>雪崩クールダウン中（この間は Pool返却・RemoveLayers を避ける）</summary>
    public bool IsInAvalancheCooldown => Time.time < _nextAvalancheTime;
    /// <summary>DebugSnowVisibility用。RoofSnowLayerのRenderer。</summary>
    public Renderer GetRoofLayerRenderer()
    {
        if (_roofLayer == null) EnsureRoofVisual();
        return _roofLayer != null ? _roofLayer.GetComponent<Renderer>() : null;
    }

    /// <summary>屋根の法線（上方向）</summary>
    public Vector3 RoofUp => roofSlideCollider != null ? roofSlideCollider.transform.up.normalized : Vector3.up;
    /// <summary>屋根法線（RoofUpと同じ）</summary>
    public Vector3 RoofNormal => RoofUp;

    void Start()
    {
        _startTime = Time.time;
        ResolveDefaults();
        EnsureRoofVisual();
        UpdateRoofVisual();
        if (roofSlideCollider != null)
        {
            Vector3 rn = roofSlideCollider.transform.up.normalized;
            Vector3 ru = roofSlideCollider.transform.up.normalized;
            Debug.Log($"[RoofVectors] roofNormal=({rn.x:F3},{rn.y:F3},{rn.z:F3}) roofUp=({ru.x:F3},{ru.y:F3},{ru.z:F3}) worldUp=(0,1,0)");
        }
        float burstScale = (snowPackSpawner != null) ? snowPackSpawner.pieceSize : 0.11f;
        Debug.Log($"[SnowPieceScale] kind=Burst scale=({burstScale:F3},{burstScale:F3},{burstScale:F3})");
        Debug.Log($"[AutoAvalancheState] default=OFF current={(AssiDebugUI.AutoAvalancheOff ? "OFF" : "ON")}");
        SnowPackSpawner.LastTapTime = -10f;
        SnowPackSpawner.LastRemovedCount = 0;
        SnowPackSpawner.LastPackedInRadiusBefore = 0;
        var g = GameObject.Find("TapHitGizmo");
        if (g != null) Object.Destroy(g);
        g = GameObject.Find("BurstMarker");
        if (g != null) Object.Destroy(g);
        Debug.Log($"[TapMarkerState] atStart visible=No lastTapValid=No");
    }

    void Update()
    {
        if (roofSlideCollider == null) return;

        if (snowPackSpawner != null)
        {
            int packed = snowPackSpawner.GetPackedCubeCountRealtime();
            if (_packedCountAtFull < 0 && packed > 50)
            {
                _packedCountAtFull = packed;
                _baseDepthAtFull = roofSnowDepthMeters;
            }
            if (packed > _packedCountAtFull && _packedCountAtFull > 0)
            {
                _packedCountAtFull = packed;
                _baseDepthAtFull = roofSnowDepthMeters;
            }
            float targetDepth = _baseDepthAtFull;
            if (_packedCountAtFull > 0)
                targetDepth = Mathf.Max(0.02f, _baseDepthAtFull * (float)packed / _packedCountAtFull);
            if (roofSnowDepthMeters > targetDepth && packed > 0)
            {
                _packedCountAtFull = packed;
                _baseDepthAtFull = roofSnowDepthMeters;
                targetDepth = roofSnowDepthMeters;
            }
            roofSnowDepthMeters = targetDepth;
            if (!_visualDepthRoofInitialized) { _visualDepthRoof = targetDepth; _visualDepthRoofInitialized = true; }
            _visualDepthRoof = Mathf.Lerp(_visualDepthRoof, targetDepth, roofVisualSmoothSpeed * Time.deltaTime);
            if (Time.frameCount % 60 == 0)
                Debug.Log($"[RoofDepthSync] packed={packed} packedAtFull={_packedCountAtFull} targetDepth={targetDepth:F3} visualDepth={_visualDepthRoof:F3}");
        }
        else if (!_visualDepthRoofInitialized)
        {
            _visualDepthRoof = roofSnowDepthMeters;
            _visualDepthRoofInitialized = true;
        }
        else
        {
            _visualDepthRoof = Mathf.Lerp(_visualDepthRoof, roofSnowDepthMeters, roofVisualSmoothSpeed * Time.deltaTime);
        }

        UpdateRoofVisual();

        Vector3 roofUp = roofSlideCollider.transform.up.normalized;
        AngleDeg = Vector3.Angle(roofUp, Vector3.up);
        ComputedThreshold = Mathf.Max(minThresholdMeters, baseThresholdMeters - AngleDeg * slopeFactor);

        Vector3 roofOrigin = roofSlideCollider.bounds.center;
        Vector3 roofNormal = roofSlideCollider.transform.up.normalized;
        Vector3 roofTangentDownSlope = Vector3.ProjectOnPlane(Vector3.down, roofNormal).normalized;
        if (roofTangentDownSlope.sqrMagnitude < 0.0001f) roofTangentDownSlope = -roofSlideCollider.transform.forward.normalized;
        Vector3 worldUp = Vector3.up;
        float rayLen = 2f;
        Debug.DrawRay(roofOrigin, roofNormal * rayLen, Color.red, 1f);
        Debug.DrawRay(roofOrigin, roofTangentDownSlope * rayLen, Color.yellow, 1f);
        Debug.DrawRay(roofOrigin, worldUp * rayLen, Color.blue, 1f);

        if (Time.time >= _nextRoofLogTime)
        {
            _nextRoofLogTime = Time.time + 1f;
            string packEuler = snowPackSpawner != null ? snowPackSpawner.SnowPackRootEulerString : "N/A";
            Debug.Log($"[RoofVectors] roofNormal=({roofNormal.x:F3},{roofNormal.y:F3},{roofNormal.z:F3}) roofUp=({roofUp.x:F3},{roofUp.y:F3},{roofUp.z:F3}) worldUp=({worldUp.x:F3},{worldUp.y:F3},{worldUp.z:F3}) SnowPackRootEuler={packEuler}");
            Debug.Log($"[RoofSnow] depth={roofSnowDepthMeters:F3} threshold={ComputedThreshold:F3} angleDeg={AngleDeg:F1}");
            var cd = FindFirstObjectByType<ToolCooldownManager>();
            float cdRem = cd != null ? cd.CooldownRemaining : 0f;
            float avgSlide = SnowPackSpawner.LastAvgRoofSlideDuration;
            int chainTriggers = SnowPackSpawner.LastChainTriggerCount;
            Debug.Log($"[TempoDebug] cooldownRemaining={cdRem:F2}s avgRoofSlideDuration={avgSlide:F3}s chainTriggersLastHit={chainTriggers}");
            string state = Time.time < _nextAvalancheTime ? "Avalanche" : "Freeze";
            float groundTotal = groundSnowSystem != null ? groundSnowSystem.totalSnowAmount : 0f;
            Debug.Log($"[AvalancheAudit1s] frame={Time.frameCount} t={Time.time:F2} state={state} roofDepth={roofSnowDepthMeters:F3} groundTotal={groundTotal:F3}");
        }

        if (AssiDebugUI.AutoAvalancheOff)
        {
            // デバッグトグル: 自動雪崩OFF。ボタンまたはタップのみ。
        }
        else if (Time.time >= _nextAvalancheTime && roofSnowDepthMeters >= ComputedThreshold)
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

    /// <summary>デバッグ用: F8で強制発火。閾値・クールダウンを無視して即時雪崩。</summary>
    public void ForceAvalancheNow()
    {
        if (roofSlideCollider == null) ResolveDefaults();
        if (roofSlideCollider == null) { Debug.LogWarning("[ForceAvalancheNow] roofSlideCollider が null"); return; }
        TriggerAvalanche();
        Debug.Log("[ForceAvalancheNow] fired by F8");
    }

    /// <summary>タップ時に呼ぶ。局所雪崩: hit中心半径内のグリッドセルを削り、Burstを斜面方向に流す。</summary>
    public void RequestTapSlide(Vector3 tapWorldPoint)
    {
        if (roofSlideCollider == null || snowPackSpawner == null) return;
        snowPackSpawner.LogNearestPieceToTap(tapWorldPoint);
        _nextAvalancheTime = Time.time + 0.3f;
        snowPackSpawner.PlayLocalAvalancheAt(tapWorldPoint, 0.6f, localAvalancheSlideSpeed);
        int removed = SnowPackSpawner.LastRemovedCount;
        if (removed > 0)
        {
            Vector3 roofUp = roofSlideCollider.transform.up.normalized;
            Vector3 slopeDir = Vector3.ProjectOnPlane(Vector3.down, roofUp).normalized;
            if (slopeDir.sqrMagnitude < 0.0001f) slopeDir = -roofSlideCollider.transform.forward.normalized;
            SpawnLocalBurstAt(tapWorldPoint, removed, slopeDir);
        }
        int packedAfter = snowPackSpawner != null ? snowPackSpawner.GetPackedCubeCountRealtime() : -1;
        Debug.Log($"[TapSlide] tapPoint={tapWorldPoint} removed={removed} packedAfter={packedAfter} (depth synced from packed in Update)");

        string sizeStr = removed <= 3 ? "Small" : (removed <= 12 ? "Medium" : "Large");
        int burstCount = removed > 0 ? Mathf.Min(removed, burstChunkCount) : 0;
        int movedCount = removed + burstCount;
        bool reachedGround = removed > 0;
        SnowLoopLogCapture.AppendToAssiReport("=== TAP AVALANCHE ===");
        SnowLoopLogCapture.AppendToAssiReport($"tapPos=({tapWorldPoint.x:F3},{tapWorldPoint.y:F3},{tapWorldPoint.z:F3}) radius=0.6 removedCount={removed}");
        SnowLoopLogCapture.AppendToAssiReport($"size={sizeStr}");
        SnowLoopLogCapture.AppendToAssiReport($"movedCount={movedCount} reachedGround={reachedGround}");
    }

    static int _spawnErrorCount;
    static float _lastSpawnErrorLog;

    public void SpawnLocalBurstAt(Vector3 origin, int removedCount, Vector3 slopeDir)
    {
        if (roofSlideCollider == null) return;
        int count = Mathf.Clamp(removedCount, 1, burstChunkCount);
        Vector3 roofN = roofSlideCollider.transform.up.normalized;
        float smallLift = 0.2f;
        float roofSlideTime = 0.4f;
        int spawnedOk = 0, spawnFailed = 0;
        for (int i = 0; i < count; i++)
        {
            try
            {
                var chunk = AcquireChunk();
                if (chunk == null)
                {
                    spawnFailed++;
                    continue;
                }
                Vector3 jitter = Vector3.ProjectOnPlane(Random.insideUnitSphere, roofN) * burstSpread * 0.5f;
                Vector3 p = origin + roofN * 0.05f + jitter * 0.3f;
                Vector3 vel = (slopeDir + jitter * 0.3f).normalized * burstChunkSpeed + roofN * smallLift;
                float perChunkDeposit = Mathf.Max(0.001f, burstGroundDepositPerChunk);
                SnowPackSpawner.RecordRoofSlideDuration(roofSlideTime);
                chunk.Activate(p, vel, Mathf.Max(burstChunkLife * 0.8f, 1.5f), groundSnowSystem, groundMask, perChunkDeposit, roofN, roofSlideTime);
                float s = snowPackSpawner != null ? snowPackSpawner.pieceSize : 0.11f;
                chunk.transform.localScale = Vector3.one * s * 0.8f;
                spawnedOk++;
            }
            catch (System.Exception ex)
            {
                spawnFailed++;
                _spawnErrorCount++;
                if (Time.time - _lastSpawnErrorLog >= 2f)
                {
                    _lastSpawnErrorLog = Time.time;
                    Debug.LogWarning($"[Hit] spawn exception (rate-limited): {ex.Message}");
                }
            }
        }
        Debug.Log($"[Hit] removedCount={removedCount} spawnedChunks={count} spawnedOk={spawnedOk} spawnFailed={spawnFailed}");
    }

    void TriggerAvalanche()
    {
        float before = roofSnowDepthMeters;
        float after = Mathf.Max(0f, before * Mathf.Clamp01(avalancheRetainRatio));
        float burstAmount = Mathf.Max(0f, before - after);

        // C) before値を雪崩処理前にとる。Realtimeで統一（矛盾防止）
        float packBefore = snowPackSpawner != null ? snowPackSpawner.packDepthMeters : before;
        int packedBefore = snowPackSpawner != null ? snowPackSpawner.GetPackedCubeCountRealtime() : -1;

        Vector3 roofUp = roofSlideCollider.transform.up.normalized;
        Vector3 slopeDir = Vector3.ProjectOnPlane(Vector3.down, roofUp).normalized;
        if (slopeDir.sqrMagnitude < 0.0001f) slopeDir = -roofSlideCollider.transform.forward.normalized;

        if (snowPackSpawner != null)
            snowPackSpawner.PlayAvalancheSlideVisual(burstAmount, slopeDir * avalancheSlideOffset, avalancheSlideDuration);

        roofSnowDepthMeters = after;
        _nextAvalancheTime = Time.time + Mathf.Max(0.2f, avalancheCooldownSeconds);
        UpdateRoofVisual();

        int packedAfter = snowPackSpawner != null ? snowPackSpawner.GetPackedCubeCountRealtime() : -1;
        if (packedBefore >= 0 && packedAfter >= 0 && packedAfter > packedBefore)
            Debug.LogWarning($"[AvalancheBeforeAfter] WARNING packedIncreased {packedBefore}->{packedAfter} (SpawnFreeze中でも増えた場合要調査)");
        Debug.Log($"[AvalancheBeforeAfter] beforeDepth={before:F3} afterDepth={after:F3} packedCubeCountBefore={packedBefore} packedCubeCountAfter={packedAfter} burstAmount={burstAmount:F3}");

        SpawnAvalancheBurstVisual(burstAmount);

        Vector3 burstVel = slopeDir * burstChunkSpeed;
        Vector3 roofFwd = roofSlideCollider.transform.forward.normalized;
        Vector3 origin = roofSlideCollider.bounds.center + roofUp * roofSlideCollider.bounds.extents.y;
        Debug.DrawRay(origin, burstVel * 2f, Color.red, 1f, false);
        Debug.DrawRay(origin, roofFwd * 2f, Color.green, 1f, false);
        Debug.DrawRay(origin, roofUp * 2f, Color.blue, 1f, false);
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
            float hRaw = Mathf.Max(0.02f, _visualDepthRoof);
            float h = hRaw * roofSnowVisualThicknessScale;
            Vector3 size = new Vector3(Mathf.Max(0.1f, box.size.x), h, Mathf.Max(0.1f, box.size.z));
            Vector3 center = box.center + Vector3.up * (box.size.y * 0.5f + h * 0.5f);
            _roofLayer.localPosition = center;
            _roofLayer.rotation = roofSlideCollider.transform.rotation;
            _roofLayer.localScale = size;
            if (!_snowRenderThicknessLogOnce)
            {
                _snowRenderThicknessLogOnce = true;
                string path = roofSlideCollider != null ? GetTransformPath(roofSlideCollider.transform) + "/RoofSnowLayer" : "RoofSnowLayer";
                SnowLoopLogCapture.AppendToAssiReport("=== SNOW_RENDER_THICKNESS_HALF (RoofSnowLayer) ===");
                SnowLoopLogCapture.AppendToAssiReport($"targetPath={path}");
                SnowLoopLogCapture.AppendToAssiReport($"beforeScale=(...,{hRaw:F3},...) afterScale=(...,{h:F3},...)");
            }
        }
        else
        {
            Bounds b = roofSlideCollider.bounds;
            float hRaw = Mathf.Max(0.02f, _visualDepthRoof);
            float h = hRaw * roofSnowVisualThicknessScale;
            _roofLayer.position = b.center + roofSlideCollider.transform.up * (b.extents.y + h * 0.5f);
            _roofLayer.rotation = roofSlideCollider.transform.rotation;
            _roofLayer.localScale = new Vector3(Mathf.Max(0.1f, b.size.x), h, Mathf.Max(0.1f, b.size.z));
            if (!_snowRenderThicknessLogOnce)
            {
                _snowRenderThicknessLogOnce = true;
                string path = roofSlideCollider != null ? GetTransformPath(roofSlideCollider.transform) + "/RoofSnowLayer" : "RoofSnowLayer";
                SnowLoopLogCapture.AppendToAssiReport("=== SNOW_RENDER_THICKNESS_HALF (RoofSnowLayer) ===");
                SnowLoopLogCapture.AppendToAssiReport($"targetPath={path}");
                SnowLoopLogCapture.AppendToAssiReport($"beforeScale=(...,{hRaw:F3},...) afterScale=(...,{h:F3},...)");
            }
        }
    }

    static string GetTransformPath(Transform t)
    {
        if (t == null) return "?";
        var parts = new System.Collections.Generic.List<string>();
        var cur = t;
        while (cur != null) { parts.Add(cur.name); cur = cur.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }

    void SpawnAvalancheBurstVisual(float burstAmount)
    {
        if (roofSlideCollider == null) return;
        Vector3 roofUp = roofSlideCollider.transform.up.normalized;
        Vector3 slopeDir = Vector3.ProjectOnPlane(Vector3.down, roofUp).normalized;
        if (slopeDir.sqrMagnitude < 0.0001f) slopeDir = -roofSlideCollider.transform.forward.normalized;
        Bounds b = roofSlideCollider.bounds;
        Vector3 burstPos = new Vector3(b.center.x, b.max.y + 0.1f, b.center.z);
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "BurstMarker";
        marker.transform.position = burstPos;
        marker.transform.localScale = Vector3.one * 0.5f;
        if (marker.GetComponent<Collider>() != null) marker.GetComponent<Collider>().enabled = false;
        var r = marker.GetComponent<Renderer>();
        if (r != null) r.material.color = Color.red;
        UnityEngine.Object.Destroy(marker, 1f);
        int count = Mathf.Max(6, burstChunkCount);
        float roofSlideTime = 0.35f;
        for (int i = 0; i < count; i++)
        {
            var chunk = AcquireChunk();
            Vector3 p = new Vector3(
                Random.Range(b.min.x, b.max.x),
                b.max.y + 0.05f,
                Random.Range(b.min.z, b.max.z));
            Vector3 jitter = Vector3.ProjectOnPlane(Random.insideUnitSphere, roofUp) * burstSpread;
            Vector3 vel = (slopeDir + jitter * 0.25f).normalized * burstChunkSpeed + roofUp * 0.15f;
            float perChunkDeposit = Mathf.Max(0.001f, burstGroundDepositPerChunk + burstAmount * 0.005f / count);
            chunk.Activate(p, vel, burstChunkLife, groundSnowSystem, groundMask, perChunkDeposit, roofUp, roofSlideTime);
            float unifiedScale = (snowPackSpawner != null) ? snowPackSpawner.pieceSize : 0.11f;
            chunk.transform.localScale = Vector3.one * unifiedScale; // Pool復帰時も再適用
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
        float unifiedScale = (snowPackSpawner != null) ? snowPackSpawner.pieceSize : 0.11f;
        go.transform.localScale = Vector3.one * unifiedScale;
        if (!_burstScaleLogOnce) { _burstScaleLogOnce = true; Debug.Log($"[SnowPieceScale] kind=Burst scale=({unifiedScale:F3},{unifiedScale:F3},{unifiedScale:F3})"); }
        var col = go.GetComponent<Collider>();
        if (col != null) col.enabled = false;
        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = sh != null ? new Material(sh) : null;
            if (mat != null)
            {
                mat.color = new Color(0.3f, 0.7f, 1f); // 雪崩中=シアンで視認性
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
