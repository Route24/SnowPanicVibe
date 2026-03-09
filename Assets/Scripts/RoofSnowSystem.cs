using System.Collections.Generic;
using UnityEngine;

/// <summary>Material の色を安全に Get/Set。URP は _BaseColor、Built-in は _Color。どちらも無い場合はデフォルト返却／何もしない。</summary>
public static class MaterialColorHelper
{
    public static void SetColorSafe(Material mat, Color c)
    {
        if (mat == null) return;
        try
        {
            if (mat.HasProperty("_BaseColor")) { mat.SetColor("_BaseColor", c); return; }
            if (mat.HasProperty("_Color")) { mat.SetColor("_Color", c); return; }
        }
        catch { }
    }

    public static Color GetColorSafe(Material mat, Color fallback = default)
    {
        if (mat == null) return fallback;
        try
        {
            if (mat.HasProperty("_BaseColor")) return mat.GetColor("_BaseColor");
            if (mat.HasProperty("_Color")) return mat.GetColor("_Color");
        }
        catch { }
        return fallback;
    }
}

/// <summary>
/// Source-of-truth roof accumulation + auto avalanche trigger.
/// Roof snow uses LOCAL cleared patches (mask) - no global thickness pulsing.
/// </summary>
[DefaultExecutionOrder(100)]
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
    [Tooltip("Constant snow surface thickness (no global pulsing).")]
    public float roofSnowConstantThickness = 0.08f;

    [Header("Burst visual")]
    public int burstChunkCount = 36;
    public float burstChunkLife = 1.8f;
    public float burstChunkSpeed = 2.2f;
    [Tooltip("Hit radius for initial detach. 巨大崩壊型: 大きめで主塊を強調。")]
    [Range(0.6f, 1.2f)] public float hitRadiusR = 1.05f;
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
    float _lastSuppressedLogTime;
    RoofSnowMaskController _maskController;
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
    /// <summary>packedTotalに連動する視覚量（0=非表示, 1=フル）。デバッグHUD用。</summary>
    public float RoofSnowVisualAmount => _roofSnowVisualAmount;
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
        SnowPackSpawner.LastTapPowderMoved = 0;
        SnowPackSpawner.LastTapSlabMoved = 0;
        SnowPackSpawner.LastTapBaseMoved = 0;
        SnowPackSpawner.LastTapSmallCluster = 0;
        SnowPackSpawner.LastTapMidCluster = 0;
        SnowPackSpawner.LastTapLargeCluster = 0;
        var g = GameObject.Find("TapHitGizmo");
        if (g != null) Object.Destroy(g);
        g = GameObject.Find("BurstMarker");
        if (g != null) Object.Destroy(g);
        Debug.Log($"[TapMarkerState] atStart visible=No lastTapValid=No");
    }

    bool _packedZeroMaskCleared;
    int _packedTotalAtStart = -1;
    float _roofSnowVisualAmount = 1f;
    const float VisualFadeSpeed = 4f;
    float _packedZeroSweepDone;
    [Header("Detached roof-stuck")]
    [Tooltip("rb.velocity < この値が継続で強制消去。")]
    public float detachedStuckVelThreshold = 0.05f;
    [Tooltip("velocity低速がこの秒数続いたら強制消去。止まり雪対策で3秒（再タップ猶予）。")]
    public float detachedStuckVelSeconds = 3f;
    [Tooltip("rb.IsSleeping()がこの秒数続いたら強制消去。止まり雪対策で3秒。")]
    public float detachedStuckSleepSeconds = 3f;
    readonly Dictionary<SnowPackFallingPiece, float> _fallingStuckTimer = new Dictionary<SnowPackFallingPiece, float>();

    void Update()
    {
        if (roofSlideCollider == null) return;

        int packed = snowPackSpawner != null ? snowPackSpawner.GetPackedCubeCountRealtime() : -1;
        if (packed > 0 && _packedTotalAtStart < 0)
            _packedTotalAtStart = packed;
        if (packed > 0 && packed > _packedTotalAtStart)
            _packedTotalAtStart = packed;

        float targetAmount = (packed <= 0 || _packedTotalAtStart <= 0) ? 0f
            : Mathf.Clamp01(packed / (float)_packedTotalAtStart);
        _roofSnowVisualAmount = Mathf.MoveTowards(_roofSnowVisualAmount, targetAmount, VisualFadeSpeed * Time.deltaTime);

        if (packed == 0)
        {
            if (_packedZeroSweepDone <= 0f)
            {
                SweepRoofDebris();
                _packedZeroSweepDone = 1f;
            }
            if (!_packedZeroMaskCleared && _maskController != null)
            {
                _packedZeroMaskCleared = true;
                _maskController.ClearEntireMask();
            }
        }
        else if (packed > 0)
        {
            _packedZeroSweepDone = 0f;
            if (_packedZeroMaskCleared)
            {
                _packedZeroMaskCleared = false;
                if (_maskController != null) _maskController.ResetMaskToFull();
            }
        }

        CheckDetachedRoofStuck(Time.deltaTime);

        UpdateRoofVisual();
        UpdateMaskShaderParams();

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

    /// <summary>Paint a cleared patch at hit location. Call from LocalAvalanche (main hit) and chain waves.</summary>
    public void PaintClearedPatchAt(Vector3 worldPoint, float radiusMeters = -1f)
    {
        if (_maskController != null)
            _maskController.PaintEraseAt(worldPoint, radiusMeters);
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
        snowPackSpawner.PlayLocalAvalancheAt(tapWorldPoint, hitRadiusR, localAvalancheSlideSpeed);
        int removed = SnowPackSpawner.LastRemovedCount;
        if (removed > 0)
        {
            Vector3 roofUp = roofSlideCollider.transform.up.normalized;
            Vector3 slopeDir = Vector3.ProjectOnPlane(Vector3.down, roofUp).normalized;
            if (slopeDir.sqrMagnitude < 0.0001f) slopeDir = -roofSlideCollider.transform.forward.normalized;
            SpawnLocalBurstAt(tapWorldPoint, removed, slopeDir);
        }
        int packedAfter = snowPackSpawner != null ? snowPackSpawner.GetPackedCubeCountRealtime() : -1;
        Debug.Log($"[TapSlide] tapPoint={tapWorldPoint} removed={removed} packedAfter={packedAfter} primary_detach_count={removed} primary_cluster_size={removed} largest_fall_group={removed} active_snow_visual=RoofSnowLayer+SnowPackPiece active_snow_break_logic=SnowPackSpawner.HandleTap+DetachInRadius");
        PaintClearedPatchAt(tapWorldPoint, hitRadiusR);
        if (removed >= 60) AvalancheFeedback.TriggerSmallShakeIfLarge(removed);

        string sizeStr = removed <= 3 ? "Small" : (removed <= 12 ? "Medium" : "Large");
        int burstCount = removed > 0 ? Mathf.Min(removed, burstChunkCount) : 0;
        int movedCount = removed + burstCount;
        bool reachedGround = removed > 0;
        SnowLoopLogCapture.AppendToAssiReport("=== TAP AVALANCHE ===");
        SnowLoopLogCapture.AppendToAssiReport($"tapPos=({tapWorldPoint.x:F3},{tapWorldPoint.y:F3},{tapWorldPoint.z:F3}) radius=0.6 removedCount={removed}");
        SnowLoopLogCapture.AppendToAssiReport($"size={sizeStr}");
        SnowLoopLogCapture.AppendToAssiReport($"movedCount={movedCount} reachedGround={reachedGround}");
        SnowLoopLogCapture.AppendToAssiReport("=== TAP AVALANCHE MIX ===");
        SnowLoopLogCapture.AppendToAssiReport($"Powder moved={SnowPackSpawner.LastTapPowderMoved}");
        SnowLoopLogCapture.AppendToAssiReport($"Slab moved={SnowPackSpawner.LastTapSlabMoved}");
        SnowLoopLogCapture.AppendToAssiReport($"Base moved={SnowPackSpawner.LastTapBaseMoved}");
        SnowLoopLogCapture.AppendToAssiReport($"smallCluster={SnowPackSpawner.LastTapSmallCluster}");
        SnowLoopLogCapture.AppendToAssiReport($"midCluster={SnowPackSpawner.LastTapMidCluster}");
        SnowLoopLogCapture.AppendToAssiReport($"largeCluster={SnowPackSpawner.LastTapLargeCluster}");
        SnowLoopLogCapture.AppendToAssiReport("=== VISUAL RESULT CHECK ===");
        bool hasSmall = SnowPackSpawner.LastTapSmallCluster > 0;
        bool hasMid = SnowPackSpawner.LastTapMidCluster > 0;
        bool hasLarge = SnowPackSpawner.LastTapLargeCluster > 0;
        bool threeVarieties = hasSmall && hasMid && hasLarge;
        SnowLoopLogCapture.AppendToAssiReport($"小粒/中塊/大塊の3種類が出たか: {(threeVarieties ? "Yes" : "No")}");
        SnowLoopLogCapture.AppendToAssiReport($"根拠: smallCluster={SnowPackSpawner.LastTapSmallCluster} midCluster={SnowPackSpawner.LastTapMidCluster} largeCluster={SnowPackSpawner.LastTapLargeCluster}");
    }

    static int _spawnErrorCount;
    static float _lastSpawnErrorLog;

    public void SpawnLocalBurstAt(Vector3 origin, int removedCount, Vector3 slopeDir)
    {
        if (roofSlideCollider == null) return;
        if (removedCount == 1)
            PaintClearedPatchAt(origin, 0.2f);
        int count = Mathf.Clamp(removedCount, 1, burstChunkCount);
        int smallC = SnowPackSpawner.LastTapSmallCluster;
        int midC = SnowPackSpawner.LastTapMidCluster;
        int largeC = SnowPackSpawner.LastTapLargeCluster;
        if (smallC + midC + largeC <= 0) { smallC = count / 3; midC = count / 3; largeC = count - smallC - midC; }
        Vector3 roofN = roofSlideCollider.transform.up.normalized;
        float smallLift = 0.2f;
        float roofSlideTime = 0.4f;
        int spawnedOk = 0, spawnFailed = 0;
        float baseScale = snowPackSpawner != null ? snowPackSpawner.pieceSize : 0.11f;
        for (int i = 0; i < count; i++)
        {
            float scaleMul = i < smallC ? 0.65f : (i < smallC + midC ? 1f : 1.35f);
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
                chunk.Activate(p, vel, Mathf.Max(burstChunkLife * 0.8f, 0.8f), groundSnowSystem, groundMask, perChunkDeposit, roofN, roofSlideTime);
                chunk.transform.localScale = Vector3.one * baseScale * 1.2f * scaleMul;
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
        int pooledNow = _chunkPool != null ? _chunkPool.Count : 0;
        Debug.Log($"[HitAudit] removedCount={removedCount} spawnedChunks={count} spawnedOk={spawnedOk} spawnFailed={spawnFailed} pooledNow={pooledNow}");
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
        if (snowPackSpawner != null)
            snowPackSpawner.EnsureSnowPackVisualHierarchy();
    }

    void EnsureRoofVisual()
    {
        if (roofSlideCollider == null) return;
        var child = roofSlideCollider.transform.Find("RoofSnowLayer");
        if (child != null)
        {
            _roofLayer = child;
            var rend = child.GetComponent<Renderer>();
            _roofLayerMat = rend != null ? rend.sharedMaterial : null;
            _maskController = child.GetComponent<RoofSnowMaskController>();
            if (_maskController == null) _maskController = child.gameObject.AddComponent<RoofSnowMaskController>();
            EnsureMaskMaterialAndInit(rend);
            LogRoofSnowSurface(rend);
            return;
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "RoofSnowLayer";
        go.transform.SetParent(roofSlideCollider.transform, false);
        var col = go.GetComponent<Collider>();
        if (col != null) col.enabled = false;
        var goRend = go.GetComponent<Renderer>();
        _maskController = go.GetComponent<RoofSnowMaskController>();
        if (_maskController == null) _maskController = go.AddComponent<RoofSnowMaskController>();
        EnsureMaskMaterialAndInit(goRend);
        _roofLayer = go.transform;
        LogRoofSnowSurface(goRend);
    }

    void EnsureMaskMaterialAndInit(Renderer rend)
    {
        if (rend == null || snowPackSpawner == null) return;
        bool needMask = _roofLayerMat == null;
        try { if (!needMask) needMask = !_roofLayerMat.HasProperty("_SnowMask"); } catch { needMask = true; }
        if (needMask)
        {
            Shader sh = Shader.Find("SnowPanic/RoofSnowMask");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _roofLayerMat = sh != null ? new Material(sh) : null;
            if (_roofLayerMat != null)
            {
                MaterialColorHelper.SetColorSafe(_roofLayerMat, roofSnowColor);
                if (_roofLayerMat.HasProperty("_SnowIntensity"))
                    _roofLayerMat.SetFloat("_SnowIntensity", _roofSnowVisualAmount);
                if (rend != null) rend.sharedMaterial = _roofLayerMat;
            }
        }
        try
        {
            if (_maskController != null && _roofLayerMat != null && snowPackSpawner != null && _roofLayerMat.HasProperty("_SnowMask"))
                _maskController.Init(_roofLayerMat, snowPackSpawner, roofSlideCollider);
        }
        catch (System.Exception) { _maskController = null; }
    }

    void LogRoofSnowSurface(Renderer rend)
    {
        if (rend == null) return;
        string path = GetTransformPath(rend.transform);
        var mat = rend.sharedMaterial;
        string matName = mat != null ? mat.name : "null";
        Debug.Log($"[RoofSnowSurface] path={path} renderer={rend.GetInstanceID()} material={matName}");
    }

    void UpdateRoofVisual()
    {
        if (roofSlideCollider == null) return;
        if (_roofLayer == null) EnsureRoofVisual();
        if (_roofLayer == null) return;

        float h = Mathf.Max(0.02f, roofSnowConstantThickness);
        if (roofSlideCollider is BoxCollider box)
        {
            Vector3 size = new Vector3(Mathf.Max(0.1f, box.size.x), h, Mathf.Max(0.1f, box.size.z));
            Vector3 center = box.center + Vector3.up * (box.size.y * 0.5f + h * 0.5f);
            _roofLayer.localPosition = center;
            _roofLayer.rotation = roofSlideCollider.transform.rotation;
            _roofLayer.localScale = size;
        }
        else
        {
            Bounds b = roofSlideCollider.bounds;
            _roofLayer.position = b.center + roofSlideCollider.transform.up * (b.extents.y + h * 0.5f);
            _roofLayer.rotation = roofSlideCollider.transform.rotation;
            _roofLayer.localScale = new Vector3(Mathf.Max(0.1f, b.size.x), h, Mathf.Max(0.1f, b.size.z));
        }
    }

    void UpdateMaskShaderParams()
    {
        if (_roofLayerMat == null || snowPackSpawner == null) return;
        try
        {
            if (!_roofLayerMat.HasProperty("_RoofCenter")) return;
        }
        catch { return; }
        var s = snowPackSpawner;
        if (s.RoofWidth <= 0f || s.RoofLength <= 0f) return;
        if (_maskController != null && !_maskController.IsInitialized && _roofLayerMat.HasProperty("_SnowMask"))
        {
            try { _maskController.Init(_roofLayerMat, s, roofSlideCollider); } catch { }
        }
        _roofLayerMat.SetVector("_RoofCenter", s.RoofCenter);
        _roofLayerMat.SetVector("_RoofR", s.RoofR);
        _roofLayerMat.SetVector("_RoofF", s.RoofF);
        _roofLayerMat.SetFloat("_RoofWidth", s.RoofWidth);
        _roofLayerMat.SetFloat("_RoofLength", s.RoofLength);
        if (_roofLayerMat.HasProperty("_SnowIntensity"))
            _roofLayerMat.SetFloat("_SnowIntensity", _roofSnowVisualAmount);
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
        if (r != null && r.material != null) MaterialColorHelper.SetColorSafe(r.material, Color.red);
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

    /// <summary>タップフォールバック用。画面位置から最も近い屋根上デブリを返す（maxPixelDist以内）。</summary>
    public bool TryGetClosestDebrisToScreen(Camera cam, Vector2 screenPos, float maxPixelDist,
        out MvpSnowChunkMotion chunk, out SnowPackFallingPiece falling)
    {
        chunk = null;
        falling = null;
        if (cam == null || roofSlideCollider == null) return false;
        float bestSq = maxPixelDist * maxPixelDist;
        var roofBounds = roofSlideCollider.bounds;
        roofBounds.Expand(0.8f);

        for (int i = 0; i < _chunkPool.Count; i++)
        {
            var c = _chunkPool[i];
            if (c == null || !c.gameObject.activeSelf) continue;
            if (!roofBounds.Contains(c.transform.position)) continue;
            Vector3 vp = cam.WorldToScreenPoint(c.transform.position);
            if (vp.z <= 0f) continue;
            float dx = vp.x - screenPos.x, dy = vp.y - screenPos.y;
            float sq = dx * dx + dy * dy;
            if (sq < bestSq) { bestSq = sq; chunk = c; falling = null; }
        }

        var hits = Physics.OverlapBox(roofSlideCollider.bounds.center,
            roofSlideCollider.bounds.extents + Vector3.one * 0.5f,
            roofSlideCollider.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        foreach (var col in hits)
        {
            if (col == null) continue;
            var f = col.GetComponentInParent<SnowPackFallingPiece>();
            if (f == null) continue;
            Vector3 vp = cam.WorldToScreenPoint(f.transform.position);
            if (vp.z <= 0f) continue;
            float dx = vp.x - screenPos.x, dy = vp.y - screenPos.y;
            float sq = dx * dx + dy * dy;
            if (sq < bestSq) { bestSq = sq; chunk = null; falling = f; }
        }
        return chunk != null || falling != null;
    }

    void CheckDetachedRoofStuck(float dt)
    {
        if (roofSlideCollider == null || dt <= 0f) return;
        int packed = snowPackSpawner != null ? snowPackSpawner.GetPackedCubeCountRealtime() : -1;
        bool packedSmall = packed >= 0 && packed <= 5; // 止まり雪対策: 残り少ない時は速めに消去
        float velSec = packedSmall ? 1f : detachedStuckVelSeconds;
        float sleepSec = packedSmall ? 1f : detachedStuckSleepSeconds;

        Bounds roofBounds = roofSlideCollider.bounds;
        roofBounds.Expand(0.5f);
        var toRemove = new List<SnowPackFallingPiece>();
        foreach (var f in DetachedSnowRegistry.Falling)
        {
            if (f == null || !f.gameObject.activeInHierarchy) { toRemove.Add(f); continue; }
            var rb = f.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic) continue;
            if (!roofBounds.Contains(f.transform.position)) { _fallingStuckTimer.Remove(f); continue; }
            float speed = rb.linearVelocity.magnitude;
            bool sleeping = rb.IsSleeping();
            bool stuck = speed < detachedStuckVelThreshold || sleeping;
            if (!stuck) { _fallingStuckTimer.Remove(f); continue; }
            float t = _fallingStuckTimer.TryGetValue(f, out var v) ? v + dt : dt;
            _fallingStuckTimer[f] = t;
            float threshold = sleeping ? sleepSec : velSec;
            if (t >= threshold)
            {
                f.ForceDespawnFromCentralRoofStuck();
                toRemove.Add(f);
            }
        }
        foreach (var f in toRemove)
            _fallingStuckTimer.Remove(f);
    }

    /// <summary>packed=0時: 屋根上デブリを一括Despawn。ChunkPool + OverlapBoxで漏れなく。</summary>
    void SweepRoofDebris()
    {
        int removedCount = 0;
        for (int i = 0; i < _chunkPool.Count; i++)
        {
            var c = _chunkPool[i];
            if (c != null && c.gameObject.activeSelf)
            {
                c.ForceDespawn();
                removedCount++;
            }
        }
        if (roofSlideCollider != null)
        {
            Bounds b = roofSlideCollider.bounds;
            Vector3 halfExtents = b.extents + Vector3.one * 0.5f;
            Collider[] hits = Physics.OverlapBox(b.center, halfExtents, roofSlideCollider.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            foreach (var col in hits)
            {
                if (col == null) continue;
                var chunk = col.GetComponent<MvpSnowChunkMotion>();
                if (chunk != null && chunk.gameObject.activeSelf)
                {
                    chunk.ForceDespawn();
                    removedCount++;
                    continue;
                }
                var falling = col.GetComponentInParent<SnowPackFallingPiece>();
                if (falling != null)
                {
                    falling.ForceDespawnFromSweep();
                    removedCount++;
                }
            }
        }
        if (removedCount > 0)
            Debug.Log($"[RoofCleanup] removedCount={removedCount}");
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
        if (col != null) { col.isTrigger = false; col.enabled = true; }
        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = sh != null ? new Material(sh) : null;
            if (mat != null)
            {
                MaterialColorHelper.SetColorSafe(mat, new Color(0.3f, 0.7f, 1f)); // 雪崩中=シアンで視認性
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
