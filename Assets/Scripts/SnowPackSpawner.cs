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
    [Tooltip("積雪厚スケール。1=等倍, 0.1=5cm相当(50cm→5cm)")]
    [Range(0.01f, 1f)] public float snowDepthScale = 0.1f;
    [Tooltip("Piece見た目厚みスケール。1=等倍, 0.25≈1.25cm相当(5cm→2.5cm→1.25cm)")]
    [Range(0.01f, 1f)] public float snowPieceThicknessScale = 0.25f;
    [Tooltip("描画メッシュ厚みスケール。1=等倍, 0.5=半分(見た目Y)")]
    [Range(0.01f, 1f)] public float snowRenderThicknessScale = 0.5f;
    [Range(0.05f, 0.5f)] public float pieceSize = 0.11f;
    [Tooltip("見た目だけのスケール（ロジックはpieceSizeのまま）。1=等倍, 0.1=1/10")]
    [Range(0.01f, 1f)] public float visualScale = 0.1f;
    [Range(0.5f, 2f)] public float pieceHeightScale = 0.85f;
    [Range(0f, 0.08f)] public float jitter = 0.03f;
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
    [Tooltip("Secondary wave count = clamp(removed*this, 12, 45).")]
    [Range(0.2f, 0.5f)] public float secondaryDetachFraction = 0.35f;
    [Tooltip("Third wave delay (only when removedCount >= 60).")]
    public float thirdWaveDelaySec = 0.85f;
    [Tooltip("Third wave count = clamp(removed*this, 8, 30).")]
    [Range(0.1f, 0.3f)] public float thirdWaveFraction = 0.20f;
    [Range(0f, 1f), Tooltip("Chance unstable cells detach when expiry/passing.")]
    public float chainDetachChance = 0.55f;
    [Tooltip("Max total chain detachments per hit (avalanche growth cap).")]
    public int maxSecondaryDetachPerHit = 60;

    Transform _visualRoot;
    Transform _piecesRoot;
    Material _snowMat;
    Mesh _pieceMesh;
    Mesh _pieceMeshNonSym;
    bool _generatedThisPlay;
    bool _spawnLogOnce;
    bool _scaleLogOnce;
    static bool _poolReturnThrowOnce;
    float _nextToggleLogTime;
    float _nextAuditLogTime;
    float _nextSyncCheckTime;
    float _nextSyncAllowedAt;
    float _nextMinFillTime = -10f;
    const bool UsingLocalPosition = true;
    const float RoofSurfaceOffset = 0.01f;

    /// <summary>Avalanche/Tap局所処理中、または AssiDebugUI.DebugFreezeSpawn 時は Spawn・MinFill・Sync 追加を停止</summary>
    public bool IsSpawnFrozen => _inAvalancheSlide || (AssiDebugUI.DebugFreezeSpawn);

    readonly List<List<Transform>> _layerPieces = new List<List<Transform>>();
    readonly Dictionary<(int, int), List<Transform>> _gridPieces = new Dictionary<(int, int), List<Transform>>();
    readonly Dictionary<Transform, (int ix, int iz, int layer)> _pieceToGridData = new Dictionary<Transform, (int, int, int)>();
    readonly List<Transform> _piecePool = new List<Transform>();
    Transform _poolRoot;
    int _poolReused;
    int _poolInstantiated;
    float _cachedLayerStep;
    int _cachedNx, _cachedNz;
    Vector3 _cachedLocalCenter;
    float _cachedHalfX, _cachedHalfZ;
    Vector3 _roofN, _roofR, _roofF, _roofDownhill;
    Vector3 _roofCenter;
    float _roofWidth, _roofLength;
    float _roofCellSize;
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
    int _lastActivePiecesCountForUI = -1;
    bool _entityCountDumpedForActiveZero;
    bool _autoRebuildFired;
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
        if (debugMinPackedPieces > 0)
        {
            int approxPerLayer = _cachedNx * _cachedNz;
            layers = Mathf.Max(layers, Mathf.Max(1, (debugMinPackedPieces + approxPerLayer - 1) / Mathf.Max(1, approxPerLayer)));
        }
        int spawned = 0;
        for (int y = 0; y < layers; y++)
        {
            var layerList = SpawnLayer(y);
            _layerPieces.Add(layerList);
            spawned += layerList.Count;
            if (spawned >= maxPieces) break;
            if (debugMinPackedPieces > 0 && spawned >= debugMinPackedPieces) break;
        }

        _rebuildCount++;
        AuditSnowPackPhysics();
        packDepthMeters = effectiveDepth;
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

        LogPiecePoseSampleFirst3();
        LogRotationOverrideSuspectedLocations();
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

                UnityEngine.Debug.Log($"[PiecePoseSample] pieceId={pieceId} pieceTransform.name={pieceT.name} pieceT.worldEuler=({we.x:F1},{we.y:F1},{we.z:F1}) pieceT.up=({pieceUp.x:F3},{pieceUp.y:F3},{pieceUp.z:F3}) dotUpN={dotUpN:F3} childRendererT.name={(childRendererT != null ? childRendererT.name : "null")} childRendererT.worldEuler=({childWe.x:F1},{childWe.y:F1},{childWe.z:F1}) childRendererT.up=({childUp.x:F3},{childUp.y:F3},{childUp.z:F3}) dotChildUpN={dotChildUpN:F3} parentChain=[{parentChain}] 判定={verdict}");
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

        Bounds b = roofCollider.bounds;
        _roofCenter = b.center;
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
        _roofWidth = Mathf.Max(0.5f, maxR - minR);
        _roofLength = Mathf.Max(0.5f, maxF - minF);
        _roofCellSize = Mathf.Max(0.05f, pieceSize);

        float inset = _roofCellSize * 0.5f;
        _cachedNx = Mathf.Max(1, Mathf.FloorToInt((_roofWidth - inset * 2f) / _roofCellSize));
        _cachedNz = Mathf.Max(1, Mathf.FloorToInt((_roofLength - inset * 2f) / _roofCellSize));
        _cachedLayerStep = Mathf.Max(0.02f, _roofCellSize * pieceHeightScale);

        float dotRN = Vector3.Dot(r, n);
        float dotFN = Vector3.Dot(f, n);
        float dotRF = Vector3.Dot(r, f);
        UnityEngine.Debug.Log($"[RoofBasis] dot(r,n)={dotRN:F4} dot(f,n)={dotFN:F4} dot(r,f)={dotRF:F4} roofUp=({n.x:F3},{n.y:F3},{n.z:F3}) downhill=({downhill.x:F3},{downhill.y:F3},{downhill.z:F3})");
        UnityEngine.Debug.Log($"[RoofBasis] center=({_roofCenter.x:F2},{_roofCenter.y:F2},{_roofCenter.z:F2}) width={_roofWidth:F2} length={_roofLength:F2} cell={_roofCellSize:F2} nx={_cachedNx} nz={_cachedNz}");
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
                float u = (ix + 0.5f) / _cachedNx - 0.5f;
                float v = (iz + 0.5f) / _cachedNz - 0.5f;
                float jx = UnityEngine.Random.Range(-jitter, jitter);
                float jz = UnityEngine.Random.Range(-jitter, jitter);
                float layerOffset = RoofSurfaceOffset + layerIndex * _cachedLayerStep;
                Vector3 p = _roofCenter + _roofR * (u * _roofWidth + jx) + _roofF * (v * _roofLength + jz) + _roofN * layerOffset;
                Vector3 cp = roofCollider.ClosestPoint(p + _roofN * 0.1f);
                if ((cp - p).sqrMagnitude > 0.35f) continue;

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
    public static int LastRemovedCount;
    public static int LastPackedInRadiusBefore;
    public static Vector3 LastTapWorld;
    public static Vector2 LastTapRoofLocal;
    public static int LastPackedTotalBefore;
    public static int LastPackedTotalAfter;
    public static float LastTapTime = -10f;
    public static float LastTapRadius = 0.6f;
    public static int LastChainTriggerCount;
    public static float LastAvgRoofSlideDuration => _roofSlideSampleCount > 0 ? _avgRoofSlideDuration : 0f;
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
        int cx = Mathf.RoundToInt((u + 0.5f) * _cachedNx - 0.5f);
        int cz = Mathf.RoundToInt((v + 0.5f) * _cachedNz - 0.5f);
        cx = Mathf.Clamp(cx, 0, _cachedNx - 1);
        cz = Mathf.Clamp(cz, 0, _cachedNz - 1);

        int packedBefore = GetPackedCubeCountRealtime();
        int packedInRadiusBefore = 0;
        var toRemove = new List<Transform>();
        var seen = new HashSet<Transform>();
        float r = radius;
        const float radiusMax = 1.5f;

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

        int capped = Mathf.Min(toRemove.Count, localAvalancheMaxDetach);
        if (toRemove.Count > capped)
        {
            for (int i = toRemove.Count - 1; i >= capped; i--)
                toRemove.RemoveAt(i);
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

        UnityEngine.Debug.Log($"[TapDebug] tapU={u:F4} tapV={v:F4} cx={cx} cz={cz} tapWorld=({worldCenter.x:F3},{worldCenter.y:F3},{worldCenter.z:F3}) packedInRadiusBefore={packedInRadiusBefore}");
        int scx = Mathf.Clamp(cx, 0, _cachedNx - 1);
        int scz = Mathf.Clamp(cz, 0, _cachedNz - 1);
        float su = (scx + 0.5f) / _cachedNx - 0.5f;
        float sv = (scz + 0.5f) / _cachedNz - 0.5f;
        Vector3 cellCenter = _roofCenter + _roofR * (su * _roofWidth) + _roofF * (sv * _roofLength) + _roofN * RoofSurfaceOffset;
        float halfCell = _roofCellSize * 0.5f;
        Vector3 cellMin = cellCenter - _roofR * halfCell - _roofF * halfCell - _roofN * 0.05f;
        Vector3 cellMax = cellCenter + _roofR * halfCell + _roofF * halfCell + _roofN * 0.2f;
        Bounds cellBounds = new Bounds(cellCenter, (cellMax - cellMin));
        UnityEngine.Debug.Log($"[CellDebug] sampleCell(cx={scx},cz={scz}) sampleCellWorld=({cellCenter.x:F3},{cellCenter.y:F3},{cellCenter.z:F3}) cellBounds=({cellBounds.min.x:F3},{cellBounds.min.y:F3},{cellBounds.min.z:F3})-({cellBounds.max.x:F3},{cellBounds.max.y:F3},{cellBounds.max.z:F3}) tapInsideBounds={cellBounds.Contains(worldCenter)}");

        if (removedCount == 0)
        {
            LastRemovedCount = 0;
            LastPackedInRadiusBefore = packedInRadiusBefore;
            UnityEngine.Debug.Log($"[LocalAvalanche] R={radius:F2} u={u:F3} v={v:F3} cx={cx} cz={cz} removedCount=0 packedInRadiusBefore={packedInRadiusBefore} packedTotal={packedBefore} gridCells={_gridPieces.Count}");
            return;
        }

        int packedAfter = packedBefore - removedCount;
        _lastAvalanchePackedCountAfter = packedAfter;
        LastRemovedCount = removedCount;
        LastPackedInRadiusBefore = packedInRadiusBefore;

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
            int count = Mathf.Clamp(Mathf.RoundToInt(lastRemoved * secondaryDetachFraction), 12, 45);
            FireScheduledWave(count);
        }
        if (!_scheduledThirdWaveFired && lastRemoved >= 60 && tSinceTap >= thirdWaveDelaySec)
        {
            _scheduledThirdWaveFired = true;
            int count = Mathf.Clamp(Mathf.RoundToInt(lastRemoved * thirdWaveFraction), 8, 30);
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
                Vector3 worldCenter = _roofCenter + _roofR * ((key.Item1 + 0.5f) / Mathf.Max(1, _cachedNx) - 0.5f) * _roofWidth + _roofF * ((key.Item2 + 0.5f) / Mathf.Max(1, _cachedNz) - 0.5f) * _roofLength + _roofN * RoofSurfaceOffset;
                roofSnowSystem.SpawnLocalBurstAt(worldCenter, 1, _roofDownhill.normalized);
            }
            detached++;
        }
        if (detached > 0)
            UnityEngine.Debug.Log($"[ChainWave] scheduled wave detached={detached} chainTotal={_chainDetachCountSinceTap}");
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
                Vector3 worldCenter = _roofCenter + _roofR * ((key.Item1 + 0.5f) / Mathf.Max(1, _cachedNx) - 0.5f) * _roofWidth + _roofF * ((key.Item2 + 0.5f) / Mathf.Max(1, _cachedNz) - 0.5f) * _roofLength + _roofN * RoofSurfaceOffset;
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

    IEnumerator LocalAvalancheSlideRoutine(List<Transform> pieces, Vector3 slopeDir, float slideSpeed)
    {
        if (pieces == null || pieces.Count == 0) { _inAvalancheSlide = false; yield break; }
        var parent = _visualRoot != null ? _visualRoot : (roofCollider != null ? roofCollider.transform : null);
        if (parent == null) { _inAvalancheSlide = false; yield break; }

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

        foreach (var t in pieces)
        {
            if (t != null)
            {
                SetPieceVisualState(t, PieceVisualState.Sliding, false);
                var mr = t.GetComponentInChildren<Renderer>();
                if (mr != null && mr.sharedMaterial != null)
                {
                    var mat = new Material(mr.sharedMaterial);
                    mat.color = Color.red;
                    mr.sharedMaterial = mat;
                }
                t.SetParent(slideRoot.transform, true);
            }
        }

        yield return new WaitForSeconds(0.15f);

        float duration = 0.8f;
        float elapsed = 0f;
        Vector3 startPos = slideRoot.transform.position;
        Vector3 slideOffset = slopeDir * slideSpeed * duration;
        LayerMask groundMask = (roofSnowSystem != null && roofSnowSystem.groundMask.value != 0) ? roofSnowSystem.groundMask : ~0;
        Vector3 slideVelocity = slopeDir.normalized * slideSpeed;

        while (elapsed < duration)
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            float t01 = Mathf.Clamp01(elapsed / duration);
            slideRoot.transform.position = startPos + slideOffset * t01;

            // Step1: Check each piece for roof edge (t > tEnd + margin)
            for (int i = slideRoot.transform.childCount - 1; i >= 0; i--)
            {
                var t = slideRoot.transform.GetChild(i);
                if (t == null) continue;
                Vector3 piecePos = t.position;
                float tVal = Vector3.Dot(piecePos - roofCenter, downhill);
                if (tVal > tEnd + RoofEdgeMargin)
                {
                    // Step2: Convert to falling - detach, add Rigidbody, gravity, keep velocity
                    t.SetParent(null, true);
                    var falling = t.gameObject.GetComponent<SnowPackFallingPiece>();
                    if (falling == null) falling = t.gameObject.AddComponent<SnowPackFallingPiece>();
                    falling.spawner = this;
                    falling.groundMask = groundMask;
                    falling.ActivateFalling(slideVelocity);

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
        _pendingSlideRootToDestroy = slideRoot;
        _inAvalancheSlide = false;
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

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t01 = Mathf.Clamp01(elapsed / duration);
            slideRoot.transform.position = startWorldPos + slideOffset * t01;
            yield return null;
        }

        Vector3 endWorldPos = startWorldPos + slideOffset;
        float movedMeters = Vector3.Distance(startWorldPos, endWorldPos);

        // AvalancheVisual.end の後に、1フレームあたり上限で分割返却（一括で rootChildren 激変を防ぐ）
        for (int i = 0; i < layers.Count; i++)
        {
            for (int j = 0; j < layers[i].Count; j++)
            {
                var piece = layers[i][j];
                if (piece != null)
                {
                    SetPieceVisualState(piece, PieceVisualState.Returning, i == 0 && j == 0);
                    _poolReturnQueue.Add(piece);
                }
            }
        }
        _pendingSlideRootToDestroy = slideRoot; // キュー処理完了後に破棄

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
            t = SpawnPieceRoofBasis(worldPos, worldRot, size);
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
            float h = Mathf.Max(0.03f, size * pieceHeightScale * UnityEngine.Random.Range(0.8f, 1.2f));
            h *= snowPieceThicknessScale;
            float baseSize = Mathf.Max(0.05f, size);
            Vector3 scale = debugForcePieceRendererDirect
                ? new Vector3(baseSize, h * snowRenderThicknessScale, baseSize)
                : new Vector3(baseSize, h, baseSize);
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

    Transform SpawnPieceRoofBasis(Vector3 worldPos, Quaternion worldRot, float size)
    {
        var go = new GameObject("SnowPackPiece");
        int before = _piecesRoot.childCount;
        go.transform.SetParent(_piecesRoot, true);
        LogRootMutation(before, before + 1, "SpawnPieceRoofBasis");
        PushLastEvent("SpawnPieceRoofBasis", $"pieceId={go.GetInstanceID()}", null);
        float h = Mathf.Max(0.03f, size * pieceHeightScale * UnityEngine.Random.Range(0.8f, 1.2f));
        h *= snowPieceThicknessScale;
        go.transform.position = worldPos;
        go.transform.rotation = worldRot;
        float baseSize = Mathf.Max(0.05f, size);
        Vector3 scale = debugForcePieceRendererDirect
            ? new Vector3(baseSize, h * snowRenderThicknessScale, baseSize)
            : new Vector3(baseSize, h, baseSize);
        go.transform.localScale = scale;

        if (debugForcePieceRendererDirect)
        {
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var mesh = GetCurrentPieceMesh(); if (mesh != null) mf.sharedMesh = mesh;
            if (_snowMat != null) mr.sharedMaterial = _snowMat;
            mr.enabled = GridVisualWatchdog.showSnowGridDebug;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
        float h = Mathf.Max(0.03f, size * pieceHeightScale * UnityEngine.Random.Range(0.8f, 1.2f));
        h *= snowPieceThicknessScale;
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        float baseSize = Mathf.Max(0.05f, size);
        Vector3 scale = new Vector3(baseSize, h * snowRenderThicknessScale, baseSize);
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
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        int snowLayer = LayerMask.NameToLayer(SnowVisualLayerName);
        if (snowLayer < 0) snowLayer = LayerMask.NameToLayer("Ignore Raycast");
        if (snowLayer < 0) snowLayer = 2;
        SetLayerRecursively(go, snowLayer);
        LogSpawnOnce(go);
        SetPieceVisualState(go.transform, PieceVisualState.Accumulating);
        if (!_scaleLogOnce) { _scaleLogOnce = true; UnityEngine.Debug.Log($"[SnowPieceScale] kind=Packed scale=({scale.x:F3},{scale.y:F3},{scale.z:F3})"); }
        return go.transform;
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
                mat.color = new Color(1f, 1f, 1f, 0.15f);
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
        _stateIndicatorRenderer.material.color = c;
        _stateIndicatorRenderer.enabled = true;
    }

    void EnsureMaterial()
    {
        if (_snowMat != null) return;
        Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (sh == null) return;
        _snowMat = new Material(sh);
        _snowMat.color = snowColor;
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
        EnsurePieceMesh();
        if (DebugSnowVisibility.DebugNonSymMesh)
        {
            EnsureNonSymMesh();
            return _pieceMeshNonSym;
        }
        return _pieceMesh;
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
            float u = (item.ix + 0.5f) / _cachedNx - 0.5f;
            float v = (item.iz + 0.5f) / _cachedNz - 0.5f;
            float layerOffset = RoofSurfaceOffset + item.layer * _cachedLayerStep;
            Vector3 p = _roofCenter + _roofR * (u * _roofWidth) + _roofF * (v * _roofLength) + _roofN * layerOffset;
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
        if (debugMinPackedPieces > 0)
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
                sb.AppendLine($"  at {method?.DeclaringType?.FullName}.{method?.Name} in {System.IO.Path.GetFileName(fn)}:{line}");
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
                r.material.color = s == PieceVisualState.Accumulating ? _colorAccum : s == PieceVisualState.Sliding ? _colorSliding : s == PieceVisualState.Cooldown ? _colorCooldown : s == PieceVisualState.Returning ? _colorReturning : _colorPooled;
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

        if (toPool)
        {
            EnsureRoot();
            if (_poolRoot == null)
            {
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
        UnityEngine.Debug.LogException(new System.Exception(msg));
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
            string selftestReason = ok ? "" : " reason=fileLineNotResolved";
            UnityEngine.Debug.Log($"[STACKTRACE_SELFTEST] ok={(ok ? "Yes" : "No")} fileLine={fileLine} method={method}{selftestReason}");
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
        UnityEngine.Debug.Log($"[AutoAvalancheState] default=OFF current={(avOff ? "OFF" : "ON")}");
        bool lastTapValid = LastTapTime > 0f && LastRemovedCount > 0;
        UnityEngine.Debug.Log($"[TapMarkerState] atStart visible=No lastTapValid={(lastTapValid ? "Yes" : "No")} LastTapTime={LastTapTime:F1} LastRemovedCount={LastRemovedCount} (2s診断補完)");
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
            int queueCount = _poolReturnQueue.Count;
            int toReturn = Mathf.Min(MaxPoolReturnsPerFrame, Mathf.Max(1, (int)(queueCount * AvalancheReturnRate)));
            int returnedPieces = 0;
            for (int i = 0; i < toReturn; i++)
            {
                if (_poolReturnQueue.Count == 0) break;
                int apBefore = CountActivePieces();
                if (apBefore <= 1) break; // 全削除禁止: 最後の1つは返却しない
                var t = _poolReturnQueue[0];
                _poolReturnQueue.RemoveAt(0);
                ReturnToPool(t, "Queue", "Avalanche");
                returnedPieces++;
            }
            int activeAfter = CountActivePieces();
            UnityEngine.Debug.Log($"[AvalancheReturn] returnedPieces={returnedPieces} activeAfter={activeAfter} queueRemaining={_poolReturnQueue.Count}");

            if (_poolReturnQueue.Count == 0 && _pendingSlideRootToDestroy != null)
            {
                _lastAvalanchePackedCountAfter = -1;
                int ap = CountActivePieces();
                if (ap == 0)
                {
                    RebuildSnowPack("AvalancheActiveZero"); // ClearSnowPack で slideRoot 破棄
                }
                // Destroy(slideRoot) 禁止: 直接 Destroy は行わない。参照のみクリア。
                _pendingSlideRootToDestroy = null;
            }
        }

        // C) Avalanche終了後の遅延記録（removeCount を Avalanche中に増やさない）
        if (_pendingRemoveCountFromAvalanche > 0 && (roofSnowSystem == null || !roofSnowSystem.IsInAvalancheCooldown))
        {
            RecordLayersRemoved(_pendingRemoveCountFromAvalanche, "AvalancheSlideVisual.end(deferred)");
            _pendingRemoveCountFromAvalanche = 0;
        }

        if (debugMinPackedPieces > 0 && !IsSpawnFrozen && Time.time >= _nextMinFillTime)
        {
            int current = GetPackedCubeCountRealtime();
            if (current < debugMinPackedPieces)
            {
                int need = debugMinPackedPieces - current;
                int layersToAdd = Mathf.Max(1, (need + Mathf.Max(1, _cachedNx * _cachedNz) - 1) / Mathf.Max(1, _cachedNx * _cachedNz));
                layersToAdd = Mathf.Min(layersToAdd, 10);
                AddLayers(layersToAdd);
                _nextMinFillTime = Time.time + 0.5f;
                UnityEngine.Debug.Log($"[SnowPackMinFill] added {layersToAdd} layers current={current} target={debugMinPackedPieces}");
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
                    if (delta >= addThreshold)
                    {
                        int layerDelta = Mathf.RoundToInt(delta / _cachedLayerStep);
                        layerDelta = Mathf.Clamp(layerDelta, 1, maxLayersPerSync);
                        AddLayers(layerDelta);
                        _nextSyncAllowedAt = Time.time + minSyncInterval;
                        action = $"AddLayers({layerDelta})";
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

        if (_visualRoot == null || _piecesRoot == null) return;
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
        if (activePiecesCount == 0 && !_autoRebuildFired && roofCollider != null)
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
        if (total == 0 && _poolInstantiated > 0)
        {
            invariantOk = false;
            UnityEngine.Debug.LogError($"[SnowPackPoolError] reason=totalBecameZeroAfterGeneration total={total} active={activeCount} pooled={poolCount}");
            _nextSyncCheckTime = Time.time + 1f;
            return;
        }
        if (rootChildren <= 1 && activePiecesCount > 50 && !_inAvalancheSlide)
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
                UnityEngine.Debug.LogError($"[SnowPackPASS] activePieces=0 FAIL frame={Time.frameCount} t={Time.time:F2} rootChildren={rootChildren} pooled={poolCount}");
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
                UnityEngine.Debug.Log($"[SnowPackBasisAudit1s] dotUp={dotUp:F3} dotFwd={dotFwd:F3} usingLocal={UsingLocalPosition} ok={(dotUp >= 0.98f ? "true" : "FAIL")}");
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
