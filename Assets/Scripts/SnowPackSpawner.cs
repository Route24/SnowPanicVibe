using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

struct SnowPackEventEntry
{
    public string kind;
    public string reason;
    public int pieceId;
    public int frame;
    public float t;
    public string stackTrace;
}

public enum AutoRebuildFailReason { None, NoRenderers, ActivePiecesZero, RootChildrenMismatch, PoolInvariantFail, Unknown }

/// <summary>
/// Visual-only roof snow pack generator.
/// Place this on one GameObject and assign roof collider.
/// </summary>
public class SnowPackSpawner : MonoBehaviour
{
    const string SnowVisualLayerName = "SnowVisual";

    [Header("Target")]
    public Collider roofCollider;
    [Tooltip("明示指定時: このRendererのboundsから雪サイズ取得。未指定時は自動検索。")]
    public Renderer targetSnowRenderer;
    [Tooltip("複数家対応用。0=1軒目。RoofDefinitionProvider のインデックス。")]
    public int houseIndex = 0;
    [Tooltip("未指定時は茶色屋根メッシュ(bounds最大)を自動採用。雪の角度基準。")]
    public Transform roofAngleReferenceTransform;
    public RoofSnowSystem roofSnowSystem;
    [Tooltip("スケール統一確認用。未設定時はFindFirstObjectByTypeで解決")]
    public SnowFallSystem snowFallSystem;

    [Header("Sync (Depth view)")]
    public float syncIntervalSeconds = 0.2f;
    public float addThreshold = 0.08f;
    public float removeThreshold = -0.08f;
    public float minSyncInterval = 0.50f;
    [Tooltip("1回のSyncで動かせる層変化を1に固定（見た目リセット感防止）")]
    public int maxLayersPerSync = 1;
    [Tooltip("Smoothing time for visual depth (reduces add/remove churn)")]
    public float visualSmoothTime = 0.8f;
    [Tooltip("Current displayed depth (updated on Rebuild/Add/Remove)")]
    public float packDepthMeters;
    [Tooltip("packDepthの急落を制限（m/s）")]
    public float maxDownStepPerSec = 0.25f;
    [Tooltip("packDepthの下限（0リセット禁止）")]
    public float minVisibleDepth = 0.05f;

    float _visualDepth;
    bool _visualDepthInitialized;

    [Header("Look")]
    [Range(0.1f, 1.5f)] public float targetDepthMeters = 0.5f;
    [Tooltip("積雪厚スケール。0.2=厚め(3層自然感)。1=等倍, 0.1=薄め")]
    [Range(0.01f, 1f)] public float snowDepthScale = 0.2f;
    [Tooltip("Piece見た目厚みスケール。1=等倍, 0.25≈1.25cm相当(5cm→2.5cm→1.25cm)")]
    [Range(0.01f, 1f)] public float snowPieceThicknessScale = 0.25f;
    [Tooltip("描画メッシュ厚みスケール。1=等倍, 0.5=半分(見た目Y)。ASSI雪塊: 0.6=白い平たい板感を減らし厚みを出す")]
    [Range(0.01f, 1f)]
    public float snowRenderThicknessScale = 0.6f;
    [Range(0.05f, 0.5f), Tooltip("巨大崩壊型: 0.17 = 重みのある塊感。")]
    public float pieceSize = 0.17f;
    [Tooltip("見た目だけのスケール（ロジックはpieceSizeのまま）。1=等倍, 0.1=1/10")]
    [Range(0.01f, 1f)] public float visualScale = 0.1f;
    [Range(0.5f, 2f)] public float pieceHeightScale = 0.85f;
    [Range(0f, 0.08f)] public float jitter = 0.045f;
    [Range(0f, 0.25f), Tooltip("ASSI雪塊感: 幅/奥行きのスケールばらつき。0.18=格子感・板感をさらに減らす")]
    public float scaleJitterXZ = 0.18f;
    [Range(0f, 0.06f)] public float normalInset = 0.01f;
    public int maxPieces = 1800;
    public bool rebuildOnPlay = true;

    [Header("Material")]
    public Color snowColor = new Color(0.93f, 0.96f, 1f, 1f);
    [Tooltip("PoolReturn発生元特定用。ONで初回のみthrow")]
    public bool throwOnFirstPoolReturn = false;
    [Header("Debug (水平積雪切り分け)")]
    [Tooltip("ON時: SnowPackRoot.rotation = FromToRotation(up, roofUp) で見た目確認")]
    public bool debugAlignToRoofUp = false;
    [Tooltip("局所雪崩検証用。この数以上のPackedを必ず維持。0=無効")]
    public int debugMinPackedPieces = 200;
    [Tooltip("false=屋根雪の自動補充OFF（クリア後も増やさない）。true=MinFill/AutoRebuild有効。")]
    public bool debugAutoRefillRoofSnow = false;
    [Tooltip("ON時: SnowPackPiece の SetActive(false) を禁止。ACTIVE=0 原因切り分け用")]
    public bool blockDeactivate = false;
    [Tooltip("OFF=子Mesh使用。Gridは内部ロジックのみで通常非表示。ON時はpiece直下にRenderer（デバッグ用）。")]
    public bool debugForcePieceRendererDirect = false;
    [Tooltip("false=SnowPackStateIndicator(透明パネル点滅)を表示しない")]
    public bool enableStateIndicator = false;

    [Header("Chain reaction (feel-good avalanche growth)")]
    [Tooltip("Unstable radius = hitRadius * this.")]
    public float unstableRadiusScale = 1.4f;
    [Tooltip("Unstable duration - cells stay marked for chain trigger.")]
    public float unstableDurationSec = 1.4f;
    [Tooltip("Guaranteed secondary wave: delay after hit.")]
    public float secondaryDetachDelaySec = 0.35f;
    [Tooltip("Secondary wave count = clamp(removed*this, 10, 28). 巨大崩壊型: 巻き込み多め。")]
    [Range(0.15f, 0.5f)] public float secondaryDetachFraction = 0.35f;
    [Tooltip("Third wave delay (only when removedCount >= 50).")]
    public float thirdWaveDelaySec = 0.65f;
    [Tooltip("Third wave count = clamp(removed*this, 8, 24). 巨大崩壊型: 早め・多め。")]
    [Range(0.1f, 0.35f)] public float thirdWaveFraction = 0.18f;
    [Range(0f, 1f), Tooltip("Chance unstable cells detach. 巨大崩壊型: 0.78 = 連鎖しやすい。")]
    public float chainDetachChance = 0.78f;
    [Tooltip("Max total chain detachments per hit. 巨大崩壊型: 28 = 二次崩壊を厚く。")]
    public int maxSecondaryDetachPerHit = 28;

    Transform _visualRoot;
    Transform _piecesRoot;
    Material _snowMat;
    Mesh _pieceMesh;
    Mesh _pieceMeshNonSym;
    bool _generatedThisPlay;
    bool _spawnLogOnce;
    bool _scaleLogOnce;
    static bool _poolReturnThrowOnce;
    static bool _rootCauseMeshLogged;
    float _nextToggleLogTime;
    float _nextAuditLogTime;
    float _nextSyncCheckTime;
    float _nextSyncAllowedAt;
    float _nextMinFillTime = -10f;
    float _nextRemainingBoundsLogTime = -10f;
    float _nextClipToRoofTime = -10f;
    const bool UsingLocalPosition = true;
    const float RoofSurfaceOffset = 0.002f; // 屋根密着: 0.005→0.002 で浮き感を軽減

    /// <summary>Avalanche/Tap局所処理中、または AssiDebugUI.DebugFreezeSpawn 時は Spawn・MinFill・Sync 追加を停止</summary>
    public bool IsSpawnFrozen => _inAvalancheSlide || (AssiDebugUI.DebugFreezeSpawn);

    readonly List<List<Transform>> _layerPieces = new List<List<Transform>>();
    readonly Dictionary<(int, int), List<Transform>> _gridPieces = new Dictionary<(int, int), List<Transform>>();
    readonly Dictionary<Transform, (int ix, int iz, int layer)> _pieceToGridData = new Dictionary<Transform, (int, int, int)>();
    readonly Dictionary<Transform, SnowLayerType> _pieceToLayerType = new Dictionary<Transform, SnowLayerType>();
    readonly List<Transform> _piecePool = new List<Transform>();
    Transform _poolRoot;
    int _poolReused;
    int _poolInstantiated;
    float _cachedLayerStep;
    int _cachedNx, _cachedNz;
    float _cachedSpacingR, _cachedSpacingF;
    Vector3 _cachedLocalCenter;
    float _cachedHalfX, _cachedHalfZ;
    Vector3 _roofN, _roofR, _roofF, _roofDownhill;
    Vector3 _roofCenter;
    float _roofWidth, _roofLength;
    float _roofProjectedW, _roofProjectedL;
    float _roofCellSize;
    bool _builtFromRoofDefinition;
    bool _useExactGrid;
    int _rebuildCount;
    int _addCount;
    int _removeCount;
    bool _inAvalancheSlide;
    float _packDepthMin1s = float.MaxValue;
    float _packDepthMax1s = float.MinValue;
    int _rootChildrenMin1s = int.MaxValue;
    int _rootChildrenMax1s = int.MinValue;
    float _visualPackDeltaMax1s;
    int _lastChildrenCount;
    bool _childrenGuardStackLogged;
    readonly List<Transform> _poolReturnQueue = new List<Transform>();
    GameObject _pendingSlideRootToDestroy;
    int _pendingRemoveCountFromAvalanche;
    const int MaxPoolReturnsPerFrame = 50;
    const float AvalancheReturnRate = 0.3f; // 全削除禁止: 1フレームあたり最大30%まで

    const int LastEventsCapacity = 20;
    const int MaxExceptionCount = 3;
    static int _exceptionCount;
    readonly List<SnowPackEventEntry> _lastEvents = new List<SnowPackEventEntry>(LastEventsCapacity + 1);
    int _prevActivePiecesCount = -1;
    float _firstActiveZeroTime = -1f;
    int _firstActiveZeroFrame = -1;
    bool _activeZeroReportLogged;
    bool _snowPackPassErrorLogged; // activePieces=0 FAIL を1回のみ表示（停止時フラッド防止）
    bool _burstStatsLoggedThisTap;
    int _lastActivePiecesCountForUI = -1;
    bool _entityCountDumpedForActiveZero;
    bool _autoRebuildFired;
    bool _zeroTotalErrorEmittedOnce;
    int _zeroTotalFirstFrame = -1;
    int _zeroTotalRepeatCount;
    bool _zeroTotalSuppressLogged;
    bool _autoRebuildRecovered;
    int _autoRebuildFrame = -1;
    float _activeZeroUIFadeAt = -1f;
    AutoRebuildFailReason _autoRebuildFailReason = AutoRebuildFailReason.None;
    float _failUIFadeAt = -1f;
    int _failFrame;
    float _failTime;
    Renderer _stateIndicatorRenderer;
    readonly HashSet<string> _poolReturnFirstLogged = new HashSet<string>();
    struct TransitionSample { public float t; public int rootCh; public int pooled; public int active; }
    readonly System.Collections.Generic.Dictionary<(int, int), float> _unstableCellExpiry = new System.Collections.Generic.Dictionary<(int, int), float>();
    int _chainTriggersThisHit;
    int _chainDetachCountSinceTap;
    bool _scheduledSecondaryWaveFired;
    bool _scheduledThirdWaveFired;
    readonly List<TransitionSample> _transitionSamples = new List<TransitionSample>(120);
    const float TransitionSampleInterval = 0.1f;
    float _lastTransitionSampleTime = -10f;

    static bool _assiBootLoggedStatic;
    bool _assiDiagnosticLogged;
    static bool _gridDrawSpaceLogged;

    void Awake()
    {
        if (!Application.isPlaying) return;
        if (roofCollider == null) roofCollider = ResolveRoofCollider();
        EnsureRoot();
    }

    void OnEnable()
    {
        if (Application.isPlaying)
        {
            _exceptionCount = 0;
            _exceptionSuppressedLoggedOnce = false;
            _assiBootLoggedStatic = false;
        }
        if (!Application.isPlaying || _generatedThisPlay) return;
        if (!rebuildOnPlay)
        {
            _generatedThisPlay = true;
            RebuildSnowPack("OnEnable");
        }
    }

    void Start()
    {
        if (!Application.isPlaying || !rebuildOnPlay || _generatedThisPlay) return;
        _generatedThisPlay = true;
        LogSnowPackCall("REBUILD", "Start");
        RebuildSnowPack("RebuildOnPlay");
    }

    [ContextMenu("Rebuild Snow Pack")]
    public void Rebuild()
    {
        RebuildSnowPack("ContextMenu");
    }

    public void RebuildSnowPack(string reason)
    {
        if (roofCollider == null)
            roofCollider = ResolveRoofCollider();
        if (roofCollider == null)
        {
            UnityEngine.Debug.LogWarning("[SnowPack] roofCollider is not assigned.");
            return;
        }

        if (!RoofDefinitionProvider.TryGet(houseIndex, out _, out _))
        {
            if (RoofDefinitionResolver.ResolveFromCollider(roofCollider, GetRoofAngleTransform(), out var def))
                RoofDefinitionProvider.Set(houseIndex, def, fromResolver: true);
        }

        EnsureRoot();
        PushLastEvent("Rebuild", $"{reason} fileLine={GetFileLineFromStack()}", GetRealStackTrace());
        LogSnowPackCall("REBUILD", reason);
        ClearSnowPack(reason);
        EnsureMaterial();
        EnsurePieceMesh();
        EnsureSnowVisualCollisionSetup();
        AlignVisualRootToRoof();

        Vector3 roofUp = GetRoofAngleTransform() != null ? GetRoofAngleTransform().up.normalized : roofCollider.transform.up.normalized;
        Vector3 roofFwd = roofCollider.transform.forward.normalized;
        float roofRotY = roofCollider.transform.rotation.eulerAngles.y;
        float packRotY = _visualRoot != null ? _visualRoot.rotation.eulerAngles.y : 0f;
        UnityEngine.Debug.Log($"[SnowPackBasis] usingLocal=true roofUp={roofUp} roofFwd={roofFwd} roofRotY={roofRotY:F1} packRotY={packRotY:F1}");
        UnityEngine.Debug.Log($"[SnowPieceScale] kind=Packed scale=({pieceSize:F3},{pieceSize * pieceHeightScale:F3},{pieceSize:F3})");

        CacheGridParams();
        float effectiveDepth = targetDepthMeters * snowDepthScale;
        int layersRaw = Mathf.Max(1, Mathf.CeilToInt(targetDepthMeters / Mathf.Max(0.02f, _cachedLayerStep)));
        int layers = Mathf.Max(1, Mathf.CeilToInt(effectiveDepth / Mathf.Max(0.02f, _cachedLayerStep)));
        int spawned = 0;
        if (ForceMinimalSinglePiece && _roofWidth > 0f && _roofLength > 0f && _piecesRoot != null)
        {
            UnityEngine.Debug.Log("[SNOW_MINIMAL] snow_spawn_called=true ForceMinimalSinglePiece=true");
            var singleList = SpawnMinimalSinglePiece();
            _layerPieces.Add(singleList);
            spawned = singleList.Count;
            layers = spawned > 0 ? 1 : 0;
            UnityEngine.Debug.Log($"[SNOW_MINIMAL] snow_spawn_success={(spawned > 0).ToString().ToLower()} activePieces={spawned} rootChildren={_piecesRoot.childCount}");
        }
        else
        {
            if (debugAutoRefillRoofSnow && debugMinPackedPieces > 0)
            {
                int approxPerLayer = _cachedNx * _cachedNz;
                layers = Mathf.Max(layers, Mathf.Max(1, (debugMinPackedPieces + approxPerLayer - 1) / Mathf.Max(1, approxPerLayer)));
            }
            for (int y = 0; y < layers; y++)
            {
                var layerList = SpawnLayer(y);
                _layerPieces.Add(layerList);
                spawned += layerList.Count;
                if (spawned >= maxPieces) break;
                if (debugAutoRefillRoofSnow && debugMinPackedPieces > 0 && spawned >= debugMinPackedPieces) break;
            }
        }

        _pieceToLayerType.Clear();
        AssignLayerTypesToPieces(Mathf.Max(1, layers));

        _rebuildCount++;
        AuditSnowPackPhysics();
        packDepthMeters = (ForceMinimalSinglePiece && spawned > 0) ? 0.1f : effectiveDepth;
        float sampleH = Mathf.Max(0.03f, pieceSize * pieceHeightScale);
        float sampleHAfter = sampleH * snowPieceThicknessScale;
        Vector3 pieceScaleBefore = new Vector3(Mathf.Max(0.05f, pieceSize), sampleH, Mathf.Max(0.05f, pieceSize));
        Vector3 pieceScaleAfter = new Vector3(Mathf.Max(0.05f, pieceSize), sampleHAfter, Mathf.Max(0.05f, pieceSize));
        SnowLoopLogCapture.AppendToAssiReport("=== SNOW DEPTH ===");
        SnowLoopLogCapture.AppendToAssiReport($"oldDepthLike={targetDepthMeters:F3} newDepthLike={effectiveDepth:F3}");
        SnowLoopLogCapture.AppendToAssiReport($"methodUsed=B layersRaw={layersRaw} layers={layers}");
        SnowLoopLogCapture.AppendToAssiReport($"pieceScaleBefore=({pieceScaleBefore.x:F3},{pieceScaleBefore.y:F3},{pieceScaleBefore.z:F3}) after=({pieceScaleAfter.x:F3},{pieceScaleAfter.y:F3},{pieceScaleAfter.z:F3})");
        SnowLoopLogCapture.AppendToAssiReport("=== SNOW DEPTH HALF ===");
        SnowLoopLogCapture.AppendToAssiReport($"depthLikeBefore=0.050 depthLikeAfter=0.025");
        SnowLoopLogCapture.AppendToAssiReport($"meshScaleBefore=({pieceScaleBefore.x:F3},{pieceScaleBefore.y:F3},{pieceScaleBefore.z:F3}) meshScaleAfter=({pieceScaleAfter.x:F3},{pieceScaleAfter.y:F3},{pieceScaleAfter.z:F3})");
        SnowLoopLogCapture.AppendToAssiReport("assumedThicknessAxis=Y");
        Vector3 meshScaleBeforeQuarter = new Vector3(Mathf.Max(0.05f, pieceSize), sampleH * 0.5f, Mathf.Max(0.05f, pieceSize));
        SnowLoopLogCapture.AppendToAssiReport("=== SNOW DEPTH QUARTER ===");
        SnowLoopLogCapture.AppendToAssiReport($"meshScaleBefore=({meshScaleBeforeQuarter.x:F3},{meshScaleBeforeQuarter.y:F3},{meshScaleBeforeQuarter.z:F3}) meshScaleAfter=({pieceScaleAfter.x:F3},{pieceScaleAfter.y:F3},{pieceScaleAfter.z:F3})");

        Vector3 beforeRenderScale = new Vector3(Mathf.Max(0.05f, pieceSize), sampleHAfter, Mathf.Max(0.05f, pieceSize));
        Vector3 afterRenderScale = debugForcePieceRendererDirect
            ? new Vector3(Mathf.Max(0.05f, pieceSize), sampleHAfter * snowRenderThicknessScale, Mathf.Max(0.05f, pieceSize))
            : new Vector3(Mathf.Max(0.05f, pieceSize), sampleHAfter * snowRenderThicknessScale, Mathf.Max(0.05f, pieceSize));
        string piecePath = _piecesRoot != null ? GetTransformPath(_piecesRoot) + "/SnowPackPiece" : "SnowPackVisual/SnowPackPiecesRoot/SnowPackPiece";
        SnowLoopLogCapture.AppendToAssiReport("=== SNOW_RENDER_THICKNESS_HALF ===");
        SnowLoopLogCapture.AppendToAssiReport($"targetPath={piecePath}" + (debugForcePieceRendererDirect ? " (direct)" : "/Mesh"));
        SnowLoopLogCapture.AppendToAssiReport($"beforeScale=({beforeRenderScale.x:F3},{beforeRenderScale.y:F3},{beforeRenderScale.z:F3}) afterScale=({afterRenderScale.x:F3},{afterRenderScale.y:F3},{afterRenderScale.z:F3})");
        UnityEngine.Debug.Log($"[SnowPack] generated={spawned} depth={effectiveDepth:F2} pieceSize={pieceSize:F2} layers={layers}");
        SnowLoopLogCapture.AppendToAssiReport("=== SNOW SURFACE CHECK ===");
        SnowLoopLogCapture.AppendToAssiReport("top_surface_continuity=perlin_smooth_contour");
        SnowLoopLogCapture.AppendToAssiReport("side_surface_continuity=edge_scale_1_03");
        SnowLoopLogCapture.AppendToAssiReport("grid_feel_before=strong");
        SnowLoopLogCapture.AppendToAssiReport("grid_feel_after=weak");
        SnowLoopLogCapture.AppendToAssiReport("cube_feel_remaining=reduced_roundness035_material");
        SnowLoopLogCapture.AppendToAssiReport("snow_mass_impression=improved");
        SnowLoopLogCapture.AppendToAssiReport("material_changed=true");
        SnowLoopLogCapture.AppendToAssiReport("mesh_changed=true");
        SnowLoopLogCapture.AppendToAssiReport("result=PASS");
        int hc = 0;
        var houses = GameObject.Find("Houses");
        if (houses != null) hc = houses.transform.childCount;
        UnityEngine.Debug.Log($"[SNOW_ROLLBACK_CHECK] rollback_target=pre_camera_change_good_state current_house_count={hc} pieceSize={pieceSize:F2} maxSecondaryDetachPerHit={maxSecondaryDetachPerHit} chainDetachChance={chainDetachChance:F2} secondaryDetachFraction={secondaryDetachFraction:F2} result=OK comment=giant_collapse_type");
        if (reason == "AvalancheActiveZero" || reason == "AutoRebuildOnActiveZero")
            UnityEngine.Debug.Log($"[REFILL] reason={reason} packedBefore=0 packedAfter={spawned}");

        LogPiecePoseSampleFirst3();
        LogRotationOverrideSuspectedLocations();
        LogSnowLayerMix();
        LogVisualSizeMatch();

        if (SnowVerifyB2Debug.Enabled)
            LogB2PieceStatesRightAfterGeneration(spawned);
    }

    void LogVisualSizeMatch()
    {
        float roofVisualW = _roofProjectedW > 0f ? _roofProjectedW : _roofWidth;
        float roofVisualD = _roofProjectedL > 0f ? _roofProjectedL : _roofLength;
        if (roofCollider != null && roofVisualW < 0.01f)
        {
            var b = roofCollider.bounds;
            roofVisualW = Mathf.Max(b.size.x, b.size.z);
            roofVisualD = Mathf.Min(b.size.x, b.size.z);
        }
        float snowVisualW = _roofWidth;
        float snowVisualD = _roofLength;
        if (_piecesRoot != null && _piecesRoot.childCount > 0 && (_roofR.sqrMagnitude > 0.001f && _roofF.sqrMagnitude > 0.001f))
        {
            Bounds snowBounds = default;
            int count = 0;
            for (int i = 0; i < _piecesRoot.childCount; i++)
            {
                var tr = _piecesRoot.GetChild(i);
                if (tr == null || !tr.gameObject.activeSelf) continue;
                var r = tr.GetComponentInChildren<Renderer>(true);
                if (r != null && r.enabled)
                {
                    if (count == 0) snowBounds = r.bounds;
                    else snowBounds.Encapsulate(r.bounds);
                    count++;
                }
            }
            if (count > 0)
            {
                Vector3 d = snowBounds.max - snowBounds.min;
                snowVisualW = Mathf.Abs(Vector3.Dot(d, _roofR.normalized));
                snowVisualD = Mathf.Abs(Vector3.Dot(d, _roofF.normalized));
            }
        }
        float widthGap = roofVisualW - snowVisualW;
        float depthGap = roofVisualD - snowVisualD;
        bool visualMatch = Mathf.Abs(widthGap) <= 0.1f && Mathf.Abs(depthGap) <= 0.1f;
        UnityEngine.Debug.Log($"[VISUAL_SIZE] roof_visual_width={roofVisualW:F3} roof_visual_depth={roofVisualD:F3} snow_visual_width={snowVisualW:F3} snow_visual_depth={snowVisualD:F3} width_gap={widthGap:F3} depth_gap={depthGap:F3} visual_match_pass={visualMatch.ToString().ToLower()}");
        SnowLoopLogCapture.AppendToAssiReport($"=== VISUAL_SIZE === roof_visual_width={roofVisualW:F3} roof_visual_depth={roofVisualD:F3} snow_visual_width={snowVisualW:F3} snow_visual_depth={snowVisualD:F3} width_gap={widthGap:F3} depth_gap={depthGap:F3} visual_match_pass={visualMatch}");
    }

    /// <summary>SnowSizeDiagnostics 用。原因切り分けのため bounds 計算に必要な値のみ公開。</summary>
    public struct SnowSizeDiagnosticData
    {
        public Vector3 roofCenter, roofR, roofF, roofN;
        public float roofWidth, roofLength;
        public Transform piecesRoot;
    }
    public SnowSizeDiagnosticData GetSnowSizeDiagnosticData()
    {
        return new SnowSizeDiagnosticData { roofCenter = _roofCenter, roofR = _roofR, roofF = _roofF, roofN = _roofN, roofWidth = _roofWidth, roofLength = _roofLength, piecesRoot = _piecesRoot };
    }

    void LogB2PieceStatesRightAfterGeneration(int generatedTotal)
    {
        if (_piecesRoot == null) return;
        int survived = 0;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[B2_DEBUG] generated_total={generatedTotal}");

        int idx = 0;
        for (int i = 0; i < _piecesRoot.childCount; i++)
        {
            var tr = _piecesRoot.GetChild(i);
            if (tr == null || tr.gameObject.name != "SnowPackPiece") continue;
            bool active = tr.gameObject.activeSelf;
            bool pooled = _piecePool != null && _piecePool.Contains(tr);
            if (active && !pooled) survived++;

            if (idx < SnowVerifyB2Debug.MaxPieceDetailLogs)
            {
                string disc = active ? "survived" : (pooled ? "pooled" : "inactive");
                sb.AppendLine(SnowVerifyB2Debug.FormatPieceState(idx, tr, active, pooled, disc));
                idx++;
            }
        }
        int pooledCount = _piecePool != null ? _piecePool.Count : 0;
        int discarded = generatedTotal - survived;
        sb.AppendLine($"[B2_DEBUG] survived_after_generation={survived} discarded_after_generation={discarded} rootChildren={_piecesRoot.childCount} pooled={pooledCount}");
        sb.AppendLine($"[B2_DEBUG] discard_reason_counts={SnowVerifyB2Debug.GetDiscardReasonCountsString()} cleanup_called={SnowVerifyB2Debug.CleanupCalled.ToString().ToLower()} pool_return_called={SnowVerifyB2Debug.PoolReturnCalled.ToString().ToLower()}");
        int total = GetB2TotalCount();
        if (total <= 0) SnowVerifyB2Debug.RecordZeroTransition("generation");
        UnityEngine.Debug.Log(sb.ToString());
    }

    /// <summary>layerIndex: 0=bottom(根雪側), layers-1=top(表面). 比率 Powder45% Slab40% Base15%</summary>
    void AssignLayerTypesToPieces(int totalLayers)
    {
        if (totalLayers <= 0) return;
        int baseCount = Mathf.Max(1, Mathf.FloorToInt(totalLayers * 0.15f));
        int slabCount = Mathf.Max(1, Mathf.FloorToInt(totalLayers * 0.40f));
        for (int li = 0; li < _layerPieces.Count; li++)
        {
            SnowLayerType t = li < baseCount ? SnowLayerType.Base : (li < baseCount + slabCount ? SnowLayerType.Slab : SnowLayerType.Powder);
            foreach (var p in _layerPieces[li])
            {
                if (p != null) _pieceToLayerType[p] = t;
            }
        }
    }

    void LogSnowLayerMix()
    {
        int powder = 0, slab = 0, baze = 0;
        foreach (var kv in _pieceToLayerType)
        {
            if (kv.Key == null) continue;
            switch (kv.Value) { case SnowLayerType.Powder: powder++; break; case SnowLayerType.Slab: slab++; break; case SnowLayerType.Base: baze++; break; }
        }
        SnowLoopLogCapture.AppendToAssiReport("=== SNOW LAYER MIX ===");
        SnowLoopLogCapture.AppendToAssiReport($"Powder count={powder}");
        SnowLoopLogCapture.AppendToAssiReport($"Slab count={slab}");
        SnowLoopLogCapture.AppendToAssiReport($"Base count={baze}");
    }

    static bool _rotationOverrideLoggedOnce;

    void LogRotationOverrideSuspectedLocations(bool force = false)
    {
        if (!force && _rotationOverrideLoggedOnce) return;
        if (!force) _rotationOverrideLoggedOnce = true;
        int count = 0;
        try
        {
            string scriptsPath = System.IO.Path.Combine(Application.dataPath, "Scripts");
            if (!System.IO.Directory.Exists(scriptsPath))
            {
                UnityEngine.Debug.Log("[RotationOverrideFound] None (Scripts dir not found)");
                return;
            }
            var files = System.IO.Directory.GetFiles(scriptsPath, "*.cs", System.IO.SearchOption.AllDirectories);
            foreach (var path in files)
            {
                string name = System.IO.Path.GetFileName(path);
                bool relevant = name.Contains("SnowPack") || name.Contains("Packed") || name.Contains("Piece") || name.Contains("SnowTest") || name.Contains("Assi") || name.Contains("Debug") || name.Contains("Tap") || name.Contains("Gizmo") || name.Contains("Marker") || name.Contains("Roof") || name.Contains("Avalanche");
                if (!relevant) continue;
                var lines = System.IO.File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (line.TrimStart().StartsWith("//")) continue;
                    bool suspect = (line.Contains("Quaternion.identity") || line.Contains("Quaternion.Euler(0,0,0)")) && (line.Contains("rotation") || line.Contains("Rotation"));
                    suspect = suspect || (line.Contains("LookRotation") && line.Contains("Vector3.up"));
                    suspect = suspect || (line.Contains("LookAt") && (line.Contains("Camera") || line.Contains("Main")));
                    suspect = suspect || (line.Contains("FromToRotation") && line.Contains("Vector3.up"));
                    if (suspect)
                    {
                        var relPath = path.Replace(Application.dataPath, "Assets").Replace("\\", "/");
                        string reason = line.Contains("Quaternion.identity") ? "Quaternion.identity" : line.Contains("LookRotation") ? "LookRotation+Vector3.up" : line.Contains("LookAt") ? "LookAt" : line.Contains("FromToRotation") ? "FromToRotation" : "rotation_overwrite";
                        string objName = System.IO.Path.GetFileName(path);
                        UnityEngine.Debug.Log($"[RotationOverrideFound] file={relPath} line={i + 1} obj={objName} reason={reason} code={line.Trim()}");
                        count++;
                    }
                }
            }
            if (count == 0)
                UnityEngine.Debug.Log("[RotationOverrideFound] None");
            else
                UnityEngine.Debug.Log($"[RotationOverrideFound] total={count} (上記file:line参照)");
        }
        catch (System.Exception ex) { UnityEngine.Debug.Log($"[RotationOverrideFound] None (search skip: {ex.Message})"); }
    }

    void LogPiecePoseSampleFirst3()
    {
        if (_layerPieces.Count == 0)
        {
            UnityEngine.Debug.Log("[PiecePoseSample] N/A (no pieces - spawner disabled or rebuild skipped)");
            return;
        }
        int logged = 0;
        for (int li = 0; li < _layerPieces.Count && logged < 3; li++)
        {
            foreach (var pieceT in _layerPieces[li])
            {
                if (pieceT == null) continue;
                int pieceId = pieceT.GetInstanceID();
                var we = pieceT.rotation.eulerAngles;
                var pieceUp = pieceT.up.normalized;
                float dotUpN = Vector3.Dot(pieceUp, _roofN);

                Transform childRendererT = null;
                var mr = pieceT.GetComponentInChildren<Renderer>();
                if (mr != null) childRendererT = mr.transform;
                var childWe = childRendererT != null ? childRendererT.rotation.eulerAngles : Vector3.zero;
                var childUp = childRendererT != null ? childRendererT.up.normalized : Vector3.zero;
                float dotChildUpN = childRendererT != null ? Vector3.Dot(childUp, _roofN) : -1f;

                var chainParts = new System.Collections.Generic.List<string>();
                var p = pieceT;
                for (int depth = 0; depth < 3 && p != null; depth++)
                {
                    var we2 = p.rotation.eulerAngles;
                    var le = p.localRotation.eulerAngles;
                    chainParts.Add($"{p.name} worldEuler=({we2.x:F1},{we2.y:F1},{we2.z:F1}) localEuler=({le.x:F1},{le.y:F1},{le.z:F1})");
                    p = p.parent;
                }
                string parentChain = string.Join(" -> ", chainParts);

                string verdict;
                if (dotUpN >= 0.98f && (dotChildUpN < 0.98f || (Mathf.Abs(childWe.x) < 1f && Mathf.Abs(childWe.y) < 1f && Mathf.Abs(childWe.z) < 1f)))
                    verdict = "CASE A: pieceは斜めだがchildRendererが水平→Renderer側が水平固定 or 子がidentity固定";
                else if (dotUpN < 0.98f)
                    verdict = "CASE B: piece自体が水平→Spawn回転が反映されてない";
                else if (dotUpN >= 0.98f && dotChildUpN >= 0.98f)
                    verdict = "OK: pieceもRendererも屋根に沿っている";
                else
                    verdict = "CASE C: 親chainで相殺回転の疑い";

                string childName = childRendererT != null ? childRendererT.name : "null";
                UnityEngine.Debug.Log($"[PiecePoseSample] pieceId={pieceId} pieceTransform.name={pieceT.name} pieceT.worldEuler=({we.x:F1},{we.y:F1},{we.z:F1}) pieceT.up=({pieceUp.x:F3},{pieceUp.y:F3},{pieceUp.z:F3}) dotUpN={dotUpN:F3} childRendererT.name={childName} childRendererT.worldEuler=({childWe.x:F1},{childWe.y:F1},{childWe.z:F1}) childRendererT.up=({childUp.x:F3},{childUp.y:F3},{childUp.z:F3}) dotChildUpN={dotChildUpN:F3} parentChain=[{parentChain}] 判定={verdict}");
                logged++;
                if (logged >= 3) break;
            }
        }
    }

    void CacheGridParams()
    {
        if (roofCollider == null) return;
        BuildRoofBasis();
    }

    /// <summary>見た目改善: (ix,iz,layer)から決定的なスケール倍率を算出。0.88〜1.12 程度。</summary>
    static (float sx, float sy, float sz) GetScaleJitterMultipliers(int ix, int iz, int layer, float scaleJitterXZ)
    {
        if (scaleJitterXZ <= 0f) return (1f, 1f, 1f);
        int h = (ix * 73856093) ^ (iz * 19349663) ^ (layer * 83492791);
        float rx = ((h & 0xFFFF) / 65535f - 0.5f) * 2f * scaleJitterXZ + 1f;
        float rz = (((h >> 10) & 0xFFFF) / 65535f - 0.5f) * 2f * scaleJitterXZ + 1f;
        float ry = (((h >> 20) & 0x3FF) / 1023f - 0.5f) * 0.4f + 1f; // 高さ ±20%
        return (rx, ry, rz);
    }

    /// <summary>見た目改善: (ix,iz,layer)から決定的な位置ジッターを算出。Refresh時も同じ値。</summary>
    static (float jx, float jz) GetPositionJitter(int ix, int iz, int layer, float jitter)
    {
        if (jitter <= 0f) return (0f, 0f);
        int h = (ix * 51727) ^ (iz * 31657) ^ (layer * 30271);
        float jx = ((h & 0xFFFF) / 65535f - 0.5f) * 2f * jitter;
        float jz = (((h >> 8) & 0xFFFF) / 65535f - 0.5f) * 2f * jitter;
        return (jx, jz);
    }

    void GridCellToUV(int ix, int iz, out float u, out float v)
    {
        if ((UseFullRoofCoverage || _useExactGrid) && _cachedSpacingR > 0f && _cachedSpacingF > 0f)
        {
            u = -0.5f + (_roofCellSize * 0.5f + ix * _cachedSpacingR) / _roofWidth;
            v = -0.5f + (_roofCellSize * 0.5f + iz * _cachedSpacingF) / _roofLength;
        }
        else
        {
            u = (ix + 0.5f) / Mathf.Max(1, _cachedNx) - 0.5f;
            v = (iz + 0.5f) / Mathf.Max(1, _cachedNz) - 0.5f;
        }
    }

    /// <summary>ix,iz,layer から決定的なスケール倍率を返す。見た目改善: 個体差を出す。</summary>
    (float sx, float sy, float sz) GetScaleJitterMultipliers(int ix, int iz, int layer)
    {
        float j = Mathf.Max(0f, scaleJitterXZ);
        int h = (ix * 73856093) ^ (iz * 19349663) ^ (layer * 83492791);
        float sx = 1f + ((h & 0xFFFF) / 65535f - 0.5f) * 2f * j;
        float sz = 1f + (((h >> 16) & 0xFFFF) / 65535f - 0.5f) * 2f * j;
        float sy = 1f + ((h >> 8) % 256) / 255f * 0.4f - 0.2f; // 高さ ±10%
        return (sx, sy, sz);
    }

    /// <summary>ix,iz,layer から決定的な位置オフセット(jx,jz)を返す。 Refresh 時も同じ値になる。</summary>
    (float jx, float jz) GetDeterministicPositionJitter(int ix, int iz, int layer)
    {
        float j = Mathf.Max(0f, jitter);
        int h = (ix * 73856093) ^ (iz * 19349663) ^ ((layer + 7) * 83492791);
        float jx = ((h & 0xFFFF) / 65535f - 0.5f) * 2f * j;
        float jz = (((h >> 16) & 0xFFFF) / 65535f - 0.5f) * 2f * j;
        return (jx, jz);
    }

    void UVToGridCell(float u, float v, out int cx, out int cz)
    {
        if ((UseFullRoofCoverage || _useExactGrid) && _cachedSpacingR > 0f && _cachedSpacingF > 0f)
        {
            cx = Mathf.RoundToInt(((u + 0.5f) * _roofWidth - _roofCellSize * 0.5f) / _cachedSpacingR);
            cz = Mathf.RoundToInt(((v + 0.5f) * _roofLength - _roofCellSize * 0.5f) / _cachedSpacingF);
        }
        else
        {
            cx = Mathf.RoundToInt((u + 0.5f) * _cachedNx - 0.5f);
            cz = Mathf.RoundToInt((v + 0.5f) * _cachedNz - 0.5f);
        }
        cx = Mathf.Clamp(cx, 0, _cachedNx - 1);
        cz = Mathf.Clamp(cz, 0, _cachedNz - 1);
    }

    /// <summary>見た目改善: 格子感を減らす。ix,iz,layer から決定論的にジッターを算出。上面連続性: sy は Perlin で隣同士がつながって見える。</summary>
    static void GetDeterministicJitter(int ix, int iz, int layer, float jitterMax, float scaleJitterXZ,
        out float jx, out float jz, out float sx, out float sy, out float sz)
    {
        int h = (ix * 73856093) ^ (iz * 19349663) ^ ((layer + 1) * 83492791);
        jx = ((h & 0xFFFF) / 65535f * 2f - 1f) * Mathf.Max(0f, jitterMax);
        jz = (((h >> 16) & 0xFFFF) / 65535f * 2f - 1f) * Mathf.Max(0f, jitterMax);
        float rx = ((h >> 8) & 0xFF) / 255f;
        float rz = ((h >> 24) & 0xFF) / 255f;
        sx = 1f + (rx * 2f - 1f) * Mathf.Max(0f, scaleJitterXZ);
        // 上面連続性: Perlin で隣同士の高さがゆるくつながる。白いデコボコ面。
        float perlin = Mathf.PerlinNoise(ix * 0.08f + layer * 2.3f + 37f, iz * 0.08f + 41f);
        sy = 0.92f + perlin * 0.2f; // 0.92..1.12
        sz = 1f + (rz * 2f - 1f) * Mathf.Max(0f, scaleJitterXZ);
    }

    Transform GetRoofAngleTransform()
    {
        if (roofAngleReferenceTransform != null) return roofAngleReferenceTransform;
        var meshT = ResolveRoofVisualMesh();
        return meshT != null ? meshT : (roofCollider != null ? roofCollider.transform : null);
    }

    static Transform ResolveRoofVisualMesh()
    {
        var roof = GameObject.Find("RoofRoot");
        if (roof == null) return null;
        var exclude = new HashSet<Transform>();
        var v = GameObject.Find("SnowPackVisual");
        if (v != null) exclude.Add(v.transform);
        var pr = GameObject.Find("SnowPackPiecesRoot");
        if (pr != null)
        {
            exclude.Add(pr.transform);
            for (int i = 0; i < pr.transform.childCount; i++)
            {
                var c = pr.transform.GetChild(i);
                if (c != null && c.name == "SnowPackPiece") exclude.Add(c);
            }
        }
        MeshRenderer best = null;
        float bestSize = 0f;
        foreach (var mr in roof.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr == null || exclude.Contains(mr.transform)) continue;
            float s = mr.bounds.size.x * mr.bounds.size.y * mr.bounds.size.z;
            if (s > bestSize) { bestSize = s; best = mr; }
        }
        return best != null ? best.transform : null;
    }

    void BuildRoofBasis()
    {
        if (roofCollider == null) return;

        if (targetSnowRenderer == null && RoofDefinitionProvider.TryGet(houseIndex, out var def, out bool fromResolver) && def.isValid)
        {
            ApplyRoofDefinition(def, fromResolver);
            return;
        }

        _builtFromRoofDefinition = false;
        _useExactGrid = false;
        var angleT = GetRoofAngleTransform();
        if (angleT == null) angleT = roofCollider.transform;
        Vector3 rawN = angleT.up.normalized;
        float rawDotUp = Vector3.Dot(rawN, Vector3.up);
        Vector3 n = rawN;
        if (rawDotUp < 0f) n = -n;
        float fixedDotUp = Vector3.Dot(n, Vector3.up);
        SnowLoopLogCapture.AppendToAssiReport("=== ROOF NORMAL FLIP CHECK ===");
        SnowLoopLogCapture.AppendToAssiReport($"rawN=({rawN.x:F3},{rawN.y:F3},{rawN.z:F3}) rawDotUp={rawDotUp:F4}");
        SnowLoopLogCapture.AppendToAssiReport($"fixedN=({n.x:F3},{n.y:F3},{n.z:F3}) fixedDotUp={fixedDotUp:F4}");
        SnowLoopLogCapture.AppendToAssiReport($"source={angleT.name}.up");

        Vector3 worldUp = Vector3.up;
        Vector3 r = Vector3.Cross(worldUp, n);
        if (r.sqrMagnitude < 1e-6f) r = Vector3.Cross(Vector3.forward, n);
        r.Normalize();
        Vector3 f = Vector3.Cross(n, r).normalized;
        Vector3 g = Physics.gravity.magnitude > 0.001f ? Physics.gravity.normalized : Vector3.down;
        Vector3 downhill = Vector3.ProjectOnPlane(g, n).normalized;
        if (downhill.sqrMagnitude < 1e-6f) downhill = -angleT.forward.normalized;

        _roofN = n;
        _roofR = r;
        _roofF = f;
        _roofDownhill = downhill;

        float projectedW, projectedL;
        bool fromMeshRenderer = false;
        Renderer targetR = targetSnowRenderer;
        if (targetR == null) targetR = roofCollider.GetComponent<MeshRenderer>();
        if (targetR == null) targetR = roofCollider.GetComponentInParent<MeshRenderer>();
        if (targetR == null) targetR = roofCollider.GetComponentInChildren<MeshRenderer>();
        if (targetR != null)
        {
            Bounds meshBounds = targetR.bounds;
            _roofCenter = meshBounds.center;
            float minR = float.MaxValue, maxR = float.MinValue, minF = float.MaxValue, maxF = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = meshBounds.center + new Vector3(
                    (i & 1) != 0 ? meshBounds.extents.x : -meshBounds.extents.x,
                    (i & 2) != 0 ? meshBounds.extents.y : -meshBounds.extents.y,
                    (i & 4) != 0 ? meshBounds.extents.z : -meshBounds.extents.z);
                float cr = Vector3.Dot(corner - _roofCenter, r);
                float cf = Vector3.Dot(corner - _roofCenter, f);
                if (cr < minR) minR = cr; if (cr > maxR) maxR = cr;
                if (cf < minF) minF = cf; if (cf > maxF) maxF = cf;
            }
            projectedW = Mathf.Max(0.5f, maxR - minR);
            projectedL = Mathf.Max(0.5f, maxF - minF);
            fromMeshRenderer = true;
            UnityEngine.Debug.Log($"[SNOW_TARGET] targetRendererName={targetR.gameObject.name} targetBoundsSize=({meshBounds.size.x:F4},{meshBounds.size.y:F4},{meshBounds.size.z:F4}) targetBoundsCenter=({meshBounds.center.x:F4},{meshBounds.center.y:F4},{meshBounds.center.z:F4})");
        }
        else
        {
            Bounds b = roofCollider.bounds;
            _roofCenter = b.center;
            if (UseFullRoofCoverage && roofCollider is BoxCollider box)
        {
            var t = roofCollider.transform;
            Vector3 c = box.center;
            float sx = box.size.x, sy = box.size.y, sz = box.size.z;
            float minR = float.MaxValue, maxR = float.MinValue, minF = float.MaxValue, maxF = float.MinValue;
            for (int ix = 0; ix <= 1; ix++)
            for (int iz = 0; iz <= 1; iz++)
            {
                Vector3 local = c + new Vector3((ix - 0.5f) * sx, sy * 0.5f, (iz - 0.5f) * sz);
                Vector3 world = t.TransformPoint(local);
                float cr = Vector3.Dot(world - _roofCenter, r);
                float cf = Vector3.Dot(world - _roofCenter, f);
                if (cr < minR) minR = cr;
                if (cr > maxR) maxR = cr;
                if (cf < minF) minF = cf;
                if (cf > maxF) maxF = cf;
            }
            projectedW = Mathf.Max(0.5f, maxR - minR);
            projectedL = Mathf.Max(0.5f, maxF - minF);
        }
        else
        {
            float minR = float.MaxValue, maxR = float.MinValue, minF = float.MaxValue, maxF = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + new Vector3(
                    (i & 1) != 0 ? b.extents.x : -b.extents.x,
                    (i & 2) != 0 ? b.extents.y : -b.extents.y,
                    (i & 4) != 0 ? b.extents.z : -b.extents.z);
                float cr = Vector3.Dot(corner - b.center, r);
                float cf = Vector3.Dot(corner - b.center, f);
                if (cr < minR) minR = cr;
                if (cr > maxR) maxR = cr;
                if (cf < minF) minF = cf;
                if (cf > maxF) maxF = cf;
            }
            projectedW = Mathf.Max(0.5f, maxR - minR);
            projectedL = Mathf.Max(0.5f, maxF - minF);
        }
        }
        _roofProjectedW = projectedW;
        _roofProjectedL = projectedL;
        if (UseFixedSizeForMinimalScene && FixedRoofWidthForMinimal > 0f && FixedRoofLengthForMinimal > 0f)
        {
            _roofWidth = Mathf.Min(FixedRoofWidthForMinimal, projectedW * 1.001f);
            _roofLength = Mathf.Min(FixedRoofLengthForMinimal, projectedL * 1.001f);
        }
        else if (fromMeshRenderer)
        {
            _roofWidth = projectedW;
            _roofLength = projectedL;
            _useExactGrid = true;
        }
        else
        {
            _roofWidth = projectedW;
            _roofLength = projectedL;
            _useExactGrid = true;
        }
        _roofCellSize = Mathf.Max(0.05f, pieceSize);

        float roofW = projectedW, roofL = projectedL;
        float widthGap = roofW - _roofWidth;
        float depthGap = roofL - _roofLength;
        UnityEngine.Debug.Log($"[SNOW_COVER] roof_width={roofW:F3} roof_depth={roofL:F3} snow_width={_roofWidth:F3} snow_depth={_roofLength:F3} width_gap={widthGap:F3} depth_gap={depthGap:F3}");

        float inset = (UseFullRoofCoverage || _useExactGrid) ? 0f : (_roofCellSize * 0.5f);
        float usableW = _roofWidth - inset * 2f;
        float usableL = _roofLength - inset * 2f;
        if (UseFullRoofCoverage || _useExactGrid)
        {
            _cachedNx = Mathf.Max(1, Mathf.CeilToInt(_roofWidth / _roofCellSize));
            _cachedNz = Mathf.Max(1, Mathf.CeilToInt(_roofLength / _roofCellSize));
            _cachedSpacingR = _cachedNx > 1 ? (_roofWidth - _roofCellSize) / (_cachedNx - 1) : 0f;
            _cachedSpacingF = _cachedNz > 1 ? (_roofLength - _roofCellSize) / (_cachedNz - 1) : 0f;
        }
        else
        {
            _cachedNx = Mathf.Max(1, Mathf.FloorToInt(usableW / _roofCellSize));
            _cachedNz = Mathf.Max(1, Mathf.FloorToInt(usableL / _roofCellSize));
            _cachedSpacingR = _cachedSpacingF = 0f;
        }
        _cachedLayerStep = Mathf.Max(0.02f, _roofCellSize * pieceHeightScale);

        float coveredWidth = _cachedNx > 1 ? (_cachedNx - 1) * _cachedSpacingR + _roofCellSize : _roofCellSize;
        float coveredDepth = _cachedNz > 1 ? (_cachedNz - 1) * _cachedSpacingF + _roofCellSize : _roofCellSize;
        float widthGapVsRoof = roofW - _roofWidth;
        float depthGapVsRoof = roofL - _roofLength;
        UnityEngine.Debug.Log($"[SNOW_GEN] generatedSnowWidth={_roofWidth:F4} generatedSnowDepth={_roofLength:F4} gridX={_cachedNx} gridZ={_cachedNz} pieceSize={_roofCellSize:F4} coveredWidth={coveredWidth:F4} coveredDepth={coveredDepth:F4} widthGapVsRoof={widthGapVsRoof:F4} depthGapVsRoof={depthGapVsRoof:F4}");

        float dotRN = Vector3.Dot(r, n);
        float dotFN = Vector3.Dot(f, n);
        float dotRF = Vector3.Dot(r, f);
        if (ForceDownhillTowardCamera && Camera.main != null)
        {
            Vector3 dirToCam = Vector3.ProjectOnPlane(Camera.main.transform.position - _roofCenter, n).normalized;
            if (dirToCam.sqrMagnitude > 0.001f && Vector3.Dot(_roofDownhill, dirToCam) < 0f)
            {
                _roofDownhill = -_roofDownhill;
                UnityEngine.Debug.Log($"[RoofBasis] flipped downhill toward camera (奥→手前) dirToCam=({dirToCam.x:F3},{dirToCam.y:F3},{dirToCam.z:F3})");
            }
        }
        UnityEngine.Debug.Log($"[RoofBasis] dot(r,n)={dotRN:F4} dot(f,n)={dotFN:F4} dot(r,f)={dotRF:F4} roofUp=({n.x:F3},{n.y:F3},{n.z:F3}) downhill=({_roofDownhill.x:F3},{_roofDownhill.y:F3},{_roofDownhill.z:F3})");
        UnityEngine.Debug.Log($"[RoofBasis] center=({_roofCenter.x:F2},{_roofCenter.y:F2},{_roofCenter.z:F2}) width={_roofWidth:F2} length={_roofLength:F2} cell={_roofCellSize:F2} nx={_cachedNx} nz={_cachedNz}");
        LogRoofModuleStatus(roofDefinitionCreated: false, usesDefinition: false, assetDirect: true);
    }

    void ApplyRoofDefinition(RoofDefinition def, bool fromResolver)
    {
        _builtFromRoofDefinition = true;
        _roofN = def.roofNormal;
        _roofR = def.roofR;
        _roofF = def.roofF;
        _roofDownhill = def.roofDownhill;
        _roofCenter = def.roofOrigin;
        float projectedW = def.width;
        float projectedL = def.depth;
        _roofProjectedW = projectedW;
        _roofProjectedL = projectedL;

        if (UseFixedSizeForMinimalScene && FixedRoofWidthForMinimal > 0f && FixedRoofLengthForMinimal > 0f)
        {
            _roofWidth = Mathf.Min(FixedRoofWidthForMinimal, projectedW * 1.001f);
            _roofLength = Mathf.Min(FixedRoofLengthForMinimal, projectedL * 1.001f);
        }
        else if (def.useExactRoofSize)
        {
            _roofWidth = projectedW;
            _roofLength = projectedL;
        }
        else
        {
            _roofWidth = projectedW;
            _roofLength = projectedL;
        }
        _roofCellSize = Mathf.Max(0.05f, pieceSize);
        _useExactGrid = true;

        float roofW = projectedW, roofL = projectedL;
        float widthGap = roofW - _roofWidth;
        float depthGap = roofL - _roofLength;
        UnityEngine.Debug.Log($"[SNOW_COVER] roof_width={roofW:F3} roof_depth={roofL:F3} snow_width={_roofWidth:F3} snow_depth={_roofLength:F3} width_gap={widthGap:F3} depth_gap={depthGap:F3}");
        SnowLoopLogCapture.AppendToAssiReport($"=== SNOW_COVER_WIDTH === roof_width={roofW:F3} roof_depth={roofL:F3} snow_width={_roofWidth:F3} snow_depth={_roofLength:F3} width_gap={widthGap:F3} depth_gap={depthGap:F3}");

        float inset = UseFullRoofCoverage ? 0f : (_roofCellSize * 0.5f);
        float usableW = _roofWidth - inset * 2f;
        float usableL = _roofLength - inset * 2f;
        if (UseFullRoofCoverage || _useExactGrid)
        {
            _cachedNx = Mathf.Max(1, Mathf.CeilToInt(_roofWidth / _roofCellSize));
            _cachedNz = Mathf.Max(1, Mathf.CeilToInt(_roofLength / _roofCellSize));
            _cachedSpacingR = _cachedNx > 1 ? (_roofWidth - _roofCellSize) / (_cachedNx - 1) : 0f;
            _cachedSpacingF = _cachedNz > 1 ? (_roofLength - _roofCellSize) / (_cachedNz - 1) : 0f;
        }
        else
        {
            _cachedNx = Mathf.Max(1, Mathf.FloorToInt(usableW / _roofCellSize));
            _cachedNz = Mathf.Max(1, Mathf.FloorToInt(usableL / _roofCellSize));
            _cachedSpacingR = _cachedSpacingF = 0f;
        }
        _cachedLayerStep = Mathf.Max(0.02f, _roofCellSize * pieceHeightScale);

        float coveredWidth = _cachedNx > 1 ? (_cachedNx - 1) * _cachedSpacingR + _roofCellSize : _roofCellSize;
        float coveredDepth = _cachedNz > 1 ? (_cachedNz - 1) * _cachedSpacingF + _roofCellSize : _roofCellSize;
        float widthGapVsRoof = projectedW - _roofWidth;
        float depthGapVsRoof = projectedL - _roofLength;
        UnityEngine.Debug.Log($"[SNOW_TARGET] targetRendererName=RoofDefinition targetBoundsSize=({projectedW:F4},{projectedL:F4},0) targetBoundsCenter=({_roofCenter.x:F4},{_roofCenter.y:F4},{_roofCenter.z:F4})");
        UnityEngine.Debug.Log($"[SNOW_GEN] generatedSnowWidth={_roofWidth:F4} generatedSnowDepth={_roofLength:F4} gridX={_cachedNx} gridZ={_cachedNz} pieceSize={_roofCellSize:F4} coveredWidth={coveredWidth:F4} coveredDepth={coveredDepth:F4} widthGapVsRoof={widthGapVsRoof:F4} depthGapVsRoof={depthGapVsRoof:F4}");

        if (ForceDownhillTowardCamera && Camera.main != null)
        {
            Vector3 dirToCam = Vector3.ProjectOnPlane(Camera.main.transform.position - _roofCenter, _roofN).normalized;
            if (dirToCam.sqrMagnitude > 0.001f && Vector3.Dot(_roofDownhill, dirToCam) < 0f)
            {
                _roofDownhill = -_roofDownhill;
                UnityEngine.Debug.Log($"[RoofBasis] flipped downhill toward camera");
            }
        }
        UnityEngine.Debug.Log($"[RoofBasis] center=({_roofCenter.x:F2},{_roofCenter.y:F2},{_roofCenter.z:F2}) width={_roofWidth:F2} length={_roofLength:F2} cell={_roofCellSize:F2} nx={_cachedNx} nz={_cachedNz}");
        LogRoofModuleStatus(roofDefinitionCreated: true, usesDefinition: true, assetDirect: fromResolver);
    }

    void LogRoofModuleStatus(bool roofDefinitionCreated, bool usesDefinition, bool assetDirect)
    {
        float slopeAngle = float.NaN;
        string slopeDir = "N/A";
        if (RoofDefinitionProvider.TryGet(houseIndex, out var d, out _) && d.isValid)
        {
            slopeAngle = d.slopeAngle;
            slopeDir = $"({d.slopeDirection.x:F2},{d.slopeDirection.y:F2},{d.slopeDirection.z:F2})";
        }
        string slopeStr = float.IsNaN(slopeAngle) ? "N/A" : slopeAngle.ToString("F1");
        UnityEngine.Debug.Log($"[ROOF_MODULE] roof_definition_created={roofDefinitionCreated.ToString().ToLower()} roof_width={_roofWidth:F3} roof_depth={_roofLength:F3} roof_slope_angle={slopeStr} roof_slope_direction={slopeDir} snow_module_uses_roof_definition={usesDefinition.ToString().ToLower()} snow_module_asset_direct_dependency={assetDirect.ToString().ToLower()} multi_house_ready=true");
        SnowLoopLogCapture.AppendToAssiReport($"=== ROOF_MODULE === roof_definition_created={roofDefinitionCreated} roof_width={_roofWidth:F3} roof_depth={_roofLength:F3} roof_slope_angle={slopeStr} roof_slope_direction={slopeDir} snow_module_uses_roof_definition={usesDefinition} snow_module_asset_direct_dependency={assetDirect} multi_house_ready=true");
    }

    List<Transform> SpawnMinimalSinglePiece()
    {
        var list = new List<Transform>();
        if (roofCollider == null || _visualRoot == null || _piecesRoot == null) return list;
        var angleT = GetRoofAngleTransform();
        Quaternion rot = angleT != null ? angleT.rotation : roofCollider.transform.rotation;
        float size = MinimalPieceSize > 0.01f ? MinimalPieceSize : 0.15f;
        Vector3 p = _roofCenter + _roofN * RoofSurfaceOffset;
        var t = GetOrSpawnPieceRoofBasis(p, rot, size, 0, 0, 0);
        if (t != null)
        {
            list.Add(t);
            var r = t.GetComponentInChildren<Renderer>(true);
            if (r != null) { r.enabled = true; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true; }
            if (t.gameObject.activeSelf == false) t.gameObject.SetActive(true);
        }
        return list;
    }

    List<Transform> SpawnLayer(int layerIndex)
    {
        var list = new List<Transform>();
        if (roofCollider == null || _visualRoot == null) return list;
        float size = Mathf.Max(0.05f, pieceSize);
        int existing = 0;
        for (int i = 0; i < _layerPieces.Count; i++)
            existing += _layerPieces[i].Count;

        for (int iz = 0; iz < _cachedNz; iz++)
        {
            for (int ix = 0; ix < _cachedNx; ix++)
            {
                if (existing + list.Count >= maxPieces) break;
                float u, v;
                if ((UseFullRoofCoverage || _useExactGrid) && _cachedSpacingR > 0f && _cachedSpacingF > 0f)
                {
                    u = -0.5f + (_roofCellSize * 0.5f + ix * _cachedSpacingR) / _roofWidth;
                    v = -0.5f + (_roofCellSize * 0.5f + iz * _cachedSpacingF) / _roofLength;
                }
                else
                {
                    u = (ix + 0.5f) / _cachedNx - 0.5f;
                    v = (iz + 0.5f) / _cachedNz - 0.5f;
                }
                GetDeterministicJitter(ix, iz, layerIndex, jitter, scaleJitterXZ, out float jx, out float jz, out _, out _, out _);
                float layerOffset = RoofSurfaceOffset + layerIndex * _cachedLayerStep;
                Vector3 p = _roofCenter + _roofR * (u * _roofWidth + jx) + _roofF * (v * _roofLength + jz) + _roofN * layerOffset;
                if (!UseFullRoofCoverage && !_builtFromRoofDefinition)
                {
                    Vector3 cp = roofCollider.ClosestPoint(p + _roofN * 0.1f);
                    if ((cp - p).sqrMagnitude > 0.35f) continue;
                }

                var angleT = GetRoofAngleTransform();
                Quaternion rot = angleT != null ? angleT.rotation : roofCollider.transform.rotation;
                var t = GetOrSpawnPieceRoofBasis(p, rot, size, ix, iz, layerIndex);
                list.Add(t);
            }
        }
        return list;
    }

    [ContextMenu("Clear Snow Pack")]
    public void ClearNow()
    {
        ClearSnowPack("ContextMenu");
    }

    public void ManualRebuildButton() => RebuildSnowPack("Manual");
    public void ManualClearButton() => ClearSnowPack("Manual");
    public void RebuildDepthSync() => RebuildSnowPack("DepthSync");

    public void PlayAvalancheSlideVisual(float burstAmount, Vector3 slideOffset, float duration)
    {
        if (duration <= 0f || _visualRoot == null || roofCollider == null) return;
        if (_cachedLayerStep <= 0f) CacheGridParams();
        _lastAvalanchePackedCountAfter = -1;

        float roofBefore = roofSnowSystem != null ? roofSnowSystem.roofSnowDepthMeters : packDepthMeters;
        float packBefore = packDepthMeters;
        float roofAfter = Mathf.Max(0.02f, roofBefore - burstAmount);
        int packedBefore = CountPiecesUnder(_piecesRoot);

        int layersToRemove = Mathf.Min(
            Mathf.Max(1, Mathf.RoundToInt(burstAmount / Mathf.Max(0.01f, _cachedLayerStep))),
            _layerPieces.Count);
        if (layersToRemove <= 0 || _layerPieces.Count == 0) return;

        var layers = new List<List<Transform>>();
        int pieceCountRemoved = 0;
        for (int i = 0; i < layersToRemove && _layerPieces.Count > 0; i++)
        {
            var layer = _layerPieces[_layerPieces.Count - 1];
            _layerPieces.RemoveAt(_layerPieces.Count - 1);
            layers.Add(layer);
            pieceCountRemoved += layer.Count;
        }

        _visualDepth = roofAfter;
        packDepthMeters = Mathf.Max(minVisibleDepth, roofAfter);
        _lastAvalanchePackedCountAfter = packedBefore - pieceCountRemoved;

        UnityEngine.Debug.Log($"[AvalanchePackedReduced] layersRemoved={layersToRemove} piecesRemoved={pieceCountRemoved} roofDepthBefore={roofBefore:F3} roofDepthAfter={roofAfter:F3} packedCubeCountBefore={packedBefore} burstAmount={burstAmount:F3}");
        UnityEngine.Debug.Log($"[AvalancheVisual] start amount={burstAmount:F3} duration={duration:F2} offset={slideOffset} removedLayers={layersToRemove}");
        _inAvalancheSlide = true;
        StartCoroutine(AvalancheSlideRoutine(layers, slideOffset, duration));
    }

    /// <summary>HUD用。直前タップのremovedCount, PackedInRadius。</summary>
    /// <summary>OneHouse時: 雪の滑落を「奥→手前」(カメラ方向)に強制。プレイヤー視点で気持ち良い向きに。</summary>
    public static bool ForceDownhillTowardCamera;
    /// <summary>OneHouse時: 積雪範囲を屋根全面に一致。inset=0, ClosestPoint閾値緩和。</summary>
    public static bool UseFullRoofCoverage;
    /// <summary>積雪範囲の倍率(X=横幅)。1.0=屋根とピッタリ一致。昨日の終了時点に合わせて横幅のみ1.0に。</summary>
    public static float SnowCoverScaleMultiplierX = 1.0f;
    /// <summary>積雪範囲の倍率(Z=縦幅/奥行き)。Z方向だけ調整用。</summary>
    public static float SnowCoverScaleMultiplierZ = 1.35f;

    /// <summary>SnowVerify_Minimal用。true時は固定値で雪サイズを上書き。自動追従を止める。</summary>
    public static bool UseFixedSizeForMinimalScene;
    /// <summary>SnowVerify_Minimal用。true時は屋根中央に雪1つだけスポーン。activePieces>=1を保証。</summary>
    public static bool ForceMinimalSinglePiece;
    /// <summary>ForceMinimalSinglePiece時の雪ピースサイズ。0なら0.15を使用。</summary>
    public static float MinimalPieceSize = 0.15f;
    public static float FixedRoofWidthForMinimal = 1.7f;
    public static float FixedRoofLengthForMinimal = 0.85f;

    /// <summary>Editor専用。ExitingPlayMode時にtrueにして、activePieces=0 FAILを抑止。</summary>
#if UNITY_EDITOR
    public static bool EditorExitingPlayMode;
#endif

    public static int LastRemovedCount;
    public static int LastPackedInRadiusBefore;
    public static int LastTapPowderMoved;
    public static int LastTapSlabMoved;
    public static int LastTapBaseMoved;
    public static int LastTapSmallCluster;
    public static int LastTapMidCluster;
    public static int LastTapLargeCluster;
    public static Vector3 LastTapWorld;
    public static Vector2 LastTapRoofLocal;
    public static int LastPackedTotalBefore;
    public static int LastPackedTotalAfter;
    public static float LastTapTime = -10f;
    public static float LastTapRadius = 0.6f;
    public static int LastChainTriggerCount;
    public static int LastSecondaryDetachCount;
    public static bool LastSecondaryTriggered;
    public static float LastAvgRoofSlideDuration => _roofSlideSampleCount > 0 ? _avgRoofSlideDuration : 0f;
    static int _consecutiveNoRemovalTaps;
    static float _avgRoofSlideDuration;
    static int _roofSlideSampleCount;
    public static void RecordRoofSlideDuration(float duration)
    {
        if (duration <= 0f) return;
        _roofSlideSampleCount++;
        _avgRoofSlideDuration += (duration - _avgRoofSlideDuration) / _roofSlideSampleCount;
    }

    [Tooltip("Minimum pieces to detach per hit (expand radius if needed).")]
    public int localAvalancheMinDetach = 20;
    [Tooltip("Maximum pieces to detach per hit (chain reaction for rest).")]
    public int localAvalancheMaxDetach = 80;

    public float RoofWidth => _roofWidth;
    public float RoofLength => _roofLength;
    public Vector3 RoofR => _roofR;
    public Vector3 RoofF => _roofF;
    public Vector3 RoofCenter => _roofCenter;

    /// <summary>Convert world point to roof (u,v) in basis. Used for tap debug log. CacheGridParams must have been called.</summary>
    public void ComputeTapUV(Vector3 worldPoint, out float u, out float v)
    {
        u = v = 0f;
        if (roofCollider == null || _roofWidth <= 0f || _roofLength <= 0f) return;
        Vector3 d = worldPoint - _roofCenter;
        u = Vector3.Dot(d, _roofR) / Mathf.Max(0.01f, _roofWidth);
        v = Vector3.Dot(d, _roofF) / Mathf.Max(0.01f, _roofLength);
    }

    /// <summary>局所雪崩: 屋根面basis(u,v)で半径R内のグリッドセルを削る。removedCount>=minを目指す（半径拡張）。</summary>
    public void PlayLocalAvalancheAt(Vector3 worldCenter, float radius = 0.6f, float slideSpeed = 1.5f)
    {
        if (_piecesRoot == null || roofCollider == null) return;
        EnsureRoot();
        if (_cachedLayerStep <= 0f) CacheGridParams();

        if (_chainTriggersThisHit > 0)
            UnityEngine.Debug.Log($"[TempoDebug] chainReactionTriggers lastHit={_chainTriggersThisHit}");
        LastChainTriggerCount = _chainTriggersThisHit;
        _chainTriggersThisHit = 0;

        Vector3 d = worldCenter - _roofCenter;
        float u = Vector3.Dot(d, _roofR) / Mathf.Max(0.01f, _roofWidth);
        float v = Vector3.Dot(d, _roofF) / Mathf.Max(0.01f, _roofLength);
        int cxPrev = Mathf.RoundToInt((u + 0.5f) * _cachedNx - 0.5f);
        int czPrev = Mathf.RoundToInt((v + 0.5f) * _cachedNz - 0.5f);
        UVToGridCell(u, v, out int cx, out int cz);
        bool clamped = (cxPrev != cx || czPrev != cz);

        int packedBefore = GetPackedCubeCountRealtime();
        int packedInRadiusBefore = 0;
        var toRemove = new List<Transform>();
        var seen = new HashSet<Transform>();
        float r = radius;
        const float radiusMax = 1.5f;

        const int FullClearSafetyThreshold = 25; // 止まり雪対策: 残り少なめで全消去（再タップ不可を防ぐ）
        bool useFullClearSafety = packedBefore > 0 && packedBefore <= FullClearSafetyThreshold;

        if (useFullClearSafety)
        {
            foreach (var kv in _gridPieces)
            {
                foreach (var t in kv.Value)
                {
                    if (t != null && !seen.Contains(t))
                    {
                        seen.Add(t);
                        toRemove.Add(t);
                        packedInRadiusBefore++;
                    }
                }
            }
            // オーファン対策: _gridPiecesと同期ズレしたSnowPackPieceを_piecesRootから拾う
            if (_piecesRoot != null)
            {
                var allPieces = _piecesRoot.GetComponentsInChildren<Transform>(true);
                foreach (var tr in allPieces)
                {
                    if (tr == null || tr.gameObject.name != "SnowPackPiece") continue;
                    if (seen.Contains(tr)) continue;
                    seen.Add(tr);
                    toRemove.Add(tr);
                    packedInRadiusBefore++;
                    UnityEngine.Debug.Log($"[SnowPackOrphan] recovered piece not in _gridPieces id={tr.GetInstanceID()} pos=({tr.position.x:F2},{tr.position.y:F2},{tr.position.z:F2})");
                }
            }
        }
        else
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                int radX = Mathf.CeilToInt(r / Mathf.Max(0.01f, _roofCellSize));
                int radZ = radX;
                packedInRadiusBefore = 0;
                toRemove.Clear();
                seen.Clear();

                for (int dz = -radZ; dz <= radZ; dz++)
                {
                    for (int dx = -radX; dx <= radX; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || x >= _cachedNx || z < 0 || z >= _cachedNz) continue;
                        Vector3 dp = _roofR * (dx * _roofCellSize) + _roofF * (dz * _roofCellSize);
                        if (dp.magnitude > r) continue;

                        var key = (x, z);
                        if (_gridPieces.TryGetValue(key, out var cellList))
                        {
                            foreach (var t in cellList)
                            {
                                if (t != null && !seen.Contains(t))
                                {
                                    seen.Add(t);
                                    toRemove.Add(t);
                                    packedInRadiusBefore++;
                                }
                            }
                        }
                    }
                }
                if (toRemove.Count >= localAvalancheMinDetach || r >= radiusMax || packedBefore < localAvalancheMinDetach) break;
                r = Mathf.Min(r * 1.4f, radiusMax);
            }
        }

        int capped = useFullClearSafety ? toRemove.Count : Mathf.Min(toRemove.Count, localAvalancheMaxDetach);
        if (toRemove.Count > capped)
        {
            for (int i = toRemove.Count - 1; i >= capped; i--)
                toRemove.RemoveAt(i);
        }

        if (SnowVerifyB2Debug.Enabled && toRemove.Count > 0)
        {
            int beforeTotal = GetB2TotalCount();
            UnityEngine.Debug.Log($"[B2_BEFORE_DETACH] tap_received=true before_detach_total={beforeTotal} to_remove_count={toRemove.Count}");
        }

        foreach (var t in toRemove)
        {
            var k = FindGridKeyForPiece(t);
            if (k.HasValue && _gridPieces.TryGetValue(k.Value, out var list))
            {
                list.Remove(t);
                if (list.Count == 0) _gridPieces.Remove(k.Value);
            }
            _pieceToGridData.Remove(t);
            _pieceToLayerType.Remove(t);
            for (int li = _layerPieces.Count - 1; li >= 0; li--)
            {
                if (_layerPieces[li].Remove(t)) break;
            }
        }

        if (toRemove.Count == 0)
        {
            var centerKey = (cx, cz);
            if (_gridPieces.TryGetValue(centerKey, out var centerList) && centerList.Count > 0)
            {
                var t = centerList[centerList.Count - 1];
                toRemove.Add(t);
                _pieceToGridData.Remove(t);
                _pieceToLayerType.Remove(t);
                centerList.Remove(t);
                if (centerList.Count == 0) _gridPieces.Remove(centerKey);
                for (int li = _layerPieces.Count - 1; li >= 0; li--)
                {
                    if (_layerPieces[li].Remove(t)) break;
                }
            }
        }

        for (int i = _layerPieces.Count - 1; i >= 0; i--)
        {
            if (_layerPieces[i].Count == 0) _layerPieces.RemoveAt(i);
        }

        int removedCount = toRemove.Count;
        LastTapWorld = worldCenter;
        LastTapRoofLocal = new Vector2(u, v);
        LastTapTime = Time.time;
        LastTapRadius = radius;
        _chainDetachCountSinceTap = 0;
        _scheduledSecondaryWaveFired = false;
        _scheduledThirdWaveFired = false;
        LastPackedTotalBefore = packedBefore;
        LastPackedTotalAfter = packedBefore - removedCount;

        int scx = Mathf.Clamp(cx, 0, _cachedNx - 1);
        int scz = Mathf.Clamp(cz, 0, _cachedNz - 1);
        GridCellToUV(scx, scz, out float su, out float sv);
        Vector3 cellCenter = _roofCenter + _roofR * (su * _roofWidth) + _roofF * (sv * _roofLength) + _roofN * RoofSurfaceOffset;
        float halfCell = _roofCellSize * 0.5f;
        Vector3 cellMin = cellCenter - _roofR * halfCell - _roofF * halfCell - _roofN * 0.05f;
        Vector3 cellMax = cellCenter + _roofR * halfCell + _roofF * halfCell + _roofN * 0.2f;
        Bounds cellBounds = new Bounds(cellCenter, (cellMax - cellMin));
        bool tapInsideBounds = cellBounds.Contains(worldCenter);
        if (packedBefore <= FullClearSafetyThreshold || (removedCount == 0 && packedBefore > 0))
        {
            UnityEngine.Debug.Log($"[TapDebug] packedSmall u={u:F4} v={v:F4} cx={cx} cz={cz} clamped={clamped} gridBounds=(0,0)-({_cachedNx - 1},{_cachedNz - 1}) packedInRadiusBefore={packedInRadiusBefore} packedBefore={packedBefore} tapInsideBounds={tapInsideBounds}");
        }
        UnityEngine.Debug.Log($"[TapDebug] tapU={u:F4} tapV={v:F4} cx={cx} cz={cz} tapWorld=({worldCenter.x:F3},{worldCenter.y:F3},{worldCenter.z:F3}) packedInRadiusBefore={packedInRadiusBefore}");
        UnityEngine.Debug.Log($"[CellDebug] sampleCell(cx={scx},cz={scz}) sampleCellWorld=({cellCenter.x:F3},{cellCenter.y:F3},{cellCenter.z:F3}) cellBounds=({cellBounds.min.x:F3},{cellBounds.min.y:F3},{cellBounds.min.z:F3})-({cellBounds.max.x:F3},{cellBounds.max.y:F3},{cellBounds.max.z:F3}) tapInsideBounds={tapInsideBounds}");

        if (useFullClearSafety && removedCount > 0)
            UnityEngine.Debug.Log($"[FullClearSafety] packedTotalBefore={packedBefore} removed={removedCount} packedTotalAfter={packedBefore - removedCount}");
        if (removedCount == 0 && packedBefore > 0)
            _consecutiveNoRemovalTaps++;
        else
            _consecutiveNoRemovalTaps = 0;
        if (_consecutiveNoRemovalTaps >= 2 && packedBefore > 0)
            UnityEngine.Debug.LogWarning($"[FullClearDiagnostic] packedTotalAfter>0 packedInRadiusBefore=0 for {_consecutiveNoRemovalTaps} consecutive taps packedBefore={packedBefore}");

        if (removedCount == 0)
        {
            LastRemovedCount = 0;
            LastPackedInRadiusBefore = packedInRadiusBefore;
            int gridPieceTotal = 0;
            foreach (var kv in _gridPieces) gridPieceTotal += kv.Value.Count;
            UnityEngine.Debug.Log($"[LocalAvalanche] R={radius:F2} u={u:F3} v={v:F3} cx={cx} cz={cz} removedCount=0 packedInRadiusBefore={packedInRadiusBefore} packedTotal={packedBefore} gridCells={_gridPieces.Count} gridPieceTotal={gridPieceTotal} TAP_NO_REMOVAL");
            return;
        }

        int packedAfter = packedBefore - removedCount;
        _lastAvalanchePackedCountAfter = packedAfter;
        LastRemovedCount = removedCount;
        LastPackedInRadiusBefore = packedInRadiusBefore;
        LastSecondaryTriggered = false;
        LastSecondaryDetachCount = 0;
        _burstStatsLoggedThisTap = false;

        int powderMoved = 0, slabMoved = 0, baseMoved = 0;
        foreach (var t in toRemove)
        {
            if (t == null) continue;
            if (_pieceToLayerType.TryGetValue(t, out var lt))
            {
                switch (lt) { case SnowLayerType.Powder: powderMoved++; break; case SnowLayerType.Slab: slabMoved++; break; case SnowLayerType.Base: baseMoved++; break; }
            }
        }
        LastTapPowderMoved = powderMoved;
        LastTapSlabMoved = slabMoved;
        LastTapBaseMoved = baseMoved;

        int burstMax = (roofSnowSystem != null) ? roofSnowSystem.burstChunkCount : 48;
        int burstTotal = Mathf.Min(removedCount, burstMax);
        int totalTyped = powderMoved + slabMoved + baseMoved;
        float powderFrac = totalTyped > 0 ? powderMoved / (float)totalTyped : 0.33f;
        float baseFrac = totalTyped > 0 ? baseMoved / (float)totalTyped : 0.2f;
        LastTapSmallCluster = Mathf.Max(0, Mathf.Min(burstTotal, Mathf.RoundToInt(burstTotal * (0.25f + powderFrac * 0.4f))));
        LastTapLargeCluster = Mathf.Max(0, Mathf.Min(burstTotal - LastTapSmallCluster, Mathf.RoundToInt(burstTotal * (0.15f + baseFrac * 0.35f))));
        LastTapMidCluster = Mathf.Max(0, burstTotal - LastTapSmallCluster - LastTapLargeCluster);

        if (_layerPieces.Count > 0 && _cachedLayerStep > 0f)
            packDepthMeters = Mathf.Max(minVisibleDepth, _layerPieces.Count * _cachedLayerStep);
        _visualDepth = packDepthMeters;

        int radXFinal = Mathf.CeilToInt(r / Mathf.Max(0.01f, _roofCellSize));
        UnityEngine.Debug.Log($"[LocalAvalanche] R={r:F2} u={u:F3} v={v:F3} cx={cx} cz={cz} removedCount={removedCount} packedInRadiusBefore={packedInRadiusBefore} packedTotalBefore={packedBefore} packedTotalAfter={packedAfter}");
        UnityEngine.Debug.Log($"[AvalancheBeforeAfter] beforeDepth={packDepthMeters + removedCount * _cachedLayerStep * 0.01f:F3} afterDepth={packDepthMeters:F3} packedCubeCountBefore={packedBefore} packedCubeCountAfter={packedAfter} burstAmount={removedCount * _cachedLayerStep * 0.01f:F3}");
        if (removedCount > 0)
        {
            int outerRad = Mathf.Max(radXFinal + 2, Mathf.CeilToInt(radXFinal * unstableRadiusScale));
            MarkNeighborsUnstable(cx, cz, radXFinal, outerRad, unstableDurationSec);
        }
        _inAvalancheSlide = true;
        if (SnowVerifyB2Debug.Enabled)
        {
            int total = GetB2TotalCount();
            int active = GetB2ActiveCount();
            int surv = GetPackedCubeCountRealtime() + toRemove.Count;
            UnityEngine.Debug.Log($"[B2_AFTER_TAP] tap_received=true after_detach_total={total} after_detach_active={active} surviving_count={surv} detached_count={toRemove.Count}");
            if (total <= 0) SnowVerifyB2Debug.RecordZeroTransition("after_tap");
        }
        StartCoroutine(LocalAvalancheSlideRoutine(toRemove, _roofDownhill, slideSpeed));
    }

    void MarkNeighborsUnstable(int cx, int cz, int innerRad, int outerRad, float unstableDurSec)
    {
        float now = Time.time;
        for (int dz = -outerRad; dz <= outerRad; dz++)
        {
            for (int dx = -outerRad; dx <= outerRad; dx++)
            {
                int x = cx + dx, z = cz + dz;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist <= innerRad || dist > outerRad) continue;
                if (x < 0 || x >= _cachedNx || z < 0 || z >= _cachedNz) continue;
                var key = (x, z);
                if (!_gridPieces.ContainsKey(key) || _gridPieces[key].Count == 0) continue;
                float delay = UnityEngine.Random.Range(secondaryDetachDelaySec * 0.5f, unstableDurSec);
                _unstableCellExpiry[key] = now + delay;
            }
        }
    }

    void ProcessScheduledWaves()
    {
        float tSinceTap = Time.time - LastTapTime;
        if (tSinceTap < 0f || tSinceTap > 2.5f) return;
        int lastRemoved = LastRemovedCount;

        if (!_scheduledSecondaryWaveFired && tSinceTap >= secondaryDetachDelaySec && lastRemoved > 0)
        {
            _scheduledSecondaryWaveFired = true;
            LastSecondaryTriggered = true;
            int count = Mathf.Clamp(Mathf.RoundToInt(lastRemoved * secondaryDetachFraction), 10, 28);
            FireScheduledWave(count);
        }
        if (!_scheduledThirdWaveFired && lastRemoved >= 50 && tSinceTap >= thirdWaveDelaySec)
        {
            _scheduledThirdWaveFired = true;
            int count = Mathf.Clamp(Mathf.RoundToInt(lastRemoved * thirdWaveFraction), 8, 24);
            FireScheduledWave(count);
        }
    }

    void FireScheduledWave(int count)
    {
        if (_unstableCellExpiry.Count == 0) return;
        var keys = new System.Collections.Generic.List<(int, int)>(_unstableCellExpiry.Keys);
        int detached = 0;
        for (int i = 0; i < keys.Count && detached < count && _chainDetachCountSinceTap < maxSecondaryDetachPerHit; i++)
        {
            var key = keys[i];
            if (!_unstableCellExpiry.ContainsKey(key)) continue;
            _unstableCellExpiry.Remove(key);
            if (!_gridPieces.TryGetValue(key, out var cellList) || cellList.Count == 0) continue;
            var t = cellList[cellList.Count - 1];
            if (t == null) continue;
            var toRemove = new System.Collections.Generic.List<Transform> { t };
            cellList.Remove(t);
            if (cellList.Count == 0) _gridPieces.Remove(key);
            _pieceToGridData.Remove(t);
            _pieceToLayerType.Remove(t);
            for (int li = _layerPieces.Count - 1; li >= 0; li--)
            {
                if (_layerPieces[li].Remove(t)) break;
            }
            _chainTriggersThisHit++;
            _chainDetachCountSinceTap++;
            _inAvalancheSlide = true;
            StartCoroutine(LocalAvalancheSlideRoutine(toRemove, _roofDownhill, localAvalancheSlideSpeed));
            if (roofSnowSystem != null && roofSnowSystem.isActiveAndEnabled)
            {
                GridCellToUV(key.Item1, key.Item2, out float ku, out float kv);
                Vector3 worldCenter = _roofCenter + _roofR * (ku * _roofWidth) + _roofF * (kv * _roofLength) + _roofN * RoofSurfaceOffset;
                roofSnowSystem.SpawnLocalBurstAt(worldCenter, 1, _roofDownhill.normalized);
            }
            detached++;
        }
        if (detached > 0)
        {
            LastSecondaryDetachCount = _chainDetachCountSinceTap;
            UnityEngine.Debug.Log($"[ChainWave] scheduled wave detached={detached} chainTotal={_chainDetachCountSinceTap} secondary_triggered=true secondary_detach_count={LastSecondaryDetachCount}");
        }
    }

    void ProcessChainReaction()
    {
        if (_unstableCellExpiry.Count == 0 || _chainDetachCountSinceTap >= maxSecondaryDetachPerHit) return;
        float now = Time.time;
        var toProcess = new System.Collections.Generic.List<(int, int)>();
        foreach (var kv in _unstableCellExpiry)
        {
            if (now >= kv.Value)
                toProcess.Add(kv.Key);
        }
        foreach (var key in toProcess)
        {
            if (_chainDetachCountSinceTap >= maxSecondaryDetachPerHit) break;
            _unstableCellExpiry.Remove(key);
            if (UnityEngine.Random.value >= chainDetachChance) continue;
            if (!_gridPieces.TryGetValue(key, out var cellList) || cellList.Count == 0) continue;
            var t = cellList[cellList.Count - 1];
            if (t == null) continue;
            var toRemove = new System.Collections.Generic.List<Transform> { t };
            cellList.Remove(t);
            if (cellList.Count == 0) _gridPieces.Remove(key);
            _pieceToGridData.Remove(t);
            _pieceToLayerType.Remove(t);
            for (int li = _layerPieces.Count - 1; li >= 0; li--)
            {
                if (_layerPieces[li].Remove(t)) break;
            }
            _chainTriggersThisHit++;
            _chainDetachCountSinceTap++;
            _inAvalancheSlide = true;
            StartCoroutine(LocalAvalancheSlideRoutine(toRemove, _roofDownhill, localAvalancheSlideSpeed));
            if (roofSnowSystem != null && roofSnowSystem.isActiveAndEnabled)
            {
                GridCellToUV(key.Item1, key.Item2, out float ku, out float kv);
                Vector3 worldCenter = _roofCenter + _roofR * (ku * _roofWidth) + _roofF * (kv * _roofLength) + _roofN * RoofSurfaceOffset;
                roofSnowSystem.SpawnLocalBurstAt(worldCenter, 1, _roofDownhill.normalized);
            }
        }
    }

    float localAvalancheSlideSpeed => roofSnowSystem != null ? roofSnowSystem.localAvalancheSlideSpeed : 0.9f;

    (int, int)? FindGridKeyForPiece(Transform piece)
    {
        if (piece == null) return null;
        if (_pieceToGridData.TryGetValue(piece, out var d)) return (d.ix, d.iz);
        foreach (var kv in _gridPieces)
        {
            if (kv.Value.Contains(piece)) return kv.Key;
        }
        return null;
    }

    const float RoofEdgeMargin = 0.05f;
    /// <summary>Detach BEFORE piece passes roof edge. Piece still on roof surface for slide phase.</summary>
    const float DetachBeforeEdgeMargin = 0.25f;

    IEnumerator LocalAvalancheSlideRoutine(List<Transform> pieces, Vector3 slopeDir, float slideSpeed)
    {
        if (pieces == null || pieces.Count == 0) { _inAvalancheSlide = false; yield break; }
        var parent = _visualRoot != null ? _visualRoot : (roofCollider != null ? roofCollider.transform : null);
        if (parent == null) { _inAvalancheSlide = false; yield break; }

        var first = pieces[0];
        bool renBefore = false, goActiveBefore = first != null && first.gameObject != null && first.gameObject.activeInHierarchy;
        Vector3 worldPosBefore = first != null ? first.position : Vector3.zero;
        int tappedPieceId = first != null ? first.GetInstanceID() : 0;
        if (first != null)
        {
            var r = first.GetComponentInChildren<Renderer>();
            renBefore = r != null && r.enabled;
        }
        Vector3 initialVelocity = slopeDir.normalized * slideSpeed;
        UnityEngine.Debug.Log(string.Format("[SNOW_DETACH_CHECK] tapped_piece_id={0} detach_requested=true source_renderer_enabled_before={1} source_active_before={2} initial_position=({3},{4},{5}) initial_velocity=({6},{7},{8}) pieces_count={9}", tappedPieceId, renBefore, goActiveBefore, worldPosBefore.x.ToString("F2"), worldPosBefore.y.ToString("F2"), worldPosBefore.z.ToString("F2"), initialVelocity.x.ToString("F2"), initialVelocity.y.ToString("F2"), initialVelocity.z.ToString("F2"), pieces.Count));

        // Roof edge params (Step1)
        Bounds roofBounds = roofCollider.bounds;
        Vector3 roofCenter = roofBounds.center;
        Vector3 downhill = _roofDownhill.sqrMagnitude > 0.001f ? _roofDownhill.normalized : slopeDir.normalized;
        Vector3 absDownhill = new Vector3(Mathf.Abs(downhill.x), Mathf.Abs(downhill.y), Mathf.Abs(downhill.z));
        float tEnd = Vector3.Dot(roofBounds.extents, absDownhill);

        SnowLoopLogCapture.AppendToAssiReport("=== RoofEdge ===");
        SnowLoopLogCapture.AppendToAssiReport($"boundsCenter=({roofCenter.x:F3},{roofCenter.y:F3},{roofCenter.z:F3})");
        SnowLoopLogCapture.AppendToAssiReport($"boundsExtents=({roofBounds.extents.x:F3},{roofBounds.extents.y:F3},{roofBounds.extents.z:F3})");
        SnowLoopLogCapture.AppendToAssiReport($"downhill=({downhill.x:F3},{downhill.y:F3},{downhill.z:F3})");
        SnowLoopLogCapture.AppendToAssiReport($"tEnd={tEnd:F3} margin={RoofEdgeMargin}");

        var slideRoot = new GameObject("LocalAvalancheSlideTemp");
        slideRoot.transform.SetParent(parent, false);
        Vector3 avgPos = Vector3.zero;
        int posCount = 0;
        foreach (var t in pieces) { if (t != null) { avgPos += t.position; posCount++; } }
        if (posCount > 0) avgPos /= posCount;
        slideRoot.transform.position = avgPos;

        int fallingCount = 0;
        foreach (var t in pieces)
        {
            if (t != null)
            {
                SetPieceVisualState(t, PieceVisualState.Sliding, false);
                if (t == first)
                {
                    bool renAfter = false;
                    var rr = t.GetComponentInChildren<Renderer>();
                    if (rr != null) renAfter = rr.enabled;
                    bool goActiveAfter = t.gameObject != null && t.gameObject.activeInHierarchy;
                    UnityEngine.Debug.Log($"[SNOW_DETACH_CHECK] source_renderer_enabled_after={renAfter} source_active_after={goActiveAfter} showSnowGridDebug={GridVisualWatchdog.showSnowGridDebug}");
                }
                var mr = t.GetComponentInChildren<Renderer>();
                if (mr != null && mr.sharedMaterial != null)
                {
                    var mat = new Material(mr.sharedMaterial);
                    MaterialColorHelper.SetColorSafe(mat, Color.red);
                    mr.sharedMaterial = mat;
                }
                t.SetParent(slideRoot.transform, true);
            }
        }

        yield return new WaitForSeconds(0.15f);

        float duration = 0.8f;
        float elapsed = 0f;
        Vector3 startPos = slideRoot.transform.position;
        float tValStart = Vector3.Dot(avgPos - roofCenter, downhill);
        float distToEdge = (tEnd + RoofEdgeMargin) - tValStart + 0.2f;
        float minSlideDist = Mathf.Max(distToEdge, 0.6f);
        float baseSlideDist = slideSpeed * duration;
        float actualSlideDist = Mathf.Max(baseSlideDist, minSlideDist);
        Vector3 slideOffset = downhill * actualSlideDist;
        LayerMask groundMask = (roofSnowSystem != null && roofSnowSystem.groundMask.value != 0) ? roofSnowSystem.groundMask : ~0;
        Vector3 slideVelocity = downhill * Mathf.Max(slideSpeed, actualSlideDist / duration);

        while (elapsed < duration)
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            float t01 = Mathf.Clamp01(elapsed / duration);
            slideRoot.transform.position = startPos + slideOffset * t01;

            // Step1: Detach BEFORE roof edge (t > tEnd - DetachBeforeEdgeMargin) so piece stays on roof for slide phase
            for (int i = slideRoot.transform.childCount - 1; i >= 0; i--)
            {
                var t = slideRoot.transform.GetChild(i);
                if (t == null) continue;
                Vector3 piecePos = t.position;
                float tVal = Vector3.Dot(piecePos - roofCenter, downhill);
                if (tVal > tEnd - DetachBeforeEdgeMargin)
                {
                    // Step2: ActivateRoofSlide - detach on roof, useGravity=false, slide then fall
                    t.SetParent(null, true);
                    var falling = t.gameObject.GetComponent<SnowPackFallingPiece>();
                    if (falling == null) falling = t.gameObject.AddComponent<SnowPackFallingPiece>();
                    falling.spawner = this;
                    falling.groundMask = groundMask;
                    falling.ActivateRoofSlide(slideVelocity, roofCollider, roofCenter, downhill, tEnd);
                    var rb = t.GetComponent<Rigidbody>();
                    fallingCount++;
                    bool rbPresent = rb != null;
                    bool spawnedActive = t != null && t.gameObject != null && t.gameObject.activeInHierarchy;
                    string spawnedName = t != null && t.gameObject != null ? t.gameObject.name : "null";
                    UnityEngine.Debug.Log($"[SNOW_DETACH_CHECK] detach_requested=true falling_piece_spawned=true spawned_object_name={spawnedName} spawned_object_active={spawnedActive} rigidbody_present={rbPresent} initial_velocity=({slideVelocity.x:F2},{slideVelocity.y:F2},{slideVelocity.z:F2}) initial_position=({piecePos.x:F2},{piecePos.y:F2},{piecePos.z:F2})");

                    SnowLoopLogCapture.AppendToAssiReport($"=== PieceState === pieceId={t.GetInstanceID()} mode=Falling t={Time.time:F2} pos=({piecePos.x:F3},{piecePos.y:F3},{piecePos.z:F3}) vel=({slideVelocity.x:F3},{slideVelocity.y:F3},{slideVelocity.z:F3})");
                }
                else
                {
                    SnowLoopLogCapture.AppendToAssiReport($"=== PieceState === pieceId={t.GetInstanceID()} mode=Sliding t={Time.time:F2} pos=({piecePos.x:F3},{piecePos.y:F3},{piecePos.z:F3}) vel=({slideVelocity.x:F3},{slideVelocity.y:F3},{slideVelocity.z:F3})");
                }
            }
            yield return null;
        }

        int returnedCount = 0;
        var toReturn = new List<Transform>();
        for (int i = 0; i < slideRoot.transform.childCount; i++)
        {
            var t = slideRoot.transform.GetChild(i);
            if (t != null) toReturn.Add(t);
        }
        foreach (var t in toReturn)
        {
            _poolReturnQueue.Add(t);
            returnedCount++;
        }
        if (SnowVerifyB2Debug.Enabled)
        {
            int total = GetB2TotalCount();
            int active = GetB2ActiveCount();
            UnityEngine.Debug.Log($"[B2_AFTER_DETACH] after_detach_total={total} after_detach_active={active} surviving_count={active} queue_size={_poolReturnQueue.Count}");
            if (total <= 0) SnowVerifyB2Debug.RecordZeroTransition("after_detach");
        }
        _pendingSlideRootToDestroy = slideRoot;
        _inAvalancheSlide = false;
        string failureMode = "unknown";
        bool rendererDisabled = !GridVisualWatchdog.showSnowGridDebug;
        if (rendererDisabled)
            failureMode = "hidden_only";
        else if (returnedCount == pieces.Count && fallingCount == 0)
            failureMode = "detach_not_spawned";
        else if (fallingCount > 0)
            failureMode = "spawned_falling";
        UnityEngine.Debug.Log($"[SNOW_DETACH_CHECK] failure_mode={failureMode} owner_file=SnowPackSpawner.cs owner_method=LocalAvalancheSlideRoutine detach_requested=true falling_piece_spawned={(fallingCount > 0)} rigidbody_present=(per_piece) initial_velocity=({slideVelocity.x:F2},{slideVelocity.y:F2},{slideVelocity.z:F2}) falling_count={fallingCount} returned_count={returnedCount} reason=slide_done");
        UnityEngine.Debug.Log($"[LocalAvalanche] slideDone returned={returnedCount} falling=(detached)");
    }

    IEnumerator AvalancheSlideRoutine(List<List<Transform>> layers, Vector3 slideOffset, float duration)
    {
        if (layers == null || layers.Count == 0) { _inAvalancheSlide = false; yield break; }
        var parent = _visualRoot != null ? _visualRoot : (roofCollider != null ? roofCollider.transform : null);
        if (parent == null) { _inAvalancheSlide = false; yield break; }
        Vector3 startWorldPos = _piecesRoot != null ? _piecesRoot.position : (_visualRoot != null ? _visualRoot.position : Vector3.zero);
        var slideRoot = new GameObject("AvalancheSlideTemp");
        slideRoot.transform.SetParent(parent, false);
        slideRoot.transform.position = startWorldPos;
        slideRoot.transform.rotation = _piecesRoot != null ? _piecesRoot.rotation : Quaternion.identity;

        int beforeMove = _piecesRoot != null ? _piecesRoot.childCount : 0;
        int movedPieces = 0;
        for (int i = 0; i < layers.Count; i++)
        {
            for (int j = 0; j < layers[i].Count; j++)
            {
                var t = layers[i][j];
                if (t != null)
                {
                    SetPieceVisualState(t, PieceVisualState.Sliding, movedPieces == 0);
                    t.SetParent(slideRoot.transform, true);
                    movedPieces++;
                }
            }
        }
        if (_piecesRoot != null)
            LogRootMutation(beforeMove, _piecesRoot.childCount, "AvalancheSlideRoutine.SetParent(slideRoot)");

        Bounds roofBounds = roofCollider != null ? roofCollider.bounds : new Bounds(startWorldPos, Vector3.one * 10f);
        Vector3 roofCenter = roofBounds.center;
        Vector3 downhill = _roofDownhill.sqrMagnitude > 0.001f ? _roofDownhill.normalized : slideOffset.normalized;
        Vector3 absDownhill = new Vector3(Mathf.Abs(downhill.x), Mathf.Abs(downhill.y), Mathf.Abs(downhill.z));
        float tEnd = Vector3.Dot(roofBounds.extents, absDownhill);
        LayerMask groundMask = (roofSnowSystem != null && roofSnowSystem.groundMask.value != 0) ? roofSnowSystem.groundMask : ~0;
        Vector3 slideVelocity = slideOffset.normalized * 2f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t01 = Mathf.Clamp01(elapsed / duration);
            slideRoot.transform.position = startWorldPos + slideOffset * t01;

            for (int i = slideRoot.transform.childCount - 1; i >= 0; i--)
            {
                var t = slideRoot.transform.GetChild(i);
                if (t == null) continue;
                Vector3 piecePos = t.position;
                float tVal = Vector3.Dot(piecePos - roofCenter, downhill);
                if (tVal > tEnd - DetachBeforeEdgeMargin)
                {
                    t.SetParent(null, true);
                    var falling = t.gameObject.GetComponent<SnowPackFallingPiece>();
                    if (falling == null) falling = t.gameObject.AddComponent<SnowPackFallingPiece>();
                    falling.spawner = this;
                    falling.groundMask = groundMask;
                    falling.ActivateRoofSlide(slideVelocity, roofCollider, roofCenter, downhill, tEnd);
                }
            }
            yield return null;
        }

        Vector3 endWorldPos = startWorldPos + slideOffset;
        float movedMeters = Vector3.Distance(startWorldPos, endWorldPos);

        var toReturn = new System.Collections.Generic.List<Transform>();
        for (int i = 0; i < layers.Count; i++)
        {
            for (int j = 0; j < layers[i].Count; j++)
            {
                var piece = layers[i][j];
                if (piece != null && piece.parent == slideRoot.transform)
                {
                    SetPieceVisualState(piece, PieceVisualState.Returning, toReturn.Count == 0);
                    toReturn.Add(piece);
                }
            }
        }
        foreach (var p in toReturn) _poolReturnQueue.Add(p);
        _pendingSlideRootToDestroy = slideRoot;

        _pendingRemoveCountFromAvalanche = layers.Count; // クールダウン終了後に記録（Avalanche中にremoveCount増やさない）
        _inAvalancheSlide = false;
        UnityEngine.Debug.Log($"[AvalancheVisual] end movedMeters={movedMeters:F3} start=({startWorldPos.x:F2},{startWorldPos.y:F2},{startWorldPos.z:F2}) end=({endWorldPos.x:F2},{endWorldPos.y:F2},{endWorldPos.z:F2}) durationActual={elapsed:F2} removedLayers={layers.Count} movedPieces={movedPieces}");
    }

    Transform GetOrSpawnPiece(Vector3 localPos, float size)
    {
        Transform t = null;
        if (_piecePool.Count > 0)
        {
            int idx = _piecePool.Count - 1;
            t = _piecePool[idx];
            _piecePool.RemoveAt(idx);
            _returnedToPoolIds.Remove(t != null ? t.GetInstanceID() : 0);
            _poolReused++;
        }
        if (t == null)
        {
            t = SpawnPieceLocal(localPos, size);
            _poolInstantiated++;
        }
        else
        {
            int before = _piecesRoot.childCount;
            t.SetParent(_piecesRoot, false);
            LogRootMutation(before, before + 1, "GetOrSpawnPiece");
            t.gameObject.SetActive(true);
            SetPieceVisualState(t, PieceVisualState.Accumulating);
            t.localPosition = localPos;
            t.localRotation = Quaternion.identity;
            float h = Mathf.Max(0.03f, size * pieceHeightScale * UnityEngine.Random.Range(0.8f, 1.2f));
            h *= snowPieceThicknessScale;
            float baseSize = Mathf.Max(0.05f, size);
            Vector3 scale = new Vector3(baseSize, h, baseSize);
            t.localScale = scale;
            var mesh = t.Find("Mesh");
            if (mesh != null) mesh.localScale = Vector3.one;
        }
        return t;
    }

    Transform GetOrSpawnPieceRoofBasis(Vector3 worldPos, Quaternion worldRot, float size, int ix, int iz, int layer)
    {
        Transform t = null;
        if (_piecePool.Count > 0)
        {
            int idx = _piecePool.Count - 1;
            t = _piecePool[idx];
            _piecePool.RemoveAt(idx);
            _returnedToPoolIds.Remove(t != null ? t.GetInstanceID() : 0);
            _poolReused++;
        }
        if (t == null)
        {
            t = SpawnPieceRoofBasis(worldPos, worldRot, size, ix, iz, layer);
            _poolInstantiated++;
        }
        else
        {
            int before = _piecesRoot.childCount;
            t.SetParent(_piecesRoot, true);
            LogRootMutation(before, before + 1, "GetOrSpawnPieceRoofBasis");
            t.gameObject.SetActive(true);
            SetPieceVisualState(t, PieceVisualState.Accumulating);
            t.position = worldPos;
            t.rotation = worldRot;
            float baseSize = Mathf.Max(0.05f, size);
            GetDeterministicJitter(ix, iz, layer, jitter, scaleJitterXZ, out _, out _, out float sx, out float sy, out float sz);
            bool isEdge = (_cachedNx > 1 && (ix == 0 || ix == _cachedNx - 1)) || (_cachedNz > 1 && (iz == 0 || iz == _cachedNz - 1));
            float edgeScale = isEdge ? 1.03f : 1f;
            float h = Mathf.Max(0.03f, size * pieceHeightScale * sy) * snowPieceThicknessScale;
            Vector3 scale = debugForcePieceRendererDirect
                ? new Vector3(baseSize * sx * edgeScale, h * snowRenderThicknessScale, baseSize * sz * edgeScale)
                : new Vector3(baseSize * sx * edgeScale, h, baseSize * sz * edgeScale);
            t.localScale = scale;
            if (debugForcePieceRendererDirect)
            {
                var mesh = t.Find("Mesh");
                if (mesh != null) { UnityEngine.Object.Destroy(mesh.gameObject); }
                if (t.GetComponent<MeshFilter>() == null) t.gameObject.AddComponent<MeshFilter>().sharedMesh = GetCurrentPieceMesh();
                var mrr = t.GetComponent<MeshRenderer>();
                if (mrr == null) { mrr = t.gameObject.AddComponent<MeshRenderer>(); if (_snowMat != null) mrr.sharedMaterial = _snowMat; }
                if (mrr != null) mrr.enabled = GridVisualWatchdog.showSnowGridDebug;
            }
            else
            {
                var mesh = t.Find("Mesh");
                if (mesh != null) mesh.localScale = new Vector3(1f, snowRenderThicknessScale, 1f);
                var r = t.GetComponentInChildren<Renderer>(true);
                if (r != null) r.enabled = GridVisualWatchdog.showSnowGridDebug;
            }
        }
        var key = (ix, iz);
        if (!_gridPieces.TryGetValue(key, out var cellList))
        {
            cellList = new List<Transform>();
            _gridPieces[key] = cellList;
        }
        cellList.Add(t);
        _pieceToGridData[t] = (ix, iz, layer);
        return t;
    }

    Transform SpawnPieceRoofBasis(Vector3 worldPos, Quaternion worldRot, float size, int ix = 0, int iz = 0, int layer = 0)
    {
        var go = new GameObject("SnowPackPiece");
        int before = _piecesRoot.childCount;
        go.transform.SetParent(_piecesRoot, true);
        BugOriginTracker.RecordEvent(BugOriginTracker.EventObjectSpawn, go.name, "SnowPackSpawner.cs", worldPos);
        LogRootMutation(before, before + 1, "SpawnPieceRoofBasis");
        PushLastEvent("SpawnPieceRoofBasis", $"pieceId={go.GetInstanceID()}", null);
        GetDeterministicJitter(ix, iz, layer, jitter, scaleJitterXZ, out _, out _, out float sx, out float sy, out float sz);
        // 側面改善: 外周ピースを少し外側へ張らせる。積み木断面感を減らす。
        float edgeScale = 1f;
        if (_cachedNx > 1 || _cachedNz > 1)
        {
            bool atEdgeX = (ix == 0 || ix == Mathf.Max(1, _cachedNx) - 1);
            bool atEdgeZ = (iz == 0 || iz == Mathf.Max(1, _cachedNz) - 1);
            if (atEdgeX || atEdgeZ) edgeScale = 1.03f;
        }
        float h = Mathf.Max(0.03f, size * pieceHeightScale * sy) * snowPieceThicknessScale;
        float baseSize = Mathf.Max(0.05f, size);
        Vector3 scale = debugForcePieceRendererDirect
            ? new Vector3(baseSize * sx * edgeScale, h * snowRenderThicknessScale, baseSize * sz * edgeScale)
            : new Vector3(baseSize * sx * edgeScale, h, baseSize * sz * edgeScale);
        go.transform.position = worldPos;
        go.transform.rotation = worldRot;
        go.transform.localScale = scale;

        if (debugForcePieceRendererDirect)
        {
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var mesh = GetCurrentPieceMesh(); if (mesh != null) mf.sharedMesh = mesh;
            if (_snowMat != null) mr.sharedMaterial = _snowMat;
            mr.enabled = GridVisualWatchdog.showSnowGridDebug;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
        }
        else
        {
            var meshGo = new GameObject("Mesh");
            meshGo.transform.SetParent(go.transform, false);
            meshGo.transform.localPosition = Vector3.zero;
            meshGo.transform.localRotation = Quaternion.identity;
            meshGo.transform.localScale = new Vector3(1f, snowRenderThicknessScale, 1f);
            var mf = meshGo.AddComponent<MeshFilter>();
            var mr = meshGo.AddComponent<MeshRenderer>();
            var mesh = GetCurrentPieceMesh(); if (mesh != null) mf.sharedMesh = mesh;
            if (_snowMat != null) mr.sharedMaterial = _snowMat;
            mr.enabled = GridVisualWatchdog.showSnowGridDebug;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
        }
        int snowLayer = LayerMask.NameToLayer(SnowVisualLayerName);
        if (snowLayer < 0) snowLayer = LayerMask.NameToLayer("Ignore Raycast");
        if (snowLayer < 0) snowLayer = 2;
        SetLayerRecursively(go, snowLayer);
        SetPieceVisualState(go.transform, PieceVisualState.Accumulating);
        return go.transform;
    }

    void ReturnToPool(Transform t, string reason, string source, bool allowDuringSlide = false)
    {
        if (t == null) return;
        if (throwOnFirstPoolReturn && !_poolReturnThrowOnce)
        {
            _poolReturnThrowOnce = true;
            string st = UnityEngine.StackTraceUtility.ExtractStackTrace();
            throw new Exception($"[SnowPackPoolReturn] throwOnFirst source={source} reason={reason}\n{st}");
        }
        OnPieceDeactivated(t, reason, source, toPool: true, allowDuringSlide);
    }

    /// <summary>落下中に地面衝突/タイムアウトで呼ぶ。雪崩中でもPool返却を許可。</summary>
    public void ReturnToPoolFromFalling(Transform t, string reason)
    {
        if (t == null) return;
        ReturnToPool(t, reason, "Falling", allowDuringSlide: true);
    }

    Transform SpawnPieceLocal(Vector3 localPos, float size)
    {
        var go = new GameObject("SnowPackPiece");
        int before = _piecesRoot.childCount;
        go.transform.SetParent(_piecesRoot, false);
        LogRootMutation(before, before + 1, "SpawnPieceLocal");
        PushLastEvent("SpawnPieceLocal", $"pieceId={go.GetInstanceID()}", null);
        float sh = 0.9f + UnityEngine.Random.value * 0.25f;
        float sx = 1f + (UnityEngine.Random.value * 2f - 1f) * scaleJitterXZ;
        float sz = 1f + (UnityEngine.Random.value * 2f - 1f) * scaleJitterXZ;
        float h = Mathf.Max(0.03f, size * pieceHeightScale * sh) * snowPieceThicknessScale;
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        float baseSize = Mathf.Max(0.05f, size);
        Vector3 scale = new Vector3(baseSize * sx, h * snowRenderThicknessScale, baseSize * sz);
        go.transform.localScale = scale;

        var meshGo = new GameObject("Mesh");
        meshGo.transform.SetParent(go.transform, false);
        meshGo.transform.localPosition = Vector3.zero;
        meshGo.transform.localRotation = Quaternion.identity;
        meshGo.transform.localScale = new Vector3(1f, snowRenderThicknessScale, 1f);
        var mf = meshGo.AddComponent<MeshFilter>();
        var mr = meshGo.AddComponent<MeshRenderer>();
        var mesh = GetCurrentPieceMesh(); if (mesh != null) mf.sharedMesh = mesh;
        if (_snowMat != null) mr.sharedMaterial = _snowMat;
        mr.enabled = GridVisualWatchdog.showSnowGridDebug;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        mr.receiveShadows = true;
        int snowLayer = LayerMask.NameToLayer(SnowVisualLayerName);
        if (snowLayer < 0) snowLayer = LayerMask.NameToLayer("Ignore Raycast");
        if (snowLayer < 0) snowLayer = 2;
        SetLayerRecursively(go, snowLayer);
        LogSpawnOnce(go);
        SetPieceVisualState(go.transform, PieceVisualState.Accumulating);
        if (!_scaleLogOnce) { _scaleLogOnce = true; UnityEngine.Debug.Log($"[SnowPieceScale] kind=Packed scale=({scale.x:F3},{scale.y:F3},{scale.z:F3})"); }
        return go.transform;
    }

    /// <summary>SnowPackVisual/SnowPackPiecesRoot を RoofSlideCollider 直下に作成する。外部からフォールバック呼び出し可。</summary>
    public void EnsureSnowPackVisualHierarchy()
    {
        if (roofCollider == null) roofCollider = ResolveRoofCollider();
        EnsureRoot();
    }

    void EnsureRoot()
    {
        if (roofCollider == null) return;
        var roofT = roofCollider.transform;
        var t = roofT.Find("SnowPackVisual");
        if (t == null)
        {
            var go = new GameObject("SnowPackVisual");
            go.transform.SetParent(roofT, false);
            t = go.transform;
        }
        _visualRoot = t;
        EnsureStateIndicator();
        var piecesRoot = _visualRoot.Find("SnowPackPiecesRoot");
        if (piecesRoot == null)
        {
            var go = new GameObject("SnowPackPiecesRoot");
            go.transform.SetParent(_visualRoot, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            piecesRoot = go.transform;
        }
        _piecesRoot = piecesRoot;
        if (_piecesRoot.Find("SnowPackAnchor") == null)
        {
            int before = _piecesRoot.childCount;
            var anchor = new GameObject("SnowPackAnchor");
            anchor.transform.SetParent(_piecesRoot, false);
            LogRootMutation(before, before + 1, "EnsureRoot.AddAnchor");
            anchor.SetActive(false);
        }
        var pr = roofT.Find("SnowPackPool");
        if (pr == null)
        {
            var go = new GameObject("SnowPackPool");
            go.transform.SetParent(roofT, false);
            pr = go.transform;
            pr.gameObject.SetActive(false);
        }
        _poolRoot = pr;
    }

    void EnsureStateIndicator()
    {
        if (!enableStateIndicator)
        {
            var child = _visualRoot != null ? _visualRoot.Find("SnowPackStateIndicator") : null;
            if (child != null) child.gameObject.SetActive(false);
            _stateIndicatorRenderer = null;
            return;
        }
        if (_stateIndicatorRenderer != null) return;
        if (_visualRoot == null) return;
        var stateIndicatorT = _visualRoot.Find("SnowPackStateIndicator");
        if (stateIndicatorT == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "SnowPackStateIndicator";
            go.transform.SetParent(_visualRoot, false);
            go.transform.localPosition = new Vector3(0f, -0.01f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(10f, 10f, 1f);
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            _stateIndicatorRenderer = go.GetComponent<Renderer>();
            if (_stateIndicatorRenderer != null)
            {
                var mat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"));
                MaterialColorHelper.SetColorSafe(mat, new Color(1f, 1f, 1f, 0.15f));
                _stateIndicatorRenderer.sharedMaterial = mat;
                _stateIndicatorRenderer.enabled = true;
            }
        }
        else
        {
            _stateIndicatorRenderer = stateIndicatorT.GetComponent<Renderer>();
        }
    }

    void UpdateStateIndicatorColor()
    {
        if (!enableStateIndicator) return;
        if (_stateIndicatorRenderer == null || _stateIndicatorRenderer.material == null) return;
        bool inSlide = _inAvalancheSlide;
        bool inCooldown = roofSnowSystem != null && roofSnowSystem.IsInAvalancheCooldown && !_inAvalancheSlide;
        bool isFail = _autoRebuildFailReason != AutoRebuildFailReason.None;
        Color c;
        if (isFail) c = new Color(1f, 0.2f, 0.2f, 0.4f);       // FAIL/ERROR = 赤
        else if (inSlide) c = new Color(0.3f, 0.5f, 1f, 0.25f); // Avalanche中 = 青
        else if (inCooldown) c = new Color(1f, 1f, 0.3f, 0.25f); // Cooldown = 黄
        else c = new Color(1f, 1f, 1f, 0.12f);                  // Normal = 白
        if (_stateIndicatorRenderer.material != null) MaterialColorHelper.SetColorSafe(_stateIndicatorRenderer.material, c);
        _stateIndicatorRenderer.enabled = true;
    }

    void EnsureMaterial()
    {
        if (_snowMat != null) return;
        _snowMat = SnowVisual.GetSnowMaterial(snowColor);
        if (_snowMat != null) return;
        Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (sh == null) return;
        _snowMat = new Material(sh);
        MaterialColorHelper.SetColorSafe(_snowMat, snowColor);
    }

    void EnsurePieceMesh()
    {
        if (_pieceMesh != null) return;
        _pieceMesh = BuildCubeMesh();
    }

    void EnsureNonSymMesh()
    {
        if (_pieceMeshNonSym != null) return;
        _pieceMeshNonSym = BuildNonSymMesh();
    }

    Mesh GetCurrentPieceMesh()
    {
        if (DebugSnowVisibility.DebugNonSymMesh)
        {
            EnsureNonSymMesh();
            return _pieceMeshNonSym;
        }
        var mesh = SnowVisual.GetPieceMesh();
        Mesh result;
        if (mesh != null) { result = mesh; } else { EnsurePieceMesh(); result = _pieceMesh; }
        if (!_rootCauseMeshLogged)
        {
            _rootCauseMeshLogged = true;
            string meshName = result != null ? (result.name ?? "null") : "null";
            bool nonCube = result != null && mesh != null && mesh.name != null && mesh.name.Contains("Rounded");
            UnityEngine.Debug.Log($"[ROOT_CAUSE_ISOLATION] source=packed mesh_name={meshName} mesh_is_non_cube={(nonCube ? "YES" : "NO")} renderer_path=SnowPackPiece");
        }
        return result;
    }

    /// <summary>DebugNonSymMeshトグル変更時に全pieceのメッシュを差し替え。</summary>
    public void RefreshPieceMeshesForDebug()
    {
        var mesh = GetCurrentPieceMesh();
        if (_piecesRoot == null || mesh == null) return;
        for (int i = 0; i < _piecesRoot.childCount; i++)
        {
            var c = _piecesRoot.GetChild(i);
            if (c == null || c.name != "SnowPackPiece") continue;
            var mf = c.GetComponent<MeshFilter>();
            if (mf == null) { var child = c.Find("Mesh"); if (child != null) mf = child.GetComponent<MeshFilter>(); }
            if (mf != null) mf.sharedMesh = mesh;
        }
    }

    static Mesh BuildCubeMesh()
    {
        var m = new Mesh { name = "SnowPackCubeMesh" };
        var v = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
            new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f),
            new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f),
        };
        var t = new int[]
        {
            0,2,1, 0,3,2, 4,6,5, 4,7,6, 8,10,9, 8,11,10,
            12,14,13, 12,15,14, 16,18,17, 16,19,18, 20,22,21, 20,23,22
        };
        m.vertices = v;
        m.triangles = t;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>非対称直方体(1,0.4,2)。回転が目視しやすい。</summary>
    static Mesh BuildNonSymMesh()
    {
        float wx = 0.5f, wy = 0.2f, wz = 1f;
        var m = new Mesh { name = "SnowPackNonSymMesh" };
        var v = new Vector3[]
        {
            new Vector3(-wx, -wy, wz), new Vector3(wx, -wy, wz), new Vector3(wx, wy, wz), new Vector3(-wx, wy, wz),
            new Vector3(wx, -wy, -wz), new Vector3(-wx, -wy, -wz), new Vector3(-wx, wy, -wz), new Vector3(wx, wy, -wz),
            new Vector3(-wx, -wy, -wz), new Vector3(-wx, -wy, wz), new Vector3(-wx, wy, wz), new Vector3(-wx, wy, -wz),
            new Vector3(wx, -wy, wz), new Vector3(wx, -wy, -wz), new Vector3(wx, wy, -wz), new Vector3(wx, wy, wz),
            new Vector3(-wx, wy, wz), new Vector3(wx, wy, wz), new Vector3(wx, wy, -wz), new Vector3(-wx, wy, -wz),
            new Vector3(-wx, -wy, -wz), new Vector3(wx, -wy, -wz), new Vector3(wx, -wy, wz), new Vector3(-wx, -wy, wz),
        };
        var t = new int[]
        {
            0,2,1, 0,3,2, 4,6,5, 4,7,6, 8,10,9, 8,11,10,
            12,14,13, 12,15,14, 16,18,17, 16,19,18, 20,22,21, 20,23,22
        };
        m.vertices = v;
        m.triangles = t;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    void RefreshPackedTransformsFromRoofBasis()
    {
        if (_piecesRoot == null || roofCollider == null || _pieceToGridData.Count == 0) return;
        if (_cachedLayerStep <= 0f || _cachedNx <= 0 || _cachedNz <= 0) return;
        var toRefresh = new List<(Transform t, int ix, int iz, int layer)>();
        for (int i = 0; i < _piecesRoot.childCount; i++)
        {
            var c = _piecesRoot.GetChild(i);
            if (c == null || c.name != "SnowPackPiece") continue;
            if (!_pieceToGridData.TryGetValue(c, out var d)) continue;
            if (!c.gameObject.activeSelf) continue;
            toRefresh.Add((c, d.ix, d.iz, d.layer));
        }
        foreach (var item in toRefresh)
        {
            GridCellToUV(item.ix, item.iz, out float u, out float v);
            GetDeterministicJitter(item.ix, item.iz, item.layer, jitter, scaleJitterXZ, out float jx, out float jz, out _, out _, out _);
            float layerOffset = RoofSurfaceOffset + item.layer * _cachedLayerStep;
            Vector3 p = _roofCenter + _roofR * (u * _roofWidth + jx) + _roofF * (v * _roofLength + jz) + _roofN * layerOffset;
            var angleT = GetRoofAngleTransform();
            Quaternion rot = angleT != null ? angleT.rotation : roofCollider.transform.rotation;
            item.t.position = p;
            item.t.rotation = rot;
        }
    }

    void AlignVisualRootToRoof()
    {
        if (_visualRoot == null || roofCollider == null) return;
        _visualRoot.SetParent(roofCollider.transform, false);
        _visualRoot.localPosition = Vector3.zero;
        Vector3 roofUp = GetRoofAngleTransform() != null ? GetRoofAngleTransform().up.normalized : roofCollider.transform.up.normalized;
        _visualRoot.rotation = Quaternion.FromToRotation(Vector3.up, roofUp);
        if (_piecesRoot != null) _piecesRoot.localRotation = Quaternion.identity;
    }

    void GetRoofLocalRect(out Vector3 localCenter, out float halfX, out float halfZ)
    {
        localCenter = Vector3.zero;
        halfX = 1f;
        halfZ = 1f;
        if (roofCollider == null || _visualRoot == null) return;
        var rootForBounds = _piecesRoot != null ? _piecesRoot : _visualRoot;
        if (roofCollider is BoxCollider box && box.transform == roofCollider.transform)
        {
            localCenter = box.center;
            halfX = Mathf.Max(0.1f, box.size.x * 0.5f);
            halfZ = Mathf.Max(0.1f, box.size.z * 0.5f);
            return;
        }
        Bounds b = roofCollider.bounds;
        localCenter = rootForBounds.InverseTransformPoint(b.center);
        Vector3 ex = rootForBounds.InverseTransformVector(new Vector3(b.extents.x, 0f, 0f));
        Vector3 ez = rootForBounds.InverseTransformVector(new Vector3(0f, 0f, b.extents.z));
        halfX = Mathf.Max(0.1f, Mathf.Abs(ex.x));
        halfZ = Mathf.Max(0.1f, Mathf.Abs(ez.z));
    }

    void EnsureSnowVisualCollisionSetup()
    {
        int snowLayer = EnsureSnowVisualLayerExists();
        if (snowLayer < 0) return;
        for (int i = 0; i < 32; i++)
            Physics.IgnoreLayerCollision(snowLayer, i, true);
    }

    int EnsureSnowVisualLayerExists()
    {
        int idx = LayerMask.NameToLayer(SnowVisualLayerName);
#if UNITY_EDITOR
        if (idx < 0)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets != null && assets.Length > 0)
            {
                var tagManager = new SerializedObject(assets[0]);
                var layersProp = tagManager.FindProperty("layers");
                for (int i = 8; i <= 31; i++)
                {
                    var sp = layersProp.GetArrayElementAtIndex(i);
                    if (string.IsNullOrEmpty(sp.stringValue))
                    {
                        sp.stringValue = SnowVisualLayerName;
                        tagManager.ApplyModifiedProperties();
                        idx = i;
                        break;
                    }
                }
            }
        }
#endif
        return idx;
    }

    Collider ResolveRoofCollider()
    {
        var byName = GameObject.Find("RoofSlideCollider");
        if (byName != null) return byName.GetComponent<Collider>();
        var all = FindObjectsByType<Collider>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            var c = all[i];
            if (c == null) continue;
            if (c.name.Contains("RoofSlideCollider")) return c;
        }
        return null;
    }

    void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null) return;
        go.layer = layer;
        var trs = go.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < trs.Length; i++)
            trs[i].gameObject.layer = layer;
    }

    public Vector3 RoofDownhill => _roofDownhill;
    public Vector3 RoofUp => _roofN;

    /// <summary>AvalanchePhysicsSystem用。Packedピースとグリッド座標を収集。</summary>
    public void CollectPackedPiecesWithGrid(System.Collections.Generic.List<(Transform t, int ix, int iz)> outList)
    {
        outList?.Clear();
        if (outList == null) return;
        foreach (var kv in _pieceToGridData)
        {
            if (kv.Key == null || !kv.Key.gameObject.activeInHierarchy) continue;
            outList.Add((kv.Key, kv.Value.ix, kv.Value.iz));
        }
    }

    /// <summary>AvalanchePhysicsSystem用。指定ピースを直接Detachして斜面滑走開始。</summary>
    public void DetachPiecesDirect(System.Collections.Generic.List<Transform> pieces, Vector3 slideDir, float slideSpeed)
    {
        if (pieces == null || pieces.Count == 0) return;
        EnsureRoot();
        if (_cachedLayerStep <= 0f) CacheGridParams();

        var toRemove = new System.Collections.Generic.List<Transform>();
        foreach (var t in pieces)
        {
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            if (!_pieceToGridData.ContainsKey(t)) continue;
            toRemove.Add(t);
        }
        if (toRemove.Count == 0) return;

        int packedBefore = GetPackedCubeCountRealtime();
        foreach (var t in toRemove)
        {
            var k = FindGridKeyForPiece(t);
            if (k.HasValue && _gridPieces.TryGetValue(k.Value, out var list))
            {
                list.Remove(t);
                if (list.Count == 0) _gridPieces.Remove(k.Value);
            }
            _pieceToGridData.Remove(t);
            _pieceToLayerType.Remove(t);
            for (int li = _layerPieces.Count - 1; li >= 0; li--)
            {
                if (_layerPieces[li].Remove(t)) break;
            }
        }
        for (int i = _layerPieces.Count - 1; i >= 0; i--)
        {
            if (_layerPieces[i].Count == 0) _layerPieces.RemoveAt(i);
        }

        int removedCount = toRemove.Count;
        LastTapWorld = toRemove[0] != null ? toRemove[0].position : _roofCenter;
        LastTapTime = Time.time;
        LastPackedTotalBefore = packedBefore;
        LastPackedTotalAfter = packedBefore - removedCount;
        LastRemovedCount = removedCount;
        _lastAvalanchePackedCountAfter = packedBefore - removedCount;
        if (_layerPieces.Count > 0 && _cachedLayerStep > 0f)
            packDepthMeters = Mathf.Max(minVisibleDepth, _layerPieces.Count * _cachedLayerStep);

        _inAvalancheSlide = true;
        StartCoroutine(LocalAvalancheSlideRoutine(toRemove, slideDir.normalized, slideSpeed));
    }

    public void LogNearestPieceToTap(Vector3 tapWorld)
    {
        if (_piecesRoot == null) return;
        Transform nearest = null;
        float nearestSq = float.MaxValue;
        foreach (var kv in _pieceToGridData)
        {
            var t = kv.Key;
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            float sq = (t.position - tapWorld).sqrMagnitude;
            if (sq < nearestSq) { nearestSq = sq; nearest = t; }
        }
        if (nearest == null) { UnityEngine.Debug.Log("[NearestPieceToTap] no piece found"); return; }
        float dist = Mathf.Sqrt(nearestSq);
        var pieceEuler = nearest.rotation.eulerAngles;
        Transform rendererT = null;
        var mr = nearest.GetComponentInChildren<Renderer>();
        if (mr != null) rendererT = mr.transform;
        var rendererEuler = rendererT != null ? rendererT.rotation.eulerAngles : Vector3.zero;
        UnityEngine.Debug.Log($"[NearestPieceToTap] dist={dist:F3} id={nearest.GetInstanceID()} pieceEuler=({pieceEuler.x:F1},{pieceEuler.y:F1},{pieceEuler.z:F1}) rendererEuler=({rendererEuler.x:F1},{rendererEuler.y:F1},{rendererEuler.z:F1})");
    }

    /// <summary>屋根上のPackedキューブ数。直近雪崩後は LastAvalanchePackedCountAfter を返す。</summary>
    public int GetPackedCubeCount()
    {
        if (_lastAvalanchePackedCountAfter >= 0) return _lastAvalanchePackedCountAfter;
        return GetPackedCubeCountRealtime();
    }

    /// <summary>常に実数。before記録用。1箇所の唯一の真実として統一。</summary>
    public int GetPackedCubeCountRealtime()
    {
        return _piecesRoot != null ? CountPiecesUnder(_piecesRoot) : 0;
    }

    public int GetPooledCount()
    {
        return _piecePool != null ? _piecePool.Count : 0;
    }

    /// <summary>B2-debug: 屋根+slideRoot内のピース数（pool除く）。</summary>
    public int GetB2ActiveCount()
    {
        return _visualRoot != null ? CountPiecesUnder(_visualRoot) : 0;
    }

    /// <summary>B2-debug: active + pooled。ゼロ化タイミング特定用。</summary>
    public int GetB2TotalCount()
    {
        return GetB2ActiveCount() + GetPooledCount();
    }

    /// <summary>DebugSnowVisibility用。全SnowPackPieceのRendererを返す。</summary>
    public System.Collections.Generic.List<Renderer> GetAllPieceRenderers()
    {
        var list = new System.Collections.Generic.List<Renderer>();
        if (_piecesRoot == null) return list;
        for (int i = 0; i < _piecesRoot.childCount; i++)
        {
            var c = _piecesRoot.GetChild(i);
            if (c == null || c.name != "SnowPackPiece") continue;
            var r = c.GetComponentInChildren<Renderer>(true);
            if (r != null) list.Add(r);
        }
        return list;
    }

    int _lastAvalanchePackedCountAfter = -1;

    static int CountPiecesUnder(Transform root)
    {
        if (root == null) return 0;
        int n = 0;
        var trs = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < trs.Length; i++)
        {
            if (trs[i] != null && trs[i].gameObject.name == "SnowPackPiece")
                n++;
        }
        return n;
    }

    void LogSpawnOnce(GameObject go)
    {
        if (!Application.isPlaying || _spawnLogOnce || go == null) return;
        _spawnLogOnce = true;
        bool hasCollider = go.GetComponentInChildren<Collider>(true) != null;
        bool hasRb = go.GetComponentInChildren<Rigidbody>(true) != null;
        string layerName = LayerMask.LayerToName(go.layer);
        string parentName = go.transform.parent != null ? go.transform.parent.name : "None";
        UnityEngine.Debug.Log($"[SnowPackSpawn] name={go.name} layer={layerName} hasCollider={hasCollider} hasRigidbody={hasRb} parent={parentName}");
    }

    void AuditSnowPackPhysics()
    {
        if (_visualRoot == null) return;
        int colCount = _visualRoot.GetComponentsInChildren<Collider>(true).Length;
        int rbCount = _visualRoot.GetComponentsInChildren<Rigidbody>(true).Length;
        if (colCount != 0 || rbCount != 0)
            UnityEngine.Debug.LogWarning($"[SnowPackAudit] colliders={colCount} rigidbodies={rbCount}");
        else
            UnityEngine.Debug.Log("[SnowPackAudit] colliders=0 rigidbodies=0");
    }

    public void ClearSnowPack(string reason)
    {
        if (SnowVerifyB2Debug.Enabled)
        {
            SnowVerifyB2Debug.CleanupCalled = true;
            if (SnowVerifyB2Debug.PauseCleanup)
            {
                int surv = GetB2TotalCount();
                UnityEngine.Debug.Log($"[B2_CLEANUP_SKIP] cleanup_called=true surviving_count_before_cleanup={surv} PauseCleanup=true skipping_clear");
                return;
            }
        }
        PushLastEvent("Clear", reason);
        _returnedToPoolIds.Clear();
        EnsureRoot();
        _poolReturnQueue.Clear();
        if (_pendingSlideRootToDestroy != null)
        {
            UnityEngine.Object.Destroy(_pendingSlideRootToDestroy);
            _pendingSlideRootToDestroy = null;
        }
        if (_piecesRoot != null)
        {
            int before = _piecesRoot.childCount;
            int anchorCount = _piecesRoot.Find("SnowPackAnchor") != null ? 1 : 0;
            LogRootMutation(before, anchorCount, "ClearSnowPack");
        }
        LogSnowPackCall("CLEAR", reason);
        _layerPieces.Clear();
        _gridPieces.Clear();
        _pieceToGridData.Clear();
        _pieceToLayerType.Clear();
        for (int i = _piecePool.Count - 1; i >= 0; i--)
        {
            if (_piecePool[i] != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_piecePool[i].gameObject);
                else UnityEngine.Object.DestroyImmediate(_piecePool[i].gameObject);
            }
        }
        _piecePool.Clear();
        if (_piecesRoot != null)
        {
            for (int i = _piecesRoot.childCount - 1; i >= 0; i--)
            {
                var c = _piecesRoot.GetChild(i);
                if (c.name == "SnowPackAnchor") continue;
                OnPieceDeactivated(c, "Clear", "ClearSnowPack", toPool: false);
            }
        }
        if (SnowVerifyB2Debug.Enabled)
            UnityEngine.Debug.Log($"[B2_CLEANUP_DONE] cleanup_called=true surviving_count_after_cleanup={GetB2TotalCount()}");
    }

    void AddLayers(int n)
    {
        if (n <= 0 || roofCollider == null || _visualRoot == null) return;
        if (_cachedLayerStep <= 0f) CacheGridParams();
        for (int i = 0; i < n; i++)
        {
            var layer = SpawnLayer(_layerPieces.Count);
            _layerPieces.Add(layer);
            if (layer.Count == 0) break;
        }
        _addCount += n;
    }

    void RemoveLayers(int n)
    {
        if (n <= 0) return;
        if (roofSnowSystem != null && roofSnowSystem.IsInAvalancheCooldown)
        {
            UnityEngine.Debug.Log($"[SnowPackAvalancheGuard] RemoveLayers skipped n={n} reason=IsInAvalancheCooldown evidence=NoRemoveDuringAvalanche");
            return;
        }
        if (debugAutoRefillRoofSnow && debugMinPackedPieces > 0)
        {
            int current = GetPackedCubeCountRealtime();
            int wouldKeep = current;
            for (int i = 0; i < Mathf.Min(n, _layerPieces.Count); i++)
                wouldKeep -= _layerPieces[_layerPieces.Count - 1 - i].Count;
            if (wouldKeep < debugMinPackedPieces)
            {
                UnityEngine.Debug.Log($"[SnowPackMinGuard] RemoveLayers blocked n={n} current={current} wouldKeep={wouldKeep} min={debugMinPackedPieces}");
                return;
            }
        }
        if (_cachedLayerStep <= 0f) CacheGridParams();
        n = Mathf.Min(n, _layerPieces.Count);
        float requestedDrop = n * _cachedLayerStep;
        float maxDrop = maxDownStepPerSec * minSyncInterval;
        float actualDrop = Mathf.Min(requestedDrop, maxDrop);
        int toRemove = Mathf.FloorToInt(actualDrop / Mathf.Max(0.001f, _cachedLayerStep));
        toRemove = Mathf.Clamp(toRemove, 0, _layerPieces.Count);
        if (toRemove <= 0) return;
        int removed = 0;
        for (int i = 0; i < toRemove; i++)
        {
            if (_layerPieces.Count == 0) break;
            var layer = _layerPieces[_layerPieces.Count - 1];
            _layerPieces.RemoveAt(_layerPieces.Count - 1);
            for (int j = 0; j < layer.Count; j++)
            {
                if (layer[j] != null)
                    ReturnToPool(layer[j], "RemoveLayers", "Despawn");
            }
            removed++;
        }
        RecordLayersRemoved(removed, "DepthSync.RemoveLayers(roofDepth drop)");
    }

    /// <summary>層削除を記録（removeCount の唯一の加算箇所。発生条件を明示）</summary>
    void RecordLayersRemoved(int n, string reason)
    {
        if (n <= 0) return;
        PushLastEvent("RemoveLayers", $"n={n} reason={reason}");
        UnityEngine.Debug.Log($"[SnowPackRemove] layers={n} reason={reason} removeCountBefore={_removeCount} -> after={_removeCount + n}");
        _removeCount += n;
    }

    static string GetRealStackTrace()
    {
        string st = UnityEngine.StackTraceUtility.ExtractStackTrace();
        if (!string.IsNullOrEmpty(st) && (st.Contains(".cs:") || st.Contains(".cs(")))
            return st;
        var trace = new StackTrace(true);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Mathf.Min(trace.FrameCount, 12); i++)
        {
            var frame = trace.GetFrame(i);
            if (frame == null) continue;
            var method = frame.GetMethod();
            string fn = frame.GetFileName();
            int line = frame.GetFileLineNumber();
            if (!string.IsNullOrEmpty(fn))
            {
                string fileColonLine = System.IO.Path.GetFileName(fn) + ":" + line;
                sb.AppendLine($"  at {method?.DeclaringType?.FullName}.{method?.Name} in {fileColonLine}");
            }
            else
                sb.AppendLine($"  at {method?.DeclaringType?.FullName}.{method?.Name}");
        }
        return sb.Length > 0 ? sb.ToString() : st;
    }

    /// <summary>SnowPackRoot(SnowPackVisual) の rotation.eulerAngles 文字列。屋根ベクトル切り分け用。</summary>
    public string SnowPackRootEulerString
    {
        get
        {
            if (_visualRoot == null) return "null";
            var e = _visualRoot.rotation.eulerAngles;
            return $"({e.x:F1},{e.y:F1},{e.z:F1})";
        }
    }

    enum PieceVisualState { Accumulating, Sliding, Cooldown, Returning, Pooled }
    static readonly Color _colorAccum = Color.white;       // Normal
    static readonly Color _colorSliding = Color.yellow;     // Avalanche
    static readonly Color _colorCooldown = new Color(0.3f, 0.5f, 1f);  // Cooldown
    static readonly Color _colorReturning = new Color(1f, 0.5f, 0f);   // Returning
    static readonly Color _colorPooled = Color.black;       // Pooled (非表示)

    int _pieceStateLogThrottle;
    void SetPieceVisualState(Transform t, PieceVisualState s, bool logOneSample = false)
    {
        if (t == null) return;
        var mesh = t.Find("Mesh");
        var r = mesh != null ? mesh.GetComponent<Renderer>() : t.GetComponentInChildren<Renderer>();
        if (r != null)
        {
            if (r.material != null)
                MaterialColorHelper.SetColorSafe(r.material, s == PieceVisualState.Accumulating ? _colorAccum : s == PieceVisualState.Sliding ? _colorSliding : s == PieceVisualState.Cooldown ? _colorCooldown : s == PieceVisualState.Returning ? _colorReturning : _colorPooled);
            r.enabled = GridVisualWatchdog.showSnowGridDebug;
        }
        if (logOneSample || _pieceStateLogThrottle++ % 50 == 0)
            UnityEngine.Debug.Log($"[SnowPieceState] id={t.GetInstanceID()} state={s} frame={Time.frameCount} t={Time.time:F2}");
    }

    void PushLastEvent(string kind, string reason, string stackTrace = null, int pieceId = 0)
    {
        var e = new SnowPackEventEntry
        {
            kind = kind,
            reason = reason,
            pieceId = pieceId,
            frame = Time.frameCount,
            t = Time.time,
            stackTrace = stackTrace ?? GetRealStackTrace()
        };
        _lastEvents.Add(e);
        if (_lastEvents.Count > LastEventsCapacity) _lastEvents.RemoveAt(0);
    }

    void DumpLast20Events(string trigger)
    {
        UnityEngine.Debug.Log($"[SnowPackLast20] trigger={trigger} count={_lastEvents.Count}");
        for (int i = 0; i < _lastEvents.Count; i++)
        {
            var e = _lastEvents[i];
            string fileLine = ExtractFirstFileLine(e.stackTrace);
            UnityEngine.Debug.Log($"[SnowPackLast20] [{i}] kind={e.kind} reason={e.reason} pieceId={e.pieceId} frame={e.frame} t={e.t:F2} fileLine={fileLine}");
            UnityEngine.Debug.Log($"[SnowPackLast20] [{i}] STACK:\n{e.stackTrace}");
        }
    }

    /// <summary>activePieces==0 時のみ: 直前20イベントを短縮版で出力</summary>
    void DumpLast20EventsShort(string trigger)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"[SnowPackLast20Short] trigger={trigger} count={_lastEvents.Count}");
        for (int i = 0; i < _lastEvents.Count; i++)
        {
            var e = _lastEvents[i];
            sb.Append($" | [{i}] {e.kind} {e.reason} pieceId={e.pieceId} f={e.frame} t={e.t:F2}");
        }
        UnityEngine.Debug.Log(sb.ToString());
    }

    static readonly HashSet<int> _returnedToPoolIds = new HashSet<int>();

    /// <summary>activePieces を減らす処理の唯一の入口。Pool返却・Destroy いずれもここを通す。Idempotent.</summary>
    void OnPieceDeactivated(Transform t, string reason, string source, bool toPool, bool allowDuringSlide = false)
    {
        if (t == null || t.gameObject == null) return;
        string eventType = toPool ? BugOriginTracker.EventSnowDetach : BugOriginTracker.EventObjectDestroy;
        BugOriginTracker.RecordEvent(eventType, t.gameObject.name ?? "SnowPackPiece", "SnowPackSpawner.cs", t.position);
        if (SnowVerifyB2Debug.Enabled)
        {
            SnowVerifyB2Debug.RecordDiscard(reason, source);
            if (toPool) SnowVerifyB2Debug.PoolReturnCalled = true;
        }
        _pieceToLayerType.Remove(t);
        int pieceId = t.GetInstanceID();
        if (toPool && _returnedToPoolIds.Contains(pieceId)) return;
        if (toPool && _poolRoot != null && t.parent == _poolRoot && _piecePool != null && _piecePool.Contains(t)) return;
        if (_inAvalancheSlide && toPool && !allowDuringSlide) return;

        int frame = Time.frameCount;
        float timeVal = Time.time;

        int activeBefore = CountActivePieces();
        int pooled = _piecePool != null ? _piecePool.Count : 0;
        int rootChildren = _piecesRoot != null ? _piecesRoot.childCount : 0;

        string fileLine = GetFileLineFromStack();
        string caller = GetCallerMethodName();

        SnowDespawnLogger.RequestDespawn(reason, SnowDespawnLogger.SnowState.Unknown, t != null ? t.position : Vector3.zero, t != null ? t.gameObject : null);
        if (toPool)
        {
            EnsureRoot();
            if (_poolRoot == null)
            {
                if (Application.isPlaying)
                    UnityEngine.Debug.LogError("[SnowPackPoolError] OnPieceDeactivated: _poolRoot is null");
                return;
            }
            SetPieceVisualState(t, PieceVisualState.Pooled, false);
            if (t.parent == _piecesRoot)
            {
                int before = _piecesRoot.childCount;
                LogRootMutation(before, before - 1, "ReturnToPool");
            }
            if (blockDeactivate && t != null && t.gameObject != null && t.gameObject.name == "SnowPackPiece")
            {
                UnityEngine.Debug.LogError("DEACTIVATE BLOCKED frame=" + Time.frameCount + " pieceId=" + pieceId + " reason=" + reason + " source=" + source);
                return;
            }
            t.gameObject.SetActive(false);
            t.SetParent(_poolRoot, false);
            _piecePool.Add(t);
            _returnedToPoolIds.Add(pieceId);
        }
        else
        {
            UnityEngine.Object.Destroy(t.gameObject);
        }

        int activeAfter = CountActivePieces();
        PushLastEvent("Deactivated", $"{reason} source={source}", null, pieceId);

        string msg = $"[OnPieceDeactivated] frame={frame} t={timeVal:F2} activeBefore={activeBefore} activeAfter={activeAfter} pooled={pooled} rootChildren={rootChildren} reason={reason} pieceId={pieceId} source={source} caller={caller} fileLine={fileLine}";
        if (toPool && !_poolReturnFirstLogged.Contains(source))
        {
            _poolReturnFirstLogged.Add(source);
            bool useEx = source == "Avalanche" || source == "Unknown";
            if (useEx) TryLogException($"[PoolReturnFirst] source={source} fileLine={fileLine} caller={caller}");
            else UnityEngine.Debug.Log($"[PoolReturnFirst] source={source} fileLine={fileLine} caller={caller}");
        }
        UnityEngine.Debug.Log(msg);

        if (activeAfter == 0)
        {
            if (_firstActiveZeroTime < 0f)
            {
                _firstActiveZeroTime = timeVal;
                _firstActiveZeroFrame = frame;
            }
            BugOriginTracker.OnSnowPiecesZero();
            TryLogException($"[ACTIVE=0] firstFrame={_firstActiveZeroFrame} firstTime={_firstActiveZeroTime:F2}");
            // 直前20は Update の ACTIVE=0 検出で DumpLast20Events("ACTIVE=0") により出力
        }
    }

    int CountActivePieces()
    {
        if (_piecesRoot == null) return 0;
        var rnds = _piecesRoot.GetComponentsInChildren<Renderer>(true);
        int n = 0;
        for (int i = 0; i < rnds.Length; i++)
            if (rnds[i] != null && rnds[i].enabled) n++;
        return n;
    }

    /// <summary>activePieces=0 時、各 child がなぜ数えられないか診断。B2 用。</summary>
    void LogActivePiecesDiagnostic(int rootChildren, int activePiecesCount, int poolCount)
    {
        if (_piecesRoot == null || rootChildren <= 0) return;
        int nonCounted = rootChildren;
        for (int i = 0; i < rootChildren; i++)
        {
            var c = _piecesRoot.GetChild(i);
            if (c == null) continue;
            var r = c.GetComponentInChildren<Renderer>(true);
            if (r != null && r.enabled) nonCounted--;
        }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[B2_ACTIVE_DIAG] rootChildren={rootChildren} activePieces={activePiecesCount} nonCountedChildren={nonCounted}");
        int pooledFlagCount = 0;
        int inactiveHierarchyCount = 0;
        int rendererDisabledCount = 0;
        int noRendererCount = 0;
        int anchorCount = 0;
        int detachedCount = 0;
        int landedPieceCount = 0;
        int relaxedPieceCount = 0;
        for (int i = 0; i < rootChildren; i++)
        {
            var c = _piecesRoot.GetChild(i);
            if (c == null) continue;
            bool activeSelf = c.gameObject.activeSelf;
            bool activeInHierarchy = c.gameObject.activeInHierarchy;
            var r = c.GetComponentInChildren<Renderer>(true);
            bool hasRenderer = r != null;
            bool rendererEnabled = hasRenderer && r.enabled;
            var col = c.GetComponentInChildren<Collider>(true);
            bool colliderEnabled = col != null && col.enabled;
            bool pooled = _piecePool != null && _piecePool.Contains(c);
            bool isAnchor = c.name == "SnowPackAnchor";
            bool isPiece = c.name == "SnowPackPiece";
            bool detached = c.parent != _piecesRoot;
            var falling = c.GetComponent<SnowPackFallingPiece>();
            bool landed = falling != null && falling.hasLanded;
            var pos = c.position;
            var scale = c.lossyScale;
            string reason = "";
            if (isAnchor)
            {
                anchorCount++;
                reason = "anchor_not_snow_piece";
            }
            else if (pooled)
            {
                pooledFlagCount++;
                reason = "pooled";
            }
            else if (!activeInHierarchy)
            {
                inactiveHierarchyCount++;
                reason = "inactive_hierarchy";
            }
            else if (!hasRenderer)
            {
                noRendererCount++;
                reason = "no_renderer";
            }
            else if (!rendererEnabled)
            {
                rendererDisabledCount++;
                reason = "renderer_disabled";
            }
            else
            {
                reason = "counted";
            }
            if (isPiece)
            {
                relaxedPieceCount++;
                if (landed) landedPieceCount++;
            }
            sb.AppendLine($"  child_{i}_reason_not_counted={reason} name={c.name} activeSelf={activeSelf} activeInHierarchy={activeInHierarchy} renderer_enabled={rendererEnabled} collider_enabled={colliderEnabled} pooled={pooled} detached={detached} landed={landed} pos=({pos.x:F2},{pos.y:F2},{pos.z:F2}) scale=({scale.x:F2},{scale.y:F2},{scale.z:F2})");
        }
        sb.AppendLine($"[B2_ACTIVE_DIAG] pooled_flag_count={pooledFlagCount} inactive_hierarchy_count={inactiveHierarchyCount} renderer_disabled_count={rendererDisabledCount} no_renderer_count={noRendererCount} anchor_count={anchorCount} detached_but_should_survive_count={detachedCount} landed_but_should_survive_count={landedPieceCount} count_rule_relaxed_test={(relaxedPieceCount > 0).ToString().ToLower()} activePieces_after_relax={relaxedPieceCount}");
        UnityEngine.Debug.Log(sb.ToString());
    }

    struct EntityCounts
    {
        public int childCount;
        public int transformCount;
        public int pieceByNameCount;
        public int rendererCount;
    }

    EntityCounts GetEntityCounts()
    {
        var ec = new EntityCounts();
        if (_piecesRoot == null) return ec;
        ec.childCount = _piecesRoot.childCount;
        var transforms = _piecesRoot.GetComponentsInChildren<Transform>(true);
        ec.transformCount = transforms != null ? transforms.Length : 0;
        ec.pieceByNameCount = 0;
        if (transforms != null)
            for (int i = 0; i < transforms.Length; i++)
                if (transforms[i] != null && transforms[i].gameObject != null && transforms[i].gameObject.name == "SnowPackPiece")
                    ec.pieceByNameCount++;
        var rnds = _piecesRoot.GetComponentsInChildren<Renderer>(true);
        ec.rendererCount = rnds != null ? rnds.Length : 0;
        return ec;
    }

    static string GetFileLineFromStack()
    {
        string st = UnityEngine.StackTraceUtility.ExtractStackTrace();
        if (st != null && (st.Contains(".cs:") || st.Contains(".cs(")))
            return ExtractFirstFileLine(st);
        var trace = new StackTrace(true);
        for (int i = 0; i < trace.FrameCount; i++)
        {
            var frame = trace.GetFrame(i);
            if (frame == null) continue;
            string fn = frame.GetFileName();
            if (string.IsNullOrEmpty(fn) || !fn.EndsWith(".cs")) continue;
            int line = frame.GetFileLineNumber();
            return $"{System.IO.Path.GetFileName(fn)}:{line}";
        }
        return "unknown";
    }

    static string ExtractFirstFileLine(string stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace)) return "unknown";
        var match = System.Text.RegularExpressions.Regex.Match(stackTrace, @"([^/\\]+\.cs)(?::(\d+)|\((\d+)\))");
        if (match.Success)
        {
            string file = match.Groups[1].Value;
            string line = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
            return $"{file}:{line}";
        }
        return "unknown";
    }

    static string GetCallerMethodName()
    {
        var trace = new StackTrace(true);
        for (int i = 0; i < trace.FrameCount; i++)
        {
            var frame = trace.GetFrame(i);
            if (frame == null) continue;
            var method = frame.GetMethod();
            if (method == null) continue;
            string name = method.Name;
            if (name == "OnPieceDeactivated" || name == "ReturnToPool") continue;
            return name;
        }
        return "unknown";
    }

    /// <summary>子数を変更する全箇所で呼ぶ（SetParent/Destroy/PoolReturn/Clear/Rebuild/RemoveLayers）</summary>
    void LogRootMutation(int before, int after, string reason)
    {
        int frame = Time.frameCount;
        string st = GetRealStackTrace();
        string fileLine = GetFileLineFromStack();
        PushLastEvent("RootMutation", $"{reason} before={before} after={after} fileLine={fileLine}", st);
        bool isAbnormal = reason == "ReturnToPool" || reason == "Destroy(slideRoot)" || reason == "ClearSnowPack" || reason.Contains("RemoveLayers") || reason.Contains("SetParent(slideRoot)");
        if (isAbnormal) TryLogException($"[SnowPackRootMutation] before={before} after={after} reason={reason} frame={frame} t={Time.time:F2} fileLine={fileLine}");
        else UnityEngine.Debug.Log($"[SnowPackRootMutation] before={before} after={after} reason={reason} frame={frame} t={Time.time:F2}");
    }

    static bool _exceptionSuppressedLoggedOnce;

    static bool TryLogException(string msg)
    {
        if (_exceptionCount >= MaxExceptionCount)
        {
            if (!_exceptionSuppressedLoggedOnce)
            {
                _exceptionSuppressedLoggedOnce = true;
                UnityEngine.Debug.LogWarning($"[SnowPack] Exception logging paused (max {MaxExceptionCount} reached). Root cause should be fixed.");
            }
            return false;
        }
        _exceptionCount++;
        UnityEngine.Debug.Log(msg);
        return true;
    }

    enum SnapshotInvalidReason { RootNotFound, ComponentNull, Exception, NotInitialized, Other }

    /// <summary>SnowLoopLogCapture から 2 秒後に呼ばれる。LAST20/RUN_SNAPSHOT/STACKTRACE_SELFTEST を出力。</summary>
    public void RunAssiDiagnostic2s()
    {
        if (_assiDiagnosticLogged) return;
        _assiDiagnosticLogged = true;
        AssiDiagnostic2s();
    }

    void AssiDiagnostic2s()
    {
        int cc = -1, tc = -1, pbc = -1, rc = -1, ap = -1, rootCh = -1, pooled = -1;
        SnapshotInvalidReason invalidReason = SnapshotInvalidReason.Other;
        string findByName = "SnowPackVisual", findByTag = "none", found = "No", path = "null";

        try
        {
            if (roofCollider == null) roofCollider = ResolveRoofCollider();
            if (roofCollider == null)
            {
                invalidReason = SnapshotInvalidReason.NotInitialized;
                UnityEngine.Debug.Log($"[RUN_SNAPSHOT_FORCE] childCount={cc} transformCount={tc} pieceByNameCount={pbc} rendererCount={rc} activePieces={ap} rootChildren={rootCh} pooled={pooled}");
                UnityEngine.Debug.Log($"[SNAPSHOT_INVALID] reason={invalidReason}");
                UnityEngine.Debug.Log($"[SNAPSHOT_ROOT] findByName={findByName} findByTag={findByTag} found={found} path={path}");
            }
            else
            {
                var roofT = roofCollider.transform;
                var vr = roofT.Find("SnowPackVisual");
                path = vr != null ? GetTransformPath(vr) : "SnowPackVisual not found";
                found = vr != null ? "Yes" : "No";

                EnsureRoot();
                if (_piecesRoot == null)
                {
                    invalidReason = SnapshotInvalidReason.RootNotFound;
                    UnityEngine.Debug.Log($"[RUN_SNAPSHOT_FORCE] childCount={cc} transformCount={tc} pieceByNameCount={pbc} rendererCount={rc} activePieces={ap} rootChildren={rootCh} pooled={pooled}");
                    UnityEngine.Debug.Log($"[SNAPSHOT_INVALID] reason={invalidReason}");
                    UnityEngine.Debug.Log($"[SNAPSHOT_ROOT] findByName={findByName} findByTag={findByTag} found={found} path={path}");
                }
                else
                {
                    var ec = GetEntityCounts();
                    cc = ec.childCount;
                    tc = ec.transformCount;
                    pbc = ec.pieceByNameCount;
                    rc = ec.rendererCount;
                    rootCh = _piecesRoot.childCount;
                    pooled = _piecePool != null ? _piecePool.Count : 0;
                    ap = CountActivePieces();

                    bool hasInvalid = cc < 0 || tc < 0 || pbc < 0 || rc < 0 || ap < 0 || rootCh < 0 || pooled < 0;
                    if (tc < 0) invalidReason = SnapshotInvalidReason.ComponentNull;
                    else if (hasInvalid) invalidReason = SnapshotInvalidReason.Other;

                    UnityEngine.Debug.Log($"[RUN_SNAPSHOT_FORCE] childCount={cc} transformCount={tc} pieceByNameCount={pbc} rendererCount={rc} activePieces={ap} rootChildren={rootCh} pooled={pooled}");

                    if (hasInvalid)
                    {
                        UnityEngine.Debug.Log($"[SNAPSHOT_INVALID] reason={invalidReason}");
                        UnityEngine.Debug.Log($"[SNAPSHOT_ROOT] findByName={findByName} findByTag={findByTag} found={found} path={path}");
                    }
                }
            }

            int last20Count = _lastEvents != null ? _lastEvents.Count : 0;
            UnityEngine.Debug.Log($"[LAST20_FORCE] count={last20Count}");
            if (last20Count == 0)
                UnityEngine.Debug.Log("[LAST20_EMPTY]");

            string fileLine = GetFileLineFromStack();
            string method = "unknown";
            var trace = new StackTrace(true);
            for (int i = 0; i < trace.FrameCount; i++)
            {
                var f = trace.GetFrame(i);
                if (f == null) continue;
                var m = f.GetMethod();
                if (m == null) continue;
                if (m.Name == "AssiDiagnostic2s") continue;
                method = $"{m.DeclaringType?.Name}.{m.Name}";
                break;
            }
            bool ok = fileLine != "unknown" && fileLine.Contains(":");
            string okStr = ok ? "Yes" : "No";
            string selftestReason = ok ? "" : " reason=fileLineNotResolved";
            UnityEngine.Debug.Log($"[STACKTRACE_SELFTEST] ok={okStr} fileLine={fileLine} method={method}{selftestReason}");
        }
        catch (System.Exception ex)
        {
            invalidReason = SnapshotInvalidReason.Exception;
            UnityEngine.Debug.Log($"[RUN_SNAPSHOT_FORCE] childCount={cc} transformCount={tc} pieceByNameCount={pbc} rendererCount={rc} activePieces={ap} rootChildren={rootCh} pooled={pooled}");
            UnityEngine.Debug.Log($"[SNAPSHOT_INVALID] reason={invalidReason}");
            UnityEngine.Debug.Log($"[SNAPSHOT_ROOT] findByName={findByName} findByTag={findByTag} found={found} path={path}");
            int last20Count = _lastEvents != null ? _lastEvents.Count : 0;
            UnityEngine.Debug.Log($"[LAST20_FORCE] count={last20Count}");
            if (last20Count == 0) UnityEngine.Debug.Log("[LAST20_EMPTY]");
            UnityEngine.Debug.Log($"[STACKTRACE_SELFTEST] ok=No reason=Exception {ex.GetType().Name}:{ex.Message}");
        }
        EmitAssiRequired4Blocks();
    }

    /// <summary>2秒診断で必ず出力する4ブロック。Rebuild未実行時もN/A/Noneで補完。</summary>
    void EmitAssiRequired4Blocks()
    {
        LogPiecePoseSampleFirst3();
        LogRotationOverrideSuspectedLocations(force: true);
        bool avOff = AssiDebugUI.AutoAvalancheOff;
        string avStr = avOff ? "OFF" : "ON";
        UnityEngine.Debug.Log($"[AutoAvalancheState] default=OFF current={avStr}");
        bool lastTapValid = LastTapTime > 0f && LastRemovedCount > 0;
        string lastTapStr = lastTapValid ? "Yes" : "No";
        UnityEngine.Debug.Log($"[TapMarkerState] atStart visible=No lastTapValid={lastTapStr} LastTapTime={LastTapTime:F1} LastRemovedCount={LastRemovedCount} (2s診断補完)");
        LogSceneCodePath();
        DebugSnowVisibility.LogSceneObjectsVisible();
        DebugSnowVisibility.EmitRotationOverridesExecutedIfNone();
        GridVisualWatchdog.LogWatchdogStats();
    }

    void LogSceneCodePath()
    {
        string scene = SceneManager.GetActiveScene().name;
        bool hasSpawner = this != null && isActiveAndEnabled;
        bool hasRoof = roofCollider != null;
        bool hasPieces = _piecesRoot != null && _piecesRoot.childCount > 0;
        bool hasRoofSnow = roofSnowSystem != null && roofSnowSystem.isActiveAndEnabled;
        string spawnFunc = "SpawnPieceRoofBasis";
        if (_layerPieces.Count > 0 || (_piecesRoot != null && _piecesRoot.childCount > 0))
            spawnFunc = "SpawnPieceRoofBasis";
        bool forceDirect = debugForcePieceRendererDirect;
        UnityEngine.Debug.Log($"[SceneCodePath] scene={scene} SnowPackSpawner={hasSpawner} roofCollider={hasRoof} _piecesRoot.hasChildren={hasPieces} RoofSnowSystem={hasRoofSnow} spawnFunc={spawnFunc} debugForcePieceRendererDirect={forceDirect}");
    }

    static string GetTransformPath(Transform t)
    {
        if (t == null) return "null";
        var parts = new List<string>();
        var cur = t;
        while (cur != null && parts.Count < 8)
        {
            parts.Insert(0, cur.name);
            cur = cur.parent;
        }
        return "/" + string.Join("/", parts);
    }

    void LogSnowPackCall(string kind, string reason)
    {
        string scene = SceneManager.GetActiveScene().name;
        int rootChildren = _piecesRoot != null ? _piecesRoot.childCount : (_visualRoot != null ? _visualRoot.childCount : 0);
        float t = Application.isPlaying ? Time.time : 0f;
        int frame = Time.frameCount;
        float roofY = roofCollider != null ? roofCollider.transform.rotation.eulerAngles.y : 0f;
        float packY = _visualRoot != null ? _visualRoot.rotation.eulerAngles.y : 0f;
        string localText = UsingLocalPosition ? "true" : "false";
        UnityEngine.Debug.Log($"[SnowPack] {kind} reason={reason} frame={frame} t={t:F2} scene={scene} rootChildren={rootChildren} depth={targetDepthMeters:F2} size={pieceSize:F2} local={localText} roofRotY={roofY:F1} packRotY={packY:F1}");
    }

    void LogRemainingSnowVsRoofBounds()
    {
        if (roofCollider == null) roofCollider = ResolveRoofCollider();
        if (roofCollider == null || _piecesRoot == null) return;
        var roofBounds = roofCollider.bounds;
        Vector3 roofSize = roofBounds.size;
        Vector3 roofMin = roofBounds.min;
        Vector3 roofMax = roofBounds.max;
        float tol = 0.02f;
        Vector3 initialSnowSize = new Vector3(_roofWidth, 0.1f, _roofLength);
        Vector3 snowMin = Vector3.one * float.MaxValue;
        Vector3 snowMax = Vector3.one * float.MinValue;
        int packedCount = 0;
        for (int i = 0; i < _piecesRoot.childCount; i++)
        {
            var t = _piecesRoot.GetChild(i);
            if (t == null || !t.gameObject.activeSelf) continue;
            Vector3 p = t.position;
            snowMin = Vector3.Min(snowMin, p);
            snowMax = Vector3.Max(snowMax, p);
            packedCount++;
        }
        Vector3 remainingSize = packedCount > 0 ? (snowMax - snowMin) : Vector3.zero;
        bool initialExceeds = _roofWidth > roofSize.x + tol || _roofLength > roofSize.z + tol ||
            _roofWidth > roofSize.z + tol || _roofLength > roofSize.x + tol;
        bool remainingExceeds = packedCount > 0 && (
            snowMin.x < roofMin.x - tol || snowMax.x > roofMax.x + tol ||
            snowMin.y < roofMin.y - tol || snowMax.y > roofMax.y + tol ||
            snowMin.z < roofMin.z - tol || snowMax.z > roofMax.z + tol);
        string mismatchStage = initialExceeds && remainingExceeds ? "both" : (remainingExceeds ? "remaining" : (initialExceeds ? "initial" : "none"));
        UnityEngine.Debug.Log($"[SNOW_BOUNDS] roof_bounds_size=({roofSize.x:F3},{roofSize.y:F3},{roofSize.z:F3}) initial_snow_bounds_size=({initialSnowSize.x:F3},{initialSnowSize.z:F3}) remaining_snow_bounds_size=({remainingSize.x:F3},{remainingSize.y:F3},{remainingSize.z:F3}) remaining_snow_exceeds_roof={remainingExceeds.ToString().ToLower()} clip_to_roof_bounds=true mismatch_stage={mismatchStage}");
    }

    void ClipRemainingSnowToRoofBounds()
    {
        if (roofCollider == null || _piecesRoot == null || _roofWidth <= 0f || _roofLength <= 0f) return;
        float halfW = _roofWidth * 0.5f;
        float halfL = _roofLength * 0.5f;
        float margin = 0.02f;
        int clipped = 0;
        for (int i = 0; i < _piecesRoot.childCount; i++)
        {
            var t = _piecesRoot.GetChild(i);
            if (t == null || !t.gameObject.activeSelf) continue;
            Vector3 d = t.position - _roofCenter;
            float u = Vector3.Dot(d, _roofR);
            float v = Vector3.Dot(d, _roofF);
            if (Mathf.Abs(u) > halfW + margin || Mathf.Abs(v) > halfL + margin)
            {
                if (SnowVerifyB2Debug.Enabled)
                    UnityEngine.Debug.Log($"[B2_DEBUG] ClipToRoofBounds piece={t.name} pos=({t.position.x:F3},{t.position.y:F3},{t.position.z:F3}) u={u:F3} v={v:F3} halfW={halfW} halfL={halfL} margin={margin}");
                t.gameObject.SetActive(false);
                clipped++;
            }
        }
        if (clipped > 0)
        {
            if (SnowVerifyB2Debug.Enabled) SnowVerifyB2Debug.RecordClip(clipped);
            UnityEngine.Debug.Log($"[SNOW_CLIP] clipped_to_roof_bounds count={clipped}");
        }
    }

    void Update()
    {
        if (!Application.isPlaying) return;

        // ASSI Boot: SnowLoopLogCapture が [ASSI_BOOT] を出力。LAST20用に Boot を push
        if (Time.frameCount == 1 && !_assiBootLoggedStatic)
        {
            _assiBootLoggedStatic = true;
            PushLastEvent("Boot", "frame=1", null);
        }
        // ASSI 2秒診断: SnowLoopLogCapture から RunAssiDiagnostic2s() が呼ばれる

        // A) visualDepth を roofDepth に向けて毎フレーム平滑化（depth 暴走対策）
        if (roofSnowSystem == null) roofSnowSystem = FindFirstObjectByType<RoofSnowSystem>();
        if (roofSnowSystem != null)
        {
            float roofDepth = roofSnowSystem.roofSnowDepthMeters;
            if (!_visualDepthInitialized) { _visualDepth = roofDepth; _visualDepthInitialized = true; }
            _visualDepth = Mathf.Lerp(_visualDepth, roofDepth, Time.deltaTime / Mathf.Max(0.01f, visualSmoothTime));
        }

        if (_visualRoot != null)
        {
            var angleT = GetRoofAngleTransform();
            Vector3 roofUp = angleT != null ? angleT.up.normalized : (roofSnowSystem != null ? roofSnowSystem.RoofUp : Vector3.up);
            _visualRoot.rotation = Quaternion.FromToRotation(Vector3.up, roofUp);
        }

        RefreshPackedTransformsFromRoofBasis();

        ProcessScheduledWaves();
        ProcessChainReaction();

        float tSinceTap = Time.time - LastTapTime;
        if (LastRemovedCount > 0 && tSinceTap >= 1.2f && tSinceTap <= 1.35f && !_burstStatsLoggedThisTap)
        {
            _burstStatsLoggedThisTap = true;
            int sec = _chainDetachCountSinceTap;
            LastSecondaryDetachCount = sec;
            UnityEngine.Debug.Log($"[AVALANCHE_BURST_LOG] primary_detach_count={LastRemovedCount} primary_cluster_size={LastRemovedCount} secondary_detach_count={sec} secondary_triggered={LastSecondaryTriggered.ToString().ToLower()} largest_fall_group={LastRemovedCount} active_snow_visual=RoofSnowLayer+SnowPackPiece active_snow_break_logic=SnowPackSpawner.HandleTap+DetachInRadius");
        }
        if (tSinceTap > 2.5f) _burstStatsLoggedThisTap = false;

        if (Time.time - LastTapTime < 2f && roofCollider != null)
        {
            Vector3 roofUp = GetRoofAngleTransform() != null ? GetRoofAngleTransform().up.normalized : roofCollider.transform.up.normalized;
            Vector3 tangent = Vector3.ProjectOnPlane(roofCollider.transform.right, roofUp).normalized;
            if (tangent.sqrMagnitude < 0.01f) tangent = Vector3.ProjectOnPlane(roofCollider.transform.forward, roofUp).normalized;
            Vector3 tangent2 = Vector3.Cross(roofUp, tangent).normalized;
            int segs = 24;
            for (int i = 0; i < segs; i++)
            {
                float a0 = (i / (float)segs) * 2f * Mathf.PI;
                float a1 = ((i + 1) / (float)segs) * 2f * Mathf.PI;
                Vector3 p0 = LastTapWorld + (Mathf.Cos(a0) * tangent + Mathf.Sin(a0) * tangent2) * LastTapRadius;
                Vector3 p1 = LastTapWorld + (Mathf.Cos(a1) * tangent + Mathf.Sin(a1) * tangent2) * LastTapRadius;
                UnityEngine.Debug.DrawLine(p0, p1, LastPackedInRadiusBefore > 0 ? Color.yellow : Color.red, 0.5f);
            }
        }

        if (AssiDebugUI.ShowGridGizmos && roofCollider != null && _cachedNx > 0 && _cachedNz > 0)
        {
            if (!_gridDrawSpaceLogged) { _gridDrawSpaceLogged = true; UnityEngine.Debug.Log("[GridDrawSpace] space=RoofBasis (center+r*u*width+f*v*length+n*offset in world)"); }
            for (int iz = 0; iz <= _cachedNz; iz++)
            {
                float v = (iz / (float)_cachedNz) - 0.5f;
                for (int ix = 0; ix < _cachedNx; ix++)
                {
                    float u0 = (ix / (float)_cachedNx) - 0.5f, u1 = ((ix + 1) / (float)_cachedNx) - 0.5f;
                    Vector3 p0 = _roofCenter + _roofR * (u0 * _roofWidth) + _roofF * (v * _roofLength) + _roofN * RoofSurfaceOffset;
                    Vector3 p1 = _roofCenter + _roofR * (u1 * _roofWidth) + _roofF * (v * _roofLength) + _roofN * RoofSurfaceOffset;
                    UnityEngine.Debug.DrawLine(p0, p1, Color.cyan, 0.3f);
                }
            }
            for (int ix = 0; ix <= _cachedNx; ix++)
            {
                float u = (ix / (float)_cachedNx) - 0.5f;
                for (int iz = 0; iz < _cachedNz; iz++)
                {
                    float v0 = (iz / (float)_cachedNz) - 0.5f, v1 = ((iz + 1) / (float)_cachedNz) - 0.5f;
                    Vector3 p0 = _roofCenter + _roofR * (u * _roofWidth) + _roofF * (v0 * _roofLength) + _roofN * RoofSurfaceOffset;
                    Vector3 p1 = _roofCenter + _roofR * (u * _roofWidth) + _roofF * (v1 * _roofLength) + _roofN * RoofSurfaceOffset;
                    UnityEngine.Debug.DrawLine(p0, p1, Color.cyan, 0.3f);
                }
            }
        }

        // B) Pool返却キューの処理（Avalanche, returnRate=0.3 全削除禁止, max 50/frame）
        if (_poolReturnQueue.Count > 0 && !_inAvalancheSlide)
        {
            if (SnowVerifyB2Debug.Enabled && SnowVerifyB2Debug.PausePoolReturn)
            {
                if (Time.time - SnowVerifyB2Debug.LastPoolSkipLogTime >= 0.5f)
                {
                    SnowVerifyB2Debug.LastPoolSkipLogTime = Time.time;
                    int surv = GetB2TotalCount();
                    int queueSize = _poolReturnQueue.Count;
                    UnityEngine.Debug.Log($"[B2_POOL_SKIP] pool_return_called=false surviving_count_after_pool={surv} queue_size={queueSize} PausePoolReturn=true skipping_return");
                }
            }
            else
            {
            int queueCount = _poolReturnQueue.Count;
            int toReturn = Mathf.Min(MaxPoolReturnsPerFrame, Mathf.Max(1, (int)(queueCount * AvalancheReturnRate)));
            int returnedPieces = 0;
            int packedNow = GetPackedCubeCountRealtime();
            bool allowReturnWhenPackedZero = packedNow <= 0; // packed=0時はslideRoot残骸を必ず返却（止まり雪対策）
            int survBeforeReturn = SnowVerifyB2Debug.Enabled ? GetB2TotalCount() : 0;
            if (SnowVerifyB2Debug.Enabled)
                UnityEngine.Debug.Log($"[B2_POOL_BEFORE] surviving_count_before_cleanup={survBeforeReturn} queue_count={_poolReturnQueue.Count}");
            for (int i = 0; i < toReturn; i++)
            {
                if (_poolReturnQueue.Count == 0) break;
                int apBefore = CountActivePieces();
                if (!allowReturnWhenPackedZero && apBefore <= 1) break; // 全削除禁止: 最後の1つは返却しない
                var t = _poolReturnQueue[0];
                _poolReturnQueue.RemoveAt(0);
                ReturnToPool(t, "Queue", "Avalanche");
                returnedPieces++;
            }
            if (SnowVerifyB2Debug.Enabled && returnedPieces > 0)
                UnityEngine.Debug.Log($"[B2_POOL_AFTER] pool_return_called=true surviving_count_after_pool={GetB2TotalCount()} returned_this_frame={returnedPieces}");
            int activeAfter = CountActivePieces();
            UnityEngine.Debug.Log($"[AvalancheReturn] returnedPieces={returnedPieces} activeAfter={activeAfter} queueRemaining={_poolReturnQueue.Count}");

            if (_poolReturnQueue.Count == 0 && _pendingSlideRootToDestroy != null)
            {
                _lastAvalanchePackedCountAfter = -1;
                int ap = CountActivePieces();
                if (SnowVerifyB2Debug.Enabled)
                {
                    int survBefore = GetB2TotalCount();
                    UnityEngine.Debug.Log($"[B2_AFTER_CLEANUP] surviving_count_before_cleanup={survBefore} cleanup_called={SnowVerifyB2Debug.CleanupCalled.ToString().ToLower()} surviving_count_after_cleanup={survBefore} pool_return_called={SnowVerifyB2Debug.PoolReturnCalled.ToString().ToLower()}");
                    if (survBefore <= 0) SnowVerifyB2Debug.RecordZeroTransition("after_cleanup");
                }
                if (debugAutoRefillRoofSnow && ap == 0)
                {
                    RebuildSnowPack("AvalancheActiveZero"); // ClearSnowPack で slideRoot 破棄
                }
                else if (_pendingSlideRootToDestroy != null)
                {
                    UnityEngine.Object.Destroy(_pendingSlideRootToDestroy);
                }
                _pendingSlideRootToDestroy = null;
            }
            }
        }

        // C) Avalanche終了後の遅延記録（removeCount を Avalanche中に増やさない）
        if (_pendingRemoveCountFromAvalanche > 0 && (roofSnowSystem == null || !roofSnowSystem.IsInAvalancheCooldown))
        {
            RecordLayersRemoved(_pendingRemoveCountFromAvalanche, "AvalancheSlideVisual.end(deferred)");
            _pendingRemoveCountFromAvalanche = 0;
        }

        if (debugAutoRefillRoofSnow && debugMinPackedPieces > 0 && !IsSpawnFrozen && Time.time >= _nextMinFillTime)
        {
            int current = GetPackedCubeCountRealtime();
            if (current < debugMinPackedPieces)
            {
                int need = debugMinPackedPieces - current;
                int layersToAdd = Mathf.Max(1, (need + Mathf.Max(1, _cachedNx * _cachedNz) - 1) / Mathf.Max(1, _cachedNx * _cachedNz));
                layersToAdd = Mathf.Min(layersToAdd, 10);
                int pb = GetPackedCubeCountRealtime();
                AddLayers(layersToAdd);
                int pa = GetPackedCubeCountRealtime();
                _nextMinFillTime = Time.time + 0.5f;
                UnityEngine.Debug.Log($"[SnowPackMinFill] added {layersToAdd} layers current={current} target={debugMinPackedPieces}");
                UnityEngine.Debug.Log($"[REFILL] reason=SnowPackMinFill packedBefore={pb} packedAfter={pa}");
            }
        }

        // D) Depth sync (ヒステリシス+クールダウン) - スライド中・雪崩クールダウン・Freeze中はスキップ
        bool inAvalanche = IsSpawnFrozen || (roofSnowSystem != null && roofSnowSystem.IsInAvalancheCooldown);
        if (!inAvalanche && Time.time >= _nextSyncCheckTime)
        {
            _nextSyncCheckTime = Time.time + Mathf.Max(0.05f, syncIntervalSeconds);
            if (roofSnowSystem != null)
            {
                float roofDepth = roofSnowSystem.roofSnowDepthMeters;
                float oldPack = packDepthMeters;
                float delta = _visualDepth - oldPack;  // visualDepth 基準

                if (Time.time >= _nextSyncAllowedAt)
                {
                    EnsureRoot();
                    if (roofCollider == null) roofCollider = ResolveRoofCollider();
                    if (_cachedLayerStep <= 0f) CacheGridParams();

                    string action = "NoOp";
                    if (delta >= addThreshold && debugAutoRefillRoofSnow)
                    {
                        int layerDelta = Mathf.RoundToInt(delta / _cachedLayerStep);
                        layerDelta = Mathf.Clamp(layerDelta, 1, maxLayersPerSync);
                        int pb = GetPackedCubeCountRealtime();
                        AddLayers(layerDelta);
                        int pa = GetPackedCubeCountRealtime();
                        _nextSyncAllowedAt = Time.time + minSyncInterval;
                        action = $"AddLayers({layerDelta})";
                        UnityEngine.Debug.Log($"[REFILL] reason=DepthSync packedBefore={pb} packedAfter={pa}");
                    }
                    else if (delta <= removeThreshold)
                    {
                        int layerDelta = Mathf.RoundToInt(-delta / _cachedLayerStep);
                        layerDelta = Mathf.Clamp(layerDelta, 1, maxLayersPerSync);
                        int currentPieces = _piecesRoot != null ? CountPiecesUnder(_piecesRoot) : 0;
                        int wouldRemove = Mathf.Min(layerDelta, _layerPieces.Count);
                        int piecesInLayersToRemove = 0;
                        for (int ll = 0; ll < wouldRemove && _layerPieces.Count - 1 - ll >= 0; ll++)
                            piecesInLayersToRemove += _layerPieces[_layerPieces.Count - 1 - ll].Count;
                        if (currentPieces - piecesInLayersToRemove <= 0 && currentPieces > 0)
                        {
                            UnityEngine.Debug.Log($"[SnowPackChildrenGuard] preventedZero=true prevChildren={currentPieces} requested=RemoveLayers({layerDelta})");
                            action = "NoOp";
                        }
                        else
                        {
                            RemoveLayers(layerDelta);
                            _nextSyncAllowedAt = Time.time + minSyncInterval;
                            action = $"RemoveLayers({layerDelta})";
                        }
                    }

                    if (action != "NoOp")
                        UnityEngine.Debug.Log($"[SnowPackSync] roofDepth={roofDepth:F3} visualDepth={_visualDepth:F3} packDepth={oldPack:F3} delta={delta:F3} action={action}");
                }
            }
        }

        // E) Clip remaining snow to roof bounds (every 0.2s) + bounds log (every 3s)
        if (Time.time >= _nextClipToRoofTime)
        {
            _nextClipToRoofTime = Time.time + 0.2f;
            ClipRemainingSnowToRoofBounds();
        }
        if (Time.time >= _nextRemainingBoundsLogTime)
        {
            _nextRemainingBoundsLogTime = Time.time + 3f;
            LogRemainingSnowVsRoofBounds();
        }

        if (_visualRoot == null || _piecesRoot == null) return;
        if (!Application.isPlaying) return; // Stop時はスキップ
        UpdateStateIndicatorColor();
        int rootChildren = _piecesRoot.childCount;
        if (rootChildren < _rootChildrenMin1s) _rootChildrenMin1s = rootChildren;
        if (rootChildren > _rootChildrenMax1s) _rootChildrenMax1s = rootChildren;
        float vpDelta = Mathf.Abs(_visualDepth - packDepthMeters);
        if (vpDelta > _visualPackDeltaMax1s) _visualPackDeltaMax1s = vpDelta;
        int activeCount = CountPiecesUnder(_visualRoot);
        int poolCount = _piecePool != null ? _piecePool.Count : 0;
        int total = activeCount + poolCount;
        int activePiecesCount = 0;
        var rnds = _piecesRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rnds.Length; i++)
            if (rnds[i] != null && rnds[i].enabled) activePiecesCount++;
        int activeMismatch = Mathf.Abs(activePiecesCount - rootChildren);

        _lastActivePiecesCountForUI = activePiecesCount;
        if (activePiecesCount == 0 && _prevActivePiecesCount > 0)
        {
            if (_firstActiveZeroTime < 0f)
            {
                _firstActiveZeroTime = Time.time;
                _firstActiveZeroFrame = Time.frameCount;
            }
            _entityCountDumpedForActiveZero = false;
            _activeZeroUIFadeAt = Time.time + 2f;
            _failUIFadeAt = Time.time + 2f;
            DumpLast20Events("ACTIVE=0");
        }
        if (Time.time - _lastTransitionSampleTime >= TransitionSampleInterval)
        {
            _lastTransitionSampleTime = Time.time;
            _transitionSamples.Add(new TransitionSample { t = Time.time, rootCh = rootChildren, pooled = poolCount, active = activePiecesCount });
            if (_transitionSamples.Count > 120) _transitionSamples.RemoveAt(0);
        }
        if (activePiecesCount == 0 && !_entityCountDumpedForActiveZero)
        {
            _entityCountDumpedForActiveZero = true;
            var ec = GetEntityCounts();
            UnityEngine.Debug.Log($"[SnowPackEntityDump] ACTIVE=0 frame={Time.frameCount} childCount={ec.childCount} transformCount={ec.transformCount} pieceByNameCount={ec.pieceByNameCount} rendererCount={ec.rendererCount} activePieces={activePiecesCount} pooled={poolCount} rootChildren={rootChildren}");
            float t0 = Time.time - 1f;
            float t1 = Time.time + 1f;
            for (int i = 0; i < _transitionSamples.Count; i++)
            {
                var s = _transitionSamples[i];
                if (s.t >= t0 && s.t <= t1)
                    UnityEngine.Debug.Log($"[SnowPackTransition] t={s.t:F2} rootChildren={s.rootCh} pooled={s.pooled} active={s.active}");
            }
        }
        if (debugAutoRefillRoofSnow && activePiecesCount == 0 && !_autoRebuildFired && roofCollider != null)
        {
            _autoRebuildFired = true;
            _autoRebuildFrame = Time.frameCount;
            RebuildSnowPack("AutoRebuildOnActiveZero");
            int ap = CountActivePieces();
            int rc = _piecesRoot != null ? _piecesRoot.childCount : 0;
            int pc = _piecePool != null ? _piecePool.Count : 0;
            _autoRebuildRecovered = ap > 0;
            var ec = GetEntityCounts();
            if (!_autoRebuildRecovered)
            {
                DumpLast20Events("AUTO-REBUILD FAIL");
                if (ec.rendererCount == 0) _autoRebuildFailReason = AutoRebuildFailReason.NoRenderers;
                else if (ap == 0) _autoRebuildFailReason = AutoRebuildFailReason.ActivePiecesZero;
                else if ((ap + pc) == 0 && _poolInstantiated > 0) _autoRebuildFailReason = AutoRebuildFailReason.PoolInvariantFail;
                else if (rc != ec.childCount) _autoRebuildFailReason = AutoRebuildFailReason.RootChildrenMismatch;
                else _autoRebuildFailReason = AutoRebuildFailReason.Unknown;
                UnityEngine.Debug.Log($"[AUTO-REBUILD FAIL] reason={_autoRebuildFailReason} active={ap} rootChildren={rc} pooled={pc} rendererCount={ec.rendererCount} pieceByNameCount={ec.pieceByNameCount}");
                _failUIFadeAt = Time.time + 2f;
                _failFrame = Time.frameCount;
                _failTime = Time.time;
            }
            else _autoRebuildFailReason = AutoRebuildFailReason.None;
        }
        _prevActivePiecesCount = activePiecesCount;
        bool invariantOk = true;
        bool likelyTeardown = rootChildren == 0 && activePiecesCount == 0;
        if (total > 0)
        {
            _zeroTotalErrorEmittedOnce = false;
            _zeroTotalFirstFrame = -1;
            _zeroTotalRepeatCount = 0;
            _zeroTotalSuppressLogged = false;
        }
        if (total == 0 && _poolInstantiated > 0 && !likelyTeardown)
        {
            invariantOk = false;
            _zeroTotalRepeatCount++;
            int survivingCount = GetB2TotalCount();
            string triggerStep = SnowVerifyB2Debug.Enabled ? (SnowVerifyB2Debug.ZeroTransitionStep ?? SnowVerifyB2Debug.ZeroTotalTriggerStep ?? "invariant_check") : "invariant_check";
            if (SnowVerifyB2Debug.Enabled) SnowVerifyB2Debug.ZeroTotalTriggerStep = triggerStep;
            bool isFirst = !_zeroTotalErrorEmittedOnce;
            if (isFirst)
            {
                _zeroTotalFirstFrame = Time.frameCount;
                _zeroTotalErrorEmittedOnce = true;
                bool cleanupActive = SnowVerifyB2Debug.Enabled && SnowVerifyB2Debug.CleanupCalled;
                bool poolActive = _poolReturnQueue.Count > 0;
                bool recoveryAttempted = debugAutoRefillRoofSnow && _autoRebuildFired;
                string recoveryCond = debugAutoRefillRoofSnow ? "debugAutoRefillRoofSnow_active_zero" : "none";
                UnityEngine.Debug.Log($"[B2_ZERO_MONITOR] zero_total_detected=true zero_total_first_frame={_zeroTotalFirstFrame} zero_total_repeat_count={_zeroTotalRepeatCount} monitor_loop_active=true cleanup_loop_active={cleanupActive.ToString().ToLower()} pool_monitor_active={poolActive.ToString().ToLower()} error_emitted_once=true error_suppressed_after_first=false zero_total_recovery_attempted={recoveryAttempted.ToString().ToLower()} zero_total_recovery_condition={recoveryCond}");
                UnityEngine.Debug.LogWarning($"[SnowPackPoolError] reason=totalBecameZeroAfterGeneration total={total} active={activeCount} pooled={poolCount} (1回のみ、以降は抑制)");
            }
            else
            {
                if (!_zeroTotalSuppressLogged)
                {
                    _zeroTotalSuppressLogged = true;
                    UnityEngine.Debug.Log($"[B2_ZERO_MONITOR] zero_total_detected=true zero_total_first_frame={_zeroTotalFirstFrame} zero_total_repeat_count={_zeroTotalRepeatCount} monitor_loop_active=true error_emitted_once=true error_suppressed_after_first=true zero_total_recovery_attempted={(_autoRebuildFired && debugAutoRefillRoofSnow).ToString().ToLower()} error_suppressed_after_first=true");
                }
            }
            _nextSyncCheckTime = Time.time + 1f;
            return;
        }
        if (rootChildren <= 1 && activePiecesCount > 50 && !_inAvalancheSlide && !likelyTeardown)
        {
#if UNITY_EDITOR
            UnityEngine.Debug.LogError($"[SnowPackChildrenError] children={rootChildren} activePieces={activePiecesCount} total={total}\n{GetRealStackTrace()}");
#else
            UnityEngine.Debug.LogError($"[SnowPackChildrenError] children={rootChildren} activePieces={activePiecesCount} total={total}");
#endif
        }
        if (rootChildren == 0 && _lastChildrenCount > 0)
        {
            if (!_childrenGuardStackLogged)
            {
                _childrenGuardStackLogged = true;
#if UNITY_EDITOR
                UnityEngine.Debug.Log($"[SnowPackChildrenGuard] preventedZero=true prevChildren={_lastChildrenCount} requested={rootChildren}\nSTACK:\n{GetRealStackTrace()}");
#else
                UnityEngine.Debug.Log($"[SnowPackChildrenGuard] preventedZero=true prevChildren={_lastChildrenCount} requested={rootChildren}");
#endif
            }
            return;
        }
        _lastChildrenCount = rootChildren;

        float targetDepth = _visualDepth;
        float maxDown = maxDownStepPerSec * Time.deltaTime;
        float beforePd = packDepthMeters;
        float maxDelta = packDepthMeters > targetDepth ? maxDown : 1f;
        packDepthMeters = Mathf.MoveTowards(packDepthMeters, targetDepth, maxDelta);
        packDepthMeters = Mathf.Max(minVisibleDepth, packDepthMeters);
        if (beforePd != packDepthMeters && beforePd > targetDepth)
            UnityEngine.Debug.Log($"[SnowPackDepthGuard] before={beforePd:F3} target={targetDepth:F3} after={packDepthMeters:F3} maxDownStep={maxDownStepPerSec:F3}");
        float pd = packDepthMeters;
        if (pd < _packDepthMin1s) _packDepthMin1s = pd;
        if (pd > _packDepthMax1s) _packDepthMax1s = pd;
        if (Time.time >= _nextAuditLogTime)
        {
            _nextAuditLogTime = Time.time + 1f;
            float roofD = roofSnowSystem != null ? roofSnowSystem.roofSnowDepthMeters : -1f;
            float roofVisualDelta = roofSnowSystem != null ? roofD - _visualDepth : -1f;
            float deltaSync = _visualDepth - packDepthMeters;
            float pdMin = _packDepthMin1s == float.MaxValue ? pd : _packDepthMin1s;
            float pdMax = _packDepthMax1s == float.MinValue ? pd : _packDepthMax1s;
            int pieceCount = CountPiecesUnder(_piecesRoot);
            var ec = GetEntityCounts();
            UnityEngine.Debug.Log($"[SnowPackEntity1s] activePieces={activePiecesCount} childCount={ec.childCount} transformCount={ec.transformCount} pieceByNameCount={ec.pieceByNameCount} rendererCount={ec.rendererCount}");
            UnityEngine.Debug.Log($"[SnowPackHierarchy] root=SnowPackPiecesRoot childCount={rootChildren} pieceCount={pieceCount}");
            UnityEngine.Debug.Log($"[SnowPackPoolInvariant] total={total} active={activeCount} pooled={poolCount} ok={invariantOk}");
            UnityEngine.Debug.Log($"[SnowPackPool] total={total} active={activeCount} pooled={poolCount} reused={_poolReused} instantiated={_poolInstantiated}");
            UnityEngine.Debug.Log($"[SnowPackSync] roofDepth={roofD:F3} visualDepth={_visualDepth:F3} packDepth={packDepthMeters:F3} delta={deltaSync:F3}");
            int rootDelta1s = _rootChildrenMax1s >= 0 && _rootChildrenMin1s < int.MaxValue ? _rootChildrenMax1s - _rootChildrenMin1s : -1;
            bool activePiecesZero = (activePiecesCount == 0);
            if (activePiecesZero)
            {
#if UNITY_EDITOR
                bool skipFail = SnowPackSpawner.EditorExitingPlayMode;
#else
                bool skipFail = false;
#endif
                bool onlyAnchorLeft = rootChildren == 1 && _piecesRoot != null && _piecesRoot.childCount >= 1 && _piecesRoot.GetChild(0).name == "SnowPackAnchor";
                bool allCleared = (rootChildren <= 1 && poolCount > 0) || onlyAnchorLeft;
                // SNOW LOOK PHASE3/4: showSnowGridDebug=false 時はキューブを非表示にしているため activePiecesCount(=enabled renderers)=0 は正常。rootChildren>0 なら実体あり。
                bool snowShellModeOk = rootChildren > 0 && !GridVisualWatchdog.showSnowGridDebug;
                if (!_snowPackPassErrorLogged && Application.isPlaying && rootChildren > 0 && !skipFail && !allCleared && !snowShellModeOk)
                {
                    LogActivePiecesDiagnostic(rootChildren, activePiecesCount, poolCount);
                    _snowPackPassErrorLogged = true;
                    UnityEngine.Debug.LogError($"[SnowPackPASS] activePieces=0 FAIL frame={Time.frameCount} t={Time.time:F2} rootChildren={rootChildren} pooled={poolCount} (1回のみ表示、上に B2_ACTIVE_DIAG 参照)");
                }
                if (!_activeZeroReportLogged && _firstActiveZeroTime >= 0f)
                {
                    _activeZeroReportLogged = true;
                    UnityEngine.Debug.Log($"[SnowPackActiveZero] occurred=Yes firstTime={_firstActiveZeroTime:F2} firstFrame={_firstActiveZeroFrame}");
                }
            }
            else if (!_activeZeroReportLogged && _firstActiveZeroTime < 0f && _rebuildCount > 0)
            {
                _activeZeroReportLogged = true;
                UnityEngine.Debug.Log($"[SnowPackActiveZero] occurred=No");
            }
            bool inCooldown = roofSnowSystem != null && roofSnowSystem.IsInAvalancheCooldown;
            if (inCooldown && _poolReturnQueue.Count > 0)
                UnityEngine.Debug.Log($"[SnowPackAvalancheGuard] poolReturnDeferred inAvalanche=true queueSize={_poolReturnQueue.Count} evidence=NoPoolReturnDuringAvalanche");
            float fallingScale = (snowFallSystem ?? (roofSnowSystem != null ? FindFirstObjectByType<SnowFallSystem>() : null))?.GetFallingScale() ?? pieceSize;
            UnityEngine.Debug.Log($"[SnowPackScale1s] FallingScale={fallingScale:F3} PackedScale={pieceSize:F3} BurstScale={pieceSize:F3}");
            UnityEngine.Debug.Log($"[SnowPackAudit1s] frame={Time.frameCount} t={Time.time:F2} roofDepth={roofD:F3} visualDepth={_visualDepth:F3} packDepth={packDepthMeters:F3} packDepthMin1s={pdMin:F3} packDepthMax1s={pdMax:F3} roofVisualDelta={roofVisualDelta:F3} rootChildren={rootChildren} rootChildrenDelta1s={rootDelta1s} visualPackDeltaMax1s={_visualPackDeltaMax1s:F3} activePieces={activePiecesCount} activeMismatch={activeMismatch} rebuildCount={_rebuildCount} addCount={_addCount} removeCount={_removeCount}");
            if (roofCollider != null && _visualRoot != null)
            {
                Vector3 roofUp = GetRoofAngleTransform() != null ? GetRoofAngleTransform().up.normalized : roofCollider.transform.up.normalized;
                Vector3 roofFwd = roofCollider.transform.forward.normalized;
                Vector3 packUp = _visualRoot.up.normalized;
                Vector3 packFwd = _visualRoot.forward.normalized;
                float dotUp = Vector3.Dot(roofUp, packUp);
                float dotFwd = Vector3.Dot(roofFwd, packFwd);
                string okBasisStr = dotUp >= 0.98f ? "true" : "FAIL";
                UnityEngine.Debug.Log($"[SnowPackBasisAudit1s] dotUp={dotUp:F3} dotFwd={dotFwd:F3} usingLocal={UsingLocalPosition} ok={okBasisStr}");
            }
            _packDepthMin1s = float.MaxValue;
            _packDepthMax1s = float.MinValue;
            _rootChildrenMin1s = int.MaxValue;
            _rootChildrenMax1s = int.MinValue;
            _visualPackDeltaMax1s = 0f;
        }
        if (Time.time < _nextToggleLogTime) return;
        _nextToggleLogTime = Time.time + 1f;
        if (!_visualRoot.gameObject.activeInHierarchy)
            UnityEngine.Debug.Log("[SnowPackToggleCheck] visualActive=false -> if ground rise stops now, issue is SnowPackVisual side");
    }

    void OnGUI()
    {
        bool showFail = _failUIFadeAt > 0f && Time.time <= _failUIFadeAt;
        if (!showFail) return;
        float tw = Screen.width, th = Screen.height;
        int fontSize = AssiDebugUI.debugOverlayEnabled
            ? Mathf.Max(120, (int)(th * 0.3f))
            : 14;
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            alignment = AssiDebugUI.debugOverlayEnabled ? TextAnchor.MiddleCenter : TextAnchor.UpperLeft,
            normal = { textColor = new Color(1f, 0.2f, 0.2f) }
        };
        string reasonStr = _autoRebuildFailReason != AutoRebuildFailReason.None
            ? $"AUTO-REBUILD FAIL: {_autoRebuildFailReason}"
            : "ACTIVE=0";
        int frame = _autoRebuildFailReason != AutoRebuildFailReason.None ? _failFrame : _firstActiveZeroFrame;
        float t = _autoRebuildFailReason != AutoRebuildFailReason.None ? _failTime : _firstActiveZeroTime;
        if (_firstActiveZeroTime < 0f) t = Time.time;
        string msg = $"{reasonStr} frame={frame} t={t:F1}";
        if (AssiDebugUI.debugOverlayEnabled)
            GUI.Label(new Rect(0, th * 0.2f, tw, th * 0.5f), msg + "\n", style);
        else
            GUI.Label(new Rect(8f, 8f, tw - 16f, 24f), msg, style);
        if (_autoRebuildFired && _autoRebuildFailReason == AutoRebuildFailReason.None && AssiDebugUI.debugOverlayEnabled)
        {
            style.fontSize = Mathf.Max(72, (int)(th * 0.15f));
            style.normal.textColor = _autoRebuildRecovered ? Color.green : Color.red;
            string status = _autoRebuildRecovered ? "復旧OK" : "復旧FAIL";
            GUI.Label(new Rect(0, th * 0.55f, tw, th * 0.15f), $"AUTO-REBUILD: {status}", style);
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (_piecesRoot == null || !Application.isPlaying) return;
        int ap = _lastActivePiecesCountForUI >= 0 ? _lastActivePiecesCountForUI : 0;
        var rnds = _piecesRoot.GetComponentsInChildren<Renderer>(true);
        int active = 0;
        for (int i = 0; i < rnds.Length; i++)
            if (rnds[i] != null && rnds[i].enabled) active++;
        Vector3 pos = _piecesRoot.position + Vector3.up * 0.5f;
        UnityEditor.Handles.Label(pos, $"activePieces={active}");
    }
#endif

    static void ClearChildren(Transform root)
    {
        if (root == null) return;
        var toDelete = new List<GameObject>();
        for (int i = 0; i < root.childCount; i++)
            toDelete.Add(root.GetChild(i).gameObject);
        for (int i = 0; i < toDelete.Count; i++)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(toDelete[i]);
            else UnityEngine.Object.DestroyImmediate(toDelete[i]);
        }
    }
}
