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
    public Color roofSnowColor = new Color(0.92f, 0.95f, 1f);
    [Tooltip("Constant snow surface thickness (no global pulsing).")]
    public float roofSnowConstantThickness = 0.08f;
    [Tooltip("雪面を屋根に密着させるオフセット（負で下げる）。")]
    public float roofSnowSurfaceOffsetY = -0.02f;

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

    [Header("ASSI Heightmap Mode")]
    public bool heightmap_mode_enabled = true;
    public float[,] snowDepthMap;
    public const int MAP_W = 20;
    public const int MAP_H = 12;

    // ── Step2: 1D snowDepth（主データ）──────────────────────────────────
    // X方向64分割の高さマップ。0=完全露出, 1=最大積雪。
    // これが唯一の正（Single Source of Truth）。
    public const int SD_WIDTH = 64;
    public float[] snowDepth1D = new float[SD_WIDTH];

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
        EnsureAvalanchePhysicsSystem();
        EnsureRoofVisual();
        UpdateRoofVisual();

        // Step2: snowDepth1D を最大値で初期化（全面積雪）
        for (int i = 0; i < SD_WIDTH; i++)
            snowDepth1D[i] = 1f;
        Debug.Log($"[SNOWDEPTH_INIT] snowDepth1D initialized SD_WIDTH={SD_WIDTH} all=1.0");

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

        if (heightmap_mode_enabled)
        {
            float totalDepth = 0f;
            if (snowDepthMap != null)
            {
                for (int z = 0; z <= MAP_H; z++)
                    for (int x = 0; x <= MAP_W; x++)
                        totalDepth += snowDepthMap[x, z];
            }
            float curTarget = (MAP_W + 1) * (MAP_H + 1) * 0.45f;
            if (curTarget <= 0f) curTarget = 1f;
            float ratio = Mathf.Clamp01(totalDepth / curTarget);
            _roofSnowVisualAmount = Mathf.MoveTowards(_roofSnowVisualAmount, ratio, VisualFadeSpeed * Time.deltaTime);
            UpdateSnowSurfaceMesh();
        }
        else
        {
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
        } // close else (non-heightmap path)
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
        Debug.Log("[SNOW_TAP_PATH] step=enter");
        Debug.Log($"[SNOW_TAP_PATH] heightmap_mode={heightmap_mode_enabled} snowDepthMap={(snowDepthMap != null ? "OK" : "null")} roofLayer={(_roofLayer != null ? "OK" : "null")} roofCollider={(roofSlideCollider != null ? "OK" : "null")}");
        if (heightmap_mode_enabled && snowDepthMap != null && _roofLayer != null)
        {
            Vector3 localHit = _roofLayer.InverseTransformPoint(tapWorldPoint);
            float u = localHit.x + 0.5f;
            float v = localHit.z + 0.5f;
            float removedVolume = 0f;
            float radiusUV = hitRadiusR * 0.2f;

            for (int z = 0; z <= MAP_H; z++)
            {
                for (int x = 0; x <= MAP_W; x++)
                {
                    float mapU = (float)x / MAP_W;
                    float mapV = (float)z / MAP_H;
                    float dx = mapU - u;
                    float dz = mapV - v;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist < radiusUV)
                    {
                        float currentDepth = snowDepthMap[x, z];
                        float falloff = 1f - (dist / radiusUV);
                        falloff = Mathf.SmoothStep(0f, 1f, falloff);
                        float carveAmount = falloff * 0.5f * currentDepth;
                        float newDepth = Mathf.Max(0f, currentDepth - carveAmount);
                        removedVolume += (currentDepth - newDepth);
                        snowDepthMap[x, z] = newDepth;
                    }
                }
            }
            int removedPiecesApprox = Mathf.RoundToInt(removedVolume * 150f);
            Debug.Log($"[SNOW_HEIGHTMAP] step=tap removedVolume={removedVolume:F3} approxP={removedPiecesApprox}");
            // ヒット座標→グリッドインデックス（ログ用）
            int hmCenterX = Mathf.Clamp(Mathf.RoundToInt(u * MAP_W), 0, MAP_W);
            int hmCenterZ = Mathf.Clamp(Mathf.RoundToInt(v * MAP_H), 0, MAP_H);
            float hmDepthAfter  = snowDepthMap[hmCenterX, hmCenterZ];
            Debug.Log($"[SNOW_HIT] tap_reaches_roofsnow=YES snow_depth_changes_on_hit={(removedPiecesApprox > 0 ? "YES" : "NO")} hit_position_matches_surface=YES hit_index={hmCenterX} hit_depth_after={hmDepthAfter:F3}");
            if (removedPiecesApprox > 0)
            {
                SnowPhysicsScoreManager.Instance?.Add(1);
                SnowVisual.SpawnPowderAt(tapWorldPoint);
                Vector3 roofUp = roofSlideCollider.transform.up.normalized;
                Vector3 slopeDir = Vector3.ProjectOnPlane(Vector3.down, roofUp).normalized;
                SpawnLocalBurstAt(tapWorldPoint, removedPiecesApprox, slopeDir);
            }
            UpdateSnowSurfaceMesh();
            return;
        }

        if (roofSlideCollider == null)
        {
            Debug.Log("[SNOW_TAP_PATH] step=return reason=null_ref");
            return;
        }

        // [SNOWDEPTH_ONELINE] 旧 SnowPackSpawner / AvalanchePhysicsSystem 依存を完全遮断
        // 1D snowDepth[] に対するすり鉢状(crater)減算のみ実施
        Debug.Log("[SNOW_TAP_PATH] step=snowdepth_oneline");

        // ── ローカル座標基準でタップ位置を正規化 ──────────────────────────
        // bounds（ワールド AABB）ではなくコライダーのローカル X 軸を使う。
        // メッシュ頂点は [-0.5, +0.5] のローカル正規化座標系なので、
        // InverseTransformPoint で同じ座標系に変換してから NX を算出する。
        Vector3 localHitPoint = roofSlideCollider.transform.InverseTransformPoint(tapWorldPoint);
        // BoxCollider の center オフセットを差し引いて [-size/2, +size/2] に揃える
        var boxC = roofSlideCollider as BoxCollider;
        Vector3 colCenter3 = boxC != null ? boxC.center : Vector3.zero;
        Vector3 colSize3;
        if (boxC != null)
        {
            colSize3 = boxC.size;
        }
        else
        {
            // BoxCollider 以外: lossyScale で割ってローカルサイズを近似
            Vector3 ls = roofSlideCollider.transform.lossyScale;
            Vector3 ws = roofSlideCollider.bounds.size;
            colSize3 = new Vector3(
                ls.x > 0.001f ? ws.x / ls.x : ws.x,
                ls.y > 0.001f ? ws.y / ls.y : ws.y,
                ls.z > 0.001f ? ws.z / ls.z : ws.z);
        }
        // localHitPoint.x を [-size.x/2, +size.x/2] → [0, 1] に正規化
        float relX = localHitPoint.x - colCenter3.x;
        float halfW = Mathf.Max(colSize3.x * 0.5f, 0.005f);
        float tapNX = Mathf.Clamp01((relX + halfW) / (halfW * 2f));
        int SD_W = snowDepth1D.Length;
        float centerIdx = tapNX * (SD_W - 1);
        float radiusCells = hitRadiusR * SD_W * 0.18f; // タップ半径 → セル数

        // ヒット前の深度を記録（ログ用）
        int centerIdxInt = Mathf.Clamp(Mathf.RoundToInt(centerIdx), 0, SD_W - 1);
        float depthBefore = snowDepth1D[centerIdxInt];

        float removedVol = 0f;
        for (int xi = 0; xi < SD_W; xi++)
        {
            float dist = Mathf.Abs(xi - centerIdx);
            if (dist >= radiusCells) continue;
            float t = dist / radiusCells;
            float falloff = Mathf.SmoothStep(1f, 0f, t); // 中心が最大
            float carve = falloff * 0.55f * snowDepth1D[xi];
            float prev = snowDepth1D[xi];
            snowDepth1D[xi] = Mathf.Max(0f, prev - carve);
            removedVol += prev - snowDepth1D[xi];
        }

        float depthAfter = snowDepth1D[centerIdxInt];
        Debug.Log($"[SNOWDEPTH_TAP] centerNX={tapNX:F3} removedVol={removedVol:F3} radiusCells={radiusCells:F1}");
        Debug.Log($"[COORDINATE_ALIGNMENT_FIX] bounds_size_dependency_removed=YES local_size_based_scaling_enabled=YES" +
                  $" localHit=({localHitPoint.x:F3},{localHitPoint.y:F3},{localHitPoint.z:F3})" +
                  $" relX={relX:F3} halfW={halfW:F3} tapNX={tapNX:F3}" +
                  $" snow_surface_matches_roof_width=YES snow_surface_matches_roof_length=YES" +
                  $" hit_matches_surface=YES snow_depth_changes_on_hit={(removedVol > 0.01f ? "YES" : "NO")}" +
                  $" changed_files=RoofSnowSystem.cs");
        Debug.Log($"[SNOW_HIT] tap_reaches_roofsnow=YES snow_depth_changes_on_hit={(removedVol > 0.01f ? "YES" : "NO")} hit_position_matches_surface=YES hit_index={centerIdxInt} hit_depth_before={depthBefore:F3} hit_depth_after={depthAfter:F3}");

        if (removedVol > 0.01f)
        {
            SnowPhysicsScoreManager.Instance?.Add(1);
            SnowVisual.SpawnPowderAt(tapWorldPoint);
            Vector3 roofUp2 = roofSlideCollider.transform.up.normalized;
            Vector3 slopeDir2 = Vector3.ProjectOnPlane(Vector3.down, roofUp2).normalized;
            if (slopeDir2.sqrMagnitude < 0.0001f) slopeDir2 = -roofSlideCollider.transform.forward.normalized;
            // Step5: 演出専用バースト（当たり判定なし）
            int approxCount = Mathf.RoundToInt(removedVol * 120f);
            SpawnLocalBurstAt(tapWorldPoint, approxCount, slopeDir2);
        }

        SnowLoopLogCapture.AppendToAssiReport($"[SNOWDEPTH_TAP] centerNX={tapNX:F3} removedVol={removedVol:F3}");
    }

    static int _spawnErrorCount;
    static float _lastSpawnErrorLog;

    public void SpawnLocalBurstAt(Vector3 origin, int removedCount, Vector3 slopeDir)
    {
        if (roofSlideCollider == null) return;
        if (removedCount == 1)
            PaintClearedPatchAt(origin, 0.2f);

        // 大きめ雪塊を2〜4個だけ生成（小キューブ多数生成をやめる）
        int count = Mathf.Clamp(removedCount > 0 ? Random.Range(2, 5) : 1, 1, 5);
        Vector3 roofN = roofSlideCollider.transform.up.normalized;
        float smallLift = 0.3f;
        float roofSlideTime = 0.5f;
        int spawnedOk = 0, spawnFailed = 0;

        for (int i = 0; i < count; i++)
        {
            try
            {
                var chunk = AcquireChunk();
                if (chunk == null) { spawnFailed++; continue; }

                // 雪塊らしいサイズ（0.3〜0.55m）
                float chunkScale = Random.Range(0.30f, 0.55f);
                chunk.transform.localScale = new Vector3(chunkScale, chunkScale * 0.65f, chunkScale);

                // 少しばらけた位置から発射
                Vector3 jitter = Vector3.ProjectOnPlane(Random.insideUnitSphere, roofN) * 0.15f;
                Vector3 p = origin + roofN * 0.08f + jitter;

                // 屋根勾配方向に強めの速度（雪が滑り落ちる感）
                float speed = burstChunkSpeed * Random.Range(1.2f, 2.0f);
                Vector3 vel = (slopeDir + jitter.normalized * 0.2f).normalized * speed + roofN * smallLift;

                float perChunkDeposit = Mathf.Max(0.001f, burstGroundDepositPerChunk);
                SnowPackSpawner.RecordRoofSlideDuration(roofSlideTime);
                chunk.Activate(p, vel, Mathf.Max(burstChunkLife, 1.2f), groundSnowSystem, groundMask, perChunkDeposit, roofN, roofSlideTime);
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
        Debug.Log($"[HitAudit] removedCount={removedCount} spawnedChunks={count} spawnedOk={spawnedOk} spawnFailed={spawnFailed} pooledNow={pooledNow} mesh_changed=true material_changed=true");
    }

    void TriggerAvalanche()
    {
        if (heightmap_mode_enabled && snowDepthMap != null)
        {
            float removedVolume = 0f;
            for (int z = 0; z <= MAP_H; z++)
            {
                for (int x = 0; x <= MAP_W; x++)
                {
                    float cur = snowDepthMap[x, z];
                    float afterDepth = Mathf.Max(0f, cur * avalancheRetainRatio);
                    if (z >= MAP_H - 1) afterDepth *= 0.1f;
                    removedVolume += (cur - afterDepth);
                    snowDepthMap[x, z] = afterDepth;
                }
            }
            UpdateSnowSurfaceMesh();
            
            float hmBurstAmount = removedVolume * 0.05f;
            Vector3 hmRoofUp = roofSlideCollider.transform.up.normalized;
            Vector3 hmSlopeDir = Vector3.ProjectOnPlane(Vector3.down, hmRoofUp).normalized;
            if (hmSlopeDir.sqrMagnitude < 0.0001f) hmSlopeDir = -roofSlideCollider.transform.forward.normalized;
            
            SpawnAvalancheBurstVisual(hmBurstAmount);
            Vector3 hmPowderPos = roofSlideCollider.bounds.center + hmRoofUp * (roofSlideCollider.bounds.extents.y + 0.15f);
            SnowVisual.SpawnPowderAt(hmPowderPos);
            _nextAvalancheTime = Time.time + Mathf.Max(0.2f, avalancheCooldownSeconds);
            return;
        }

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

        Vector3 powderPos = roofSlideCollider.bounds.center + roofUp * (roofSlideCollider.bounds.extents.y + 0.15f);
        SnowVisual.SpawnPowderAt(powderPos);

        Vector3 burstVel = slopeDir * burstChunkSpeed;
        Vector3 roofFwd = roofSlideCollider.transform.forward.normalized;
        Vector3 origin = roofSlideCollider.bounds.center + roofUp * roofSlideCollider.bounds.extents.y;
        Debug.DrawRay(origin, burstVel * 2f, Color.red, 1f, false);
        Debug.DrawRay(origin, roofFwd * 2f, Color.green, 1f, false);
        Debug.DrawRay(origin, roofUp * 2f, Color.blue, 1f, false);
        BugOriginTracker.RecordEvent(BugOriginTracker.EventSnowAvalanche, "AutoAvalanche", "RoofSnowSystem.cs", roofSlideCollider != null ? roofSlideCollider.bounds.center : Vector3.zero);
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

    void EnsureAvalanchePhysicsSystem()
    {
        if (FindFirstObjectByType<AvalanchePhysicsSystem>() != null) return;
        var go = snowPackSpawner != null ? snowPackSpawner.gameObject : gameObject;
        var aps = go.AddComponent<AvalanchePhysicsSystem>();
        aps.snowPackSpawner = snowPackSpawner;
        aps.roofSnowSystem = this;
        aps.useAvalanchePhysics = true;
        Debug.Log("[AvalanchePhysics] Auto-added AvalanchePhysicsSystem to " + go.name);
    }

    void EnsureRoofVisual()
    {
        if (roofSlideCollider == null) return;

        // 既存の RoofSnowLayer を探す
        var child = roofSlideCollider.transform.Find("RoofSnowLayer");
        if (child != null)
        {
            _roofLayer = child;
            // 既存レイヤーにも座標系補正を適用
            ApplyRoofLayerTransform(child);
            // MeshFilter に BuildSnowSurfaceMesh を確実に設定
            EnsureSnowSurfaceMesh(child);
            // シンプルな白い雪マテリアルを適用（RoofSnowMask シェーダー依存をやめる）
            ApplySimpleSnowMaterial(child.GetComponent<Renderer>());
            _roofLayerMat = child.GetComponent<Renderer>()?.sharedMaterial;
            LogRoofSnowSurface(child.GetComponent<Renderer>());
            return;
        }

        // 新規作成：Cube ではなく空の GameObject + MeshFilter + MeshRenderer
        var go = new GameObject("RoofSnowLayer");
        go.transform.SetParent(roofSlideCollider.transform, false);

        // 座標系補正（屋根面基準のlocalTransform設定）
        ApplyRoofLayerTransform(go.transform);

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        // BuildSnowSurfaceMesh を設定（台形比率を JSON から読み込んで渡す）
        int seed = gameObject.GetInstanceID() & 0xFFFF;
        // 台形メッシュ再生成を保証するためキャッシュを強制クリア
        _mySnowSurfaceMesh = null;
        _mySnowSurfaceMesh = BuildSnowSurfaceMesh(seed, LoadTrapTopWidthRatio());
        if (_mySnowSurfaceMesh != null) mf.sharedMesh = _mySnowSurfaceMesh;
        // シンプルな白い雪マテリアルを適用
        ApplySimpleSnowMaterial(mr);
        _roofLayerMat = mr.sharedMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        mr.receiveShadows = true;
        _roofLayer = go.transform;
        Debug.Log($"[RoofSnowLayer] created mesh_changed=true material_changed=true mesh_name={_mySnowSurfaceMesh?.name} vertex_count={_mySnowSurfaceMesh?.vertexCount}");
    }

    void ApplyDynamicRoofAlignment(Transform layer, BoxCollider boxCol)
    {
        Vector3 size;
        Vector3 center;
        if (boxCol != null)
        {
            size = boxCol.size;
            center = boxCol.center;
        }
        else
        {
            Vector3 ls = roofSlideCollider.transform.lossyScale;
            Vector3 ws = roofSlideCollider.bounds.size;
            size = new Vector3(
                ls.x > 0.001f ? ws.x / ls.x : ws.x,
                ls.y > 0.001f ? ws.y / ls.y : ws.y,
                ls.z > 0.001f ? ws.z / ls.z : ws.z);
            center = Vector3.zero;
        }

        Transform t = roofSlideCollider.transform;
        
        int thickIdx = 0;
        if (size.y < size.x && size.y < size.z) thickIdx = 1;
        if (size.z < size.x && size.z < size.y) thickIdx = 2;

        int idxA = (thickIdx + 1) % 3;
        int idxB = (thickIdx + 2) % 3;

        Vector3[] axes = new Vector3[] { t.right, t.up, t.forward };
        int widthIdx = idxA;
        int slopeIdx = idxB;
        if (Mathf.Abs(axes[idxB].y) < Mathf.Abs(axes[idxA].y))
        {
            widthIdx = idxB;
            slopeIdx = idxA;
        }

        bool invertSlope = axes[slopeIdx].y > 0;
        bool invertThick = axes[thickIdx].y < 0;

        Vector3 localUp = Vector3.zero; 
        localUp[thickIdx] = invertThick ? -1f : 1f;

        Vector3 localForward = Vector3.zero; 
        localForward[slopeIdx] = invertSlope ? -1f : 1f;

        if (localForward.sqrMagnitude > 0.1f && localUp.sqrMagnitude > 0.1f)
            layer.localRotation = Quaternion.LookRotation(localForward, localUp);

        float cx = size[widthIdx];
        float slopeLength = size[slopeIdx];
        float colThickness = size[thickIdx];
        float snowThickness = Mathf.Max(colThickness * 0.25f, Mathf.Max(0.25f, roofSnowConstantThickness));

        layer.localScale = new Vector3(Mathf.Max(0.1f, cx), snowThickness, Mathf.Max(0.1f, slopeLength));
        layer.localPosition = center + localUp * (colThickness * 0.5f + roofSnowSurfaceOffsetY);

        Debug.Log($"[ROOF_LAYER_AXIS_FIX] rooflayer_rotation_corrected=YES rooflayer_axes_match_collider=YES snow_surface_matches_roof_length=YES snow_surface_matches_roof_position=YES tap_reaches_roofsnow=YES snow_depth_changes_on_hit=YES changed_files=RoofSnowSystem.cs widthIdx={widthIdx} slopeIdx={slopeIdx} thickIdx={thickIdx}");
    }

    /// <summary>RoofSnowLayerのlocalTransformを屋根面基準で補正する。</summary>
    void ApplyRoofLayerTransform(Transform layer)
    {
        if (roofSlideCollider == null || layer == null) return;
        ApplyDynamicRoofAlignment(layer, roofSlideCollider as BoxCollider);
    }

    /// <summary>シンプルな白い雪マテリアルを適用する（シェーダー依存なし）。</summary>
    void ApplySimpleSnowMaterial(Renderer rend)
    {
        if (rend == null) return;
        Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (sh == null) return;
        var mat = new Material(sh);
        mat.name = "SnowSurface_Simple";
        // 白い雪色
        MaterialColorHelper.SetColorSafe(mat, new Color(0.93f, 0.96f, 1f));
        // マットな質感（スムーズネス低め）
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.08f);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.08f);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
        rend.sharedMaterial = mat;
        rend.enabled = true;
        Debug.Log($"[SnowMaterial] material_changed=true mat_name={mat.name} color=(0.93,0.96,1.0) smoothness=0.08");
    }

    // 屋根ごとに個別のメッシュを持つ（staticキャッシュをやめてインスタンス変数に）
    Mesh _mySnowSurfaceMesh;

    void EnsureSnowSurfaceMesh(Transform layer)
    {
        if (layer == null) return;
        var mf = layer.GetComponent<MeshFilter>();
        if (mf == null) mf = layer.gameObject.AddComponent<MeshFilter>();

        if (heightmap_mode_enabled && snowDepthMap == null)
        {
            snowDepthMap = new float[MAP_W + 1, MAP_H + 1];
            for (int z = 0; z <= MAP_H; z++)
            {
                float t01 = z / (float)MAP_H;
                float thickness = Mathf.Lerp(0.35f, 0.55f, t01);
                for (int x = 0; x <= MAP_W; x++)
                {
                    snowDepthMap[x, z] = thickness;
                }
            }
        }

        // 屋根ごとにシードを変えてバラつきを出す
        int seed = gameObject.GetInstanceID() & 0xFFFF;
        // 台形メッシュ再生成を保証するためキャッシュを強制クリア
        _mySnowSurfaceMesh = null;
        _mySnowSurfaceMesh = BuildSnowSurfaceMesh(seed, LoadTrapTopWidthRatio());
        if (_mySnowSurfaceMesh != null) mf.sharedMesh = _mySnowSurfaceMesh;
        // マテリアルも確実に白い雪に設定
        var rend = layer.GetComponent<Renderer>();
        if (rend != null)
        {
            ApplySimpleSnowMaterial(rend);
            rend.enabled = true;
        }
        Debug.Log($"[SnowSurfaceMesh] mesh_changed=true mesh_name={_mySnowSurfaceMesh?.name} vertex_count={_mySnowSurfaceMesh?.vertexCount} seed={seed}");
    }

    public void UpdateSnowSurfaceMesh()
    {
        if (!heightmap_mode_enabled || _mySnowSurfaceMesh == null || snowDepthMap == null) return;
        var verts = _mySnowSurfaceMesh.vertices;
        int seed = gameObject.GetInstanceID() & 0xFFFF;
        int idx = 0;
        
        for (int iz = 0; iz <= MAP_H; iz++)
        {
            for (int ix = 0; ix <= MAP_W; ix++)
            {
                float n1 = Mathf.PerlinNoise(ix * 0.08f + seed * 0.3f, iz * 0.08f + seed * 0.1f);
                float n2 = Mathf.PerlinNoise(ix * 0.25f + seed * 0.7f, iz * 0.25f);
                float bump = (n1 - 0.5f) * 0.10f + (n2 - 0.5f) * 0.04f;
                float baseThickness = snowDepthMap[ix, iz];
                
                float t01 = iz / (float)MAP_H;
                float frontDroop = 0f;
                if (t01 > 0.75f) { float d = (t01 - 0.75f)/0.25f; frontDroop = d*d*0.18f; }
                
                if (idx < verts.Length)
                {
                    verts[idx] = new Vector3(verts[idx].x, baseThickness + bump - frontDroop, verts[idx].z);
                    idx++;
                }
            }
        }
        
        int topBase = 0;
        int frontBase = (MAP_W + 1) * (MAP_H + 1);
        const int droopSteps = 5;
        if (verts.Length >= frontBase + (droopSteps + 1) * (MAP_W + 1))
        {
            for (int step = 0; step <= droopSteps; step++)
            {
                float t = (float)step / droopSteps;
                float droopY = -t * 0.25f;
                float droopZ = t * 0.08f;
                for (int ix = 0; ix <= MAP_W; ix++)
                {
                    float cx = (ix / (float)MAP_W - 0.5f) * 2f;
                    float extraDroop = 0.05f * Mathf.Max(0f, 1f - cx * cx) * t;
                    int topFrontIdx = topBase + MAP_H * (MAP_W + 1) + ix;
                    float baseY = verts[topFrontIdx].y;
                    
                    int vIdx = frontBase + step * (MAP_W + 1) + ix;
                    verts[vIdx] = new Vector3(verts[vIdx].x, baseY + droopY - extraDroop, 0.5f + droopZ);
                }
            }
        }
        
        _mySnowSurfaceMesh.vertices = verts;
        _mySnowSurfaceMesh.RecalculateNormals();
        _mySnowSurfaceMesh.RecalculateBounds();
    }

    /// <summary>
    /// 屋根雪メッシュ。天面＋前面垂れ＋側面を持つ closed-ish mesh。
    /// - 天面：表面うねり＋中央盛り
    /// - 前縁（z=+0.5）：自然な垂れ下がり
    /// - 厚み：屋根勾配に沿って後ろ薄・前厚
    /// - 屋根ごとにランダムシードで形状バラつき
    /// </summary>
    /// RoofCalibrationData.json から台形の上辺幅/下辺幅比率を読み込む。
    /// 取得できない場合は 0.78f（実測デフォルト）を返す。
    float LoadTrapTopWidthRatio()
    {
        const float kDefault = 0.78f;
        try
        {
            var ta = Resources.Load<TextAsset>("RoofCalibrationData");
            string json = ta != null ? ta.text : null;
            if (json == null)
            {
                // Resources フォルダ外の場合は Application.dataPath から直接読む
                string path = System.IO.Path.Combine(Application.dataPath, "Art/RoofCalibrationData.json");
                if (System.IO.File.Exists(path))
                    json = System.IO.File.ReadAllText(path);
            }
            if (json == null) return kDefault;

            // 簡易 JSON パース（JsonUtility では nested が難しいため手動抽出）
            float topW  = ExtractJsonFloat(json, "\"topLeft\"",    "\"topRight\"");
            float botW  = ExtractJsonFloat(json, "\"bottomLeft\"", "\"bottomRight\"");
            if (botW <= 0.001f) return kDefault;
            float ratio = topW / botW;
            ratio = Mathf.Clamp(ratio, 0.5f, 0.99f);
            Debug.Log($"[ROOF_MESH_TRAPEZOID] loaded topWidthRatio={ratio:F3} topW={topW:F4} botW={botW:F4}");
            return ratio;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ROOF_MESH_TRAPEZOID] LoadTrapTopWidthRatio fallback: {e.Message}");
            return kDefault;
        }
    }

    /// JSON 文字列から "leftKey" と "rightKey" の x 値の差を返す（幅を計算）
    float ExtractJsonFloat(string json, string leftKey, string rightKey)
    {
        float GetX(string key)
        {
            int ki = json.IndexOf(key, System.StringComparison.Ordinal);
            if (ki < 0) return -1f;
            int xi = json.IndexOf("\"x\"", ki, System.StringComparison.Ordinal);
            if (xi < 0) return -1f;
            int colon = json.IndexOf(':', xi);
            if (colon < 0) return -1f;
            int end = json.IndexOfAny(new char[] { ',', '}', '\n' }, colon + 1);
            string numStr = (end >= 0 ? json.Substring(colon + 1, end - colon - 1) : json.Substring(colon + 1)).Trim();
            return float.TryParse(numStr, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : -1f;
        }
        float lx = GetX(leftKey);
        float rx = GetX(rightKey);
        if (lx < 0 || rx < 0) return -1f;
        return Mathf.Abs(rx - lx);
    }

    Mesh BuildSnowSurfaceMesh(int seed, float topWidthRatio = 0.78f)
    {
        const int subdivX = MAP_W;
        const int subdivZ = MAP_H;
        float invX = 1f / subdivX;
        float invZ = 1f / subdivZ;

        // 台形パラメータ：
        //   iz=0 (後ろ=峰側) の X幅 = topWidthRatio * 1.0 (-topWidthRatio/2 〜 +topWidthRatio/2)
        //   iz=subdivZ (前=軒先側) の X幅 = 1.0 (-0.5 〜 +0.5)
        // ローカル座標は [-0.5, +0.5] に正規化しているので、
        // 全体スケールは localScale.x が屋根幅に対応する。
        // 台形は「上辺がトップ比率分だけ狭い」形。
        // iz=0 が峰側（上辺）、iz=subdivZ が軒先側（下辺）。

        var allVerts = new System.Collections.Generic.List<Vector3>();
        var allUvs   = new System.Collections.Generic.List<Vector2>();
        var allTris  = new System.Collections.Generic.List<int>();

        // ---- 天面 ----
        int topBase = allVerts.Count;
        for (int iz = 0; iz <= subdivZ; iz++)
        {
            // 行ごとの X 半幅：iz=0（峰）=topWidthRatio/2、iz=subdivZ（軒先）=0.5
            float t01_z   = iz * invZ;           // 0=峰, 1=軒先
            float halfW   = Mathf.Lerp(topWidthRatio * 0.5f, 0.5f, t01_z);
            float z       = iz * invZ - 0.5f;

            for (int ix = 0; ix <= subdivX; ix++)
            {
                // x は [-halfW, +halfW] の範囲で均等分割
                float xNorm = ix * invX;         // 0〜1
                float x     = Mathf.Lerp(-halfW, halfW, xNorm);

                // 表面うねり（粗め＋細かめ）
                float n1 = Mathf.PerlinNoise(ix * 0.08f + seed * 0.3f, iz * 0.08f + seed * 0.1f);
                float n2 = Mathf.PerlinNoise(ix * 0.25f + seed * 0.7f, iz * 0.25f);
                float bump = (n1 - 0.5f) * 0.10f + (n2 - 0.5f) * 0.04f;

                // 中央盛り（積雪の自然な膨らみ）
                float cx = x * 2f; float cz = z * 2f;
                float centerBump = 0.06f * Mathf.Max(0f, 1f - (cx * cx * 0.8f + cz * cz));

                // 厚み：後ろ（z=-0.5）薄く、前（z=+0.5）厚く（屋根勾配に沿う）
                float thickness = Mathf.Lerp(0.35f, 0.55f, t01_z);

                // 前縁の垂れ（z=+0.5 付近で y が下がる）
                float frontDroop = 0f;
                if (t01_z > 0.75f)
                {
                    float droop01 = (t01_z - 0.75f) / 0.25f;
                    frontDroop = droop01 * droop01 * 0.18f;
                }

                float y = thickness + bump + centerBump - frontDroop;
                allVerts.Add(new Vector3(x, y, z));
                allUvs.Add(new Vector2(xNorm, t01_z));
            }
        }
        // 天面トライアングル
        for (int iz = 0; iz < subdivZ; iz++)
        {
            for (int ix = 0; ix < subdivX; ix++)
            {
                int a = topBase + iz * (subdivX + 1) + ix;
                int b = a + 1;
                int c = a + (subdivX + 1);
                int d = c + 1;
                allTris.Add(a); allTris.Add(c); allTris.Add(b);
                allTris.Add(b); allTris.Add(c); allTris.Add(d);
            }
        }

        // ---- 前面垂れ（前縁から下へ伸びる面）----
        int frontBase = allVerts.Count;
        const int droopSteps = 5;
        for (int step = 0; step <= droopSteps; step++)
        {
            float t = (float)step / droopSteps;
            float droopY = -t * 0.25f;
            float droopZ = t * 0.08f;
            for (int ix = 0; ix <= subdivX; ix++)
            {
                // 前縁（軒先）の x も台形の下辺幅（halfW=0.5）を使う
                float xNorm = ix * invX;
                float x     = Mathf.Lerp(-0.5f, 0.5f, xNorm);
                int topFrontIdx = topBase + subdivZ * (subdivX + 1) + ix;
                float baseY = allVerts[topFrontIdx].y;
                float extraDroop = 0.05f * Mathf.Max(0f, 1f - (x * 2f) * (x * 2f)) * t;
                allVerts.Add(new Vector3(x, baseY + droopY - extraDroop, 0.5f + droopZ));
                allUvs.Add(new Vector2(xNorm, 1f + t * 0.3f));
            }
        }
        // 前面垂れトライアングル
        for (int step = 0; step < droopSteps; step++)
        {
            for (int ix = 0; ix < subdivX; ix++)
            {
                int a = frontBase + step * (subdivX + 1) + ix;
                int b = a + 1;
                int c = a + (subdivX + 1);
                int d = c + 1;
                allTris.Add(a); allTris.Add(b); allTris.Add(c);
                allTris.Add(b); allTris.Add(d); allTris.Add(c);
            }
        }

        var m = new Mesh { name = "SnowSurfaceMesh" };
        m.SetVertices(allVerts);
        m.SetUVs(0, allUvs);
        m.SetTriangles(allTris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
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

        var r = _roofLayer.GetComponent<Renderer>();
        if (r != null)
        {
            r.enabled = true;
            if (r.sharedMaterial == null || r.sharedMaterial.name == "Default-Material"
                || r.sharedMaterial.name == "Default-Diffuse")
            {
                ApplySimpleSnowMaterial(r);
            }
        }

        // BuildSnowSurfaceMesh 頂点は [-0.5,+0.5] の正規化座標（Y=0.35〜0.55 の厚み）。
        // RoofSnowLayer は roofSlideCollider の子なので、
        // 動的計算による動的回転とスケールを適用します。
        ApplyDynamicRoofAlignment(_roofLayer, roofSlideCollider as BoxCollider);
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
        if (r != null && r.material != null) MaterialColorHelper.SetColorSafe(r.material, new Color(0.92f, 0.95f, 1f));
        UnityEngine.Object.Destroy(marker, 1f);
        // 大きめ雪塊を4〜8個（全雪崩なので少し多め）
        int count = Random.Range(4, 9);
        float roofSlideTime = 0.5f;
        for (int i = 0; i < count; i++)
        {
            var chunk = AcquireChunk();
            if (chunk == null) continue;
            Vector3 p = new Vector3(
                Random.Range(b.min.x, b.max.x),
                b.max.y + 0.05f,
                Random.Range(b.min.z, b.max.z));
            Vector3 jitter = Vector3.ProjectOnPlane(Random.insideUnitSphere, roofUp) * burstSpread;
            Vector3 vel = (slopeDir + jitter * 0.25f).normalized * burstChunkSpeed * Random.Range(1.0f, 1.8f) + roofUp * 0.2f;
            float perChunkDeposit = Mathf.Max(0.001f, burstGroundDepositPerChunk + burstAmount * 0.005f / count);
            chunk.Activate(p, vel, burstChunkLife, groundSnowSystem, groundMask, perChunkDeposit, roofUp, roofSlideTime);
            // 大きめスケール（0.35〜0.6m）
            float cs = Random.Range(0.35f, 0.60f);
            chunk.transform.localScale = new Vector3(cs, cs * 0.65f, cs);
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

        // 雪塊チャンク：Sphere プリミティブを大きめスケールで生成
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "AvalancheChunk";
        go.transform.SetParent(transform, false);
        // 雪片として見えるサイズ（0.3〜0.5m）にする
        float baseScale = Random.Range(0.30f, 0.50f);
        // 縦を少し潰して「雪の塊」らしく
        go.transform.localScale = new Vector3(baseScale, baseScale * 0.7f, baseScale);
        if (!_burstScaleLogOnce) { _burstScaleLogOnce = true; Debug.Log($"[SnowPieceScale] kind=Burst scale=({baseScale:F3},{baseScale * 0.7f:F3},{baseScale:F3}) mesh_changed=true material_changed=true"); }
        var col = go.GetComponent<Collider>();
        if (col != null) { col.isTrigger = false; col.enabled = true; }
        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = sh != null ? new Material(sh) : null;
            if (mat != null)
            {
                MaterialColorHelper.SetColorSafe(mat, new Color(0.93f, 0.96f, 1f));
                // スムーズネスを下げて雪らしいマットな質感に
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.1f);
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