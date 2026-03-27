using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Snow Panic 雪崩物理: 気持ちいい崩壊体験に最適化。
/// 溜め→崩れ始め→連鎖→巨大雪崩→余韻 の5段階を再現。
/// </summary>
[DefaultExecutionOrder(100)]
public class AvalanchePhysicsSystem : MonoBehaviour
{
    [Header("References")]
    public SnowPackSpawner snowPackSpawner;
    public RoofSnowSystem roofSnowSystem;

    [Header("Support thresholds")]
    [Tooltip("support_value > this = Stable")]
    public float thresholdStable = 3f;
    [Tooltip("support_value > this = Weak, else Critical")]
    public float thresholdWeak = 1f;

    [Header("Hit")]
    [Tooltip("1タップで減る support")]
    public float hitDamage = 1.5f;
    public float hitRadius = 0.7f;

    [Header("Chain")]
    [Tooltip("隣接clusterへ伝播する weaken 量")]
    public float weakenValue = 1.2f;
    [Tooltip("遅延 detach の確率")]
    [Range(0f, 1f)] public float detachChance = 0.65f;
    [Tooltip("遅延秒数")]
    public float chainDelaySec = 0.25f;

    [Header("Mega Avalanche")]
    [Tooltip("連続Detach数がこれを超えると Mega")]
    public int megaConsecutiveThreshold = 8;
    [Tooltip("連鎖段数がこれを超えると Mega")]
    public int megaChainDepthThreshold = 2;

    [Header("Slide")]
    public float slideSpeed = 1.2f;

    [Header("Weak Point")]
    [Tooltip("support_damage 倍率（弱点ヒット時）")]
    public float weakPointDamageMultiplier = 3f;
    [Tooltip("隣接クラスターへの追加 weaken 量")]
    public float weakPointNeighborWeakenBonus = 2f;
    [Tooltip("Mega Avalanche 確率ボーナス")]
    [Range(0f, 1f)] public float weakPointMegaChanceBonus = 0.4f;
    [Tooltip("スコアボーナス倍率")]
    public float weakPointScoreMultiplier = 2f;

    [Header("Toggle")]
    [Tooltip("false=従来の PlayLocalAvalancheAt を使う")]
    public bool useAvalanchePhysics = true;

    readonly List<SnowCluster> _clusters = new List<SnowCluster>();
    readonly HashSet<int> _weakPointHintApplied = new HashSet<int>();
    readonly List<(Transform t, int ix, int iz)> _pieceBuffer = new List<(Transform, int, int)>();
    readonly Dictionary<(int, int), SnowCluster> _cellToCluster = new Dictionary<(int, int), SnowCluster>();
    readonly HashSet<SnowCluster> _detachedThisHit = new HashSet<SnowCluster>();
    readonly Queue<(SnowCluster c, int depth)> _chainQueue = new Queue<(SnowCluster, int)>();
    readonly List<SnowCluster> _neighborsBuffer = new List<SnowCluster>();

    int _clusterIdCounter;
    int _clustersDetachedTotal;
    int _maxChainDepth;
    bool _megaAvalancheTriggered;
    int _consecutiveDetachThisHit;
    float _weakPointNeighborBonus;
    float _weakPointMegaChanceBonus;
    bool _weakPointHitThisTap;

    public static int ClustersTotal { get; private set; }
    public static int ClustersDetached { get; private set; }
    public static int MaxChainDepth { get; private set; }
    public static float SlideDistanceAvg => s_slideCount > 0 ? s_slideDistanceAccum / s_slideCount : 0f;
    static float s_slideDistanceAccum;
    static int s_slideCount;
    public static bool MegaAvalancheTriggered { get; private set; }
    public static int MegaAvalancheCount { get; private set; }

    /// <summary>Run Structure: 新Run開始時にカウンタをリセット。</summary>
    public static void ResetRunCounters()
    {
        ClustersTotal = 0;
        ClustersDetached = 0;
        MaxChainDepth = 0;
        MegaAvalancheTriggered = false;
        MegaAvalancheCount = 0;
        WeakPointHits = 0;
        WeakPointMegaTriggered = false;
        s_slideDistanceAccum = 0f;
        s_slideCount = 0;
    }
    /// <summary>直近タップでの合計剥離数（RoofSnowSystem 表示用）</summary>
    public static int LastTapRemovedTotal { get; private set; }

    public static int WeakPointsTotal { get; private set; }
    public static int WeakPointHits { get; private set; }
    public static bool WeakPointMegaTriggered { get; private set; }

    /// <summary>観測用: 直近タップ位置から全クラスターへの最短距離。</summary>
    public static float LastTapNearestClusterDistance { get; private set; }
    /// <summary>観測用: 直近タップで半径内にヒットしたクラスター数。</summary>
    public static int LastTapHitClustersCount { get; private set; }
    /// <summary>観測用: 直近タップでいずれかのクラスターが Critical になったか。</summary>
    public static bool LastTapAnyClusterCritical { get; private set; }

    void Start()
    {
        if (snowPackSpawner == null) snowPackSpawner = FindFirstObjectByType<SnowPackSpawner>();
        if (roofSnowSystem == null) roofSnowSystem = FindFirstObjectByType<RoofSnowSystem>();
        _clustersDetachedTotal = 0;
        _maxChainDepth = 0;
        s_slideDistanceAccum = 0f;
        s_slideCount = 0;
        MegaAvalancheTriggered = false;
        ClustersTotal = 0;
        ClustersDetached = 0;
        MaxChainDepth = 0;
        WeakPointsTotal = 0;
        WeakPointHits = 0;
        WeakPointMegaTriggered = false;
    }

    public void OnSnowHit(Vector3 worldPoint)
    {
        if (snowPackSpawner == null || roofSnowSystem == null) return;
        if (!useAvalanchePhysics) return;
        if (roofSnowSystem.heightmap_mode_enabled) return;

        RebuildClusters();
        if (_clusters.Count == 0) return;

        _detachedThisHit.Clear();
        _consecutiveDetachThisHit = 0;
        LastTapRemovedTotal = 0;
        _weakPointHitThisTap = false;

        var hitClusters = GetClustersInRadius(worldPoint, hitRadius);
        float nearest = float.MaxValue;
        foreach (var c in _clusters)
        {
            if (c.ActivePieceCount == 0) continue;
            float d = Vector3.Distance(c.Center, worldPoint);
            if (d < nearest) nearest = d;
        }
        LastTapNearestClusterDistance = nearest < float.MaxValue ? nearest : -1f;
        LastTapHitClustersCount = hitClusters.Count;
        bool anyWeakPointHit = false;
        foreach (var c in hitClusters)
        {
            if (c.isWeakPoint) anyWeakPointHit = true;
            float damage = hitDamage * (c.isWeakPoint ? weakPointDamageMultiplier : 1f);
            c.support_value -= damage;
            c.UpdateState(thresholdStable, thresholdWeak);
            bool isCrit = c.weak_state == SnowCluster.ClusterState.Critical;
            string reason = isCrit ? "support_value<=thresholdWeak" : (c.support_value > thresholdStable ? "support_value>thresholdStable" : "support_value>thresholdWeak");
            UnityEngine.Debug.Log($"[SNOW_CRITICAL_CHECK] cluster_id={c.cluster_id} cluster_size={c.ActivePieceCount} support_count={c.support_value:F2} edge_contact={c.edge_contact:F2} is_critical={isCrit.ToString().ToLower()} reason={reason}");
            if (isCrit && !_detachedThisHit.Contains(c))
                DetachCluster(c, 0, c.isWeakPoint);
        }

        _weakPointNeighborBonus = anyWeakPointHit ? weakPointNeighborWeakenBonus : 0f;
        _weakPointMegaChanceBonus = anyWeakPointHit ? weakPointMegaChanceBonus : 0f;
        _weakPointHitThisTap = anyWeakPointHit;
        if (anyWeakPointHit) WeakPointHits++;

        ProcessChainQueue();
        LastTapAnyClusterCritical = ClustersDetached > 0;
    }

    void RebuildClusters()
    {
        _clusters.Clear();
        _cellToCluster.Clear();
        _clusterIdCounter = 0;
        if (snowPackSpawner == null) return;

        snowPackSpawner.CollectPackedPiecesWithGrid(_pieceBuffer);
        if (_pieceBuffer.Count == 0) return;

        // Coarse grid: 2x2 fine cells = 1 cluster region. Cluster = 3~8 pieces.
        const int coarseScale = 2;
        var cellToPieces = new Dictionary<(int, int), List<(Transform t, int ix, int iz)>>();
        foreach (var p in _pieceBuffer)
        {
            int cx = p.ix / coarseScale;
            int cz = p.iz / coarseScale;
            var key = (cx, cz);
            if (!cellToPieces.TryGetValue(key, out var list))
            {
                list = new List<(Transform, int, int)>();
                cellToPieces[key] = list;
            }
            list.Add(p);
        }

        foreach (var kv in cellToPieces)
        {
            var pieces = kv.Value;
            if (pieces.Count == 0) continue;

            var cluster = new SnowCluster { cluster_id = _clusterIdCounter++, cellX = kv.Key.Item1, cellZ = kv.Key.Item2 };
            foreach (var p in pieces)
                cluster.piece_list.Add(p.t);

            float below = 0f, side = 0f, slopeEscape = 0.3f;
            foreach (var p in pieces)
            {
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = p.ix + dx, nz = p.iz + dz;
                        var nk = (nx / coarseScale, nz / coarseScale);
                        if (cellToPieces.ContainsKey(nk)) side += 0.5f;
                    }
                if (p.iz > 0) below += 1f;
            }
            int n = pieces.Count;
            cluster.edge_contact = side;
            float baseSupport = Mathf.Max(0.5f, below / n + side / n - slopeEscape) * n * 0.5f + 1f;
            cluster.support_value = Mathf.Clamp(baseSupport, 1.5f, 6f);
            cluster.UpdateState(thresholdStable, thresholdWeak);
            _clusters.Add(cluster);
            _cellToCluster[kv.Key] = cluster;
        }

        int weakCount = Mathf.Max(1, _clusters.Count / 8);
        var indices = new List<int>();
        for (int i = 0; i < _clusters.Count; i++) indices.Add(i);
        for (int i = indices.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }
        for (int k = 0; k < weakCount && k < indices.Count; k++)
        {
            var c = _clusters[indices[k]];
            c.isWeakPoint = true;
            ApplyWeakPointVisualHint(c);
        }
        WeakPointsTotal = weakCount;
        ClustersTotal = _clusters.Count;
    }

    void ApplyWeakPointVisualHint(SnowCluster cluster)
    {
        foreach (var t in cluster.piece_list)
        {
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            int id = t.GetInstanceID();
            if (_weakPointHintApplied.Contains(id)) continue;
            _weakPointHintApplied.Add(id);
            var r = t.GetComponentInChildren<Renderer>(true);
            if (r == null || r.sharedMaterial == null) continue;
            var mat = new Material(r.sharedMaterial);
            var c = MaterialColorHelper.GetColorSafe(mat, Color.white);
            c *= 0.92f;
            c.a = 1f;
            MaterialColorHelper.SetColorSafe(mat, c);
            r.sharedMaterial = mat;
        }
    }

    List<SnowCluster> GetClustersInRadius(Vector3 worldPoint, float radius)
    {
        var result = new List<SnowCluster>();
        float r2 = radius * radius;
        foreach (var c in _clusters)
        {
            if (c.ActivePieceCount == 0) continue;
            float sq = (c.Center - worldPoint).sqrMagnitude;
            if (sq <= r2) result.Add(c);
        }
        return result;
    }

    void DetachCluster(SnowCluster cluster, int chainDepth, bool fromWeakPoint = false)
    {
        if (cluster == null || cluster.ActivePieceCount == 0) return;
        if (_detachedThisHit.Contains(cluster)) return;

        int scoreNow = SnowPhysicsScoreManager.Instance != null ? SnowPhysicsScoreManager.Instance.Score : 0;
        int pieceCount = 0;
        string firstName = "cluster";
        foreach (var t in cluster.piece_list)
        {
            if (t != null && t.gameObject.activeInHierarchy) { pieceCount++; if (firstName == "cluster") firstName = t.name; }
        }
        UnityEngine.Debug.Log($"[SNOW_HIT_CHECK] hit_detected=true hit_object_name={firstName} script_source=AvalanchePhysicsSystem.cs time={UnityEngine.Time.time:F2} current_score={scoreNow}");

        _detachedThisHit.Add(cluster);
        _consecutiveDetachThisHit++;
        _maxChainDepth = Mathf.Max(_maxChainDepth, chainDepth);
        _clustersDetachedTotal++;
        ClustersDetached = _clustersDetachedTotal;
        MaxChainDepth = _maxChainDepth;

        bool megaByCount = _consecutiveDetachThisHit >= megaConsecutiveThreshold || chainDepth >= megaChainDepthThreshold;
        bool megaByWeakPoint = fromWeakPoint && Random.value < _weakPointMegaChanceBonus;
        if (megaByCount || megaByWeakPoint)
        {
            _megaAvalancheTriggered = true;
            MegaAvalancheTriggered = true;
            MegaAvalancheCount++;
            if (fromWeakPoint || _weakPointHitThisTap) WeakPointMegaTriggered = true;
        }

        var toDetach = new List<Transform>();
        foreach (var t in cluster.piece_list)
        {
            if (t != null && t.gameObject.activeInHierarchy) toDetach.Add(t);
        }
        if (toDetach.Count == 0) return;

        if (fromWeakPoint && SnowPhysicsScoreManager.Instance != null && weakPointScoreMultiplier > 1f)
        {
            int scoreBefore = SnowPhysicsScoreManager.Instance.Score;
            UnityEngine.Debug.Log($"[SNOW_HIT_CHECK] hit_detected=true hit_object=cluster_pieces_count={toDetach.Count} script=AvalanchePhysicsSystem.cs time={UnityEngine.Time.time:F2} current_score={scoreBefore}");
            SnowPhysicsScoreManager.Instance.Add(Mathf.RoundToInt(toDetach.Count * (weakPointScoreMultiplier - 1f)));
        }

        Vector3 slideDir = snowPackSpawner.RoofDownhill;
        if (slideDir.sqrMagnitude < 0.001f && roofSnowSystem != null && roofSnowSystem.roofSlideCollider != null)
        {
            Vector3 up = roofSnowSystem.roofSlideCollider.transform.up;
            slideDir = Vector3.ProjectOnPlane(Vector3.down, up).normalized;
        }
        float spd = _megaAvalancheTriggered ? slideSpeed * 1.5f : slideSpeed;

        UnityEngine.Debug.Log($"[SNOW_DETACH_PIPE] requested=true piece_count={toDetach.Count} fromTap=true weakpoint={fromWeakPoint}");
        snowPackSpawner.DetachPiecesDirect(toDetach, slideDir, spd);
        LastTapRemovedTotal += toDetach.Count;

        if (chainDepth == 0 && toDetach.Count <= 4)
            AvalancheFeedback.TriggerMicroShakeIfExists();
        else if (_megaAvalancheTriggered)
            AvalancheFeedback.Trigger();

        float dist = slideSpeed * 0.8f;
        s_slideDistanceAccum += dist;
        s_slideCount++;

        var neighbors = GetNeighborClusters(cluster);
        float neighborWeaken = weakenValue + _weakPointNeighborBonus;
        foreach (var nb in neighbors)
        {
            if (_detachedThisHit.Contains(nb)) continue;
            nb.support_value -= neighborWeaken;
            nb.UpdateState(thresholdStable, thresholdWeak);
            if (Random.value < detachChance)
                _chainQueue.Enqueue((nb, chainDepth + 1));
            else if (nb.weak_state == SnowCluster.ClusterState.Critical)
                _chainQueue.Enqueue((nb, chainDepth + 1));
        }
    }

    List<SnowCluster> GetNeighborClusters(SnowCluster cluster)
    {
        _neighborsBuffer.Clear();
        if (cluster.piece_list.Count == 0) return _neighborsBuffer;

        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                var k = (cluster.cellX + dx, cluster.cellZ + dz);
                if (_cellToCluster.TryGetValue(k, out var nc) && nc != cluster && !_detachedThisHit.Contains(nc))
                    _neighborsBuffer.Add(nc);
            }
        return _neighborsBuffer;
    }

    void ProcessChainQueue()
    {
        if (_chainQueue.Count == 0) return;
        StartCoroutine(ProcessChainQueueRoutine());
    }

    IEnumerator ProcessChainQueueRoutine()
    {
        yield return new WaitForSeconds(chainDelaySec);
        int limit = 20;
        while (_chainQueue.Count > 0 && limit-- > 0)
        {
            var (c, depth) = _chainQueue.Dequeue();
            if (c.ActivePieceCount == 0 || _detachedThisHit.Contains(c)) continue;
            c.UpdateState(thresholdStable, thresholdWeak);
            if (c.weak_state == SnowCluster.ClusterState.Critical)
                DetachCluster(c, depth);
        }
        _chainQueue.Clear();
    }

    /// <summary>ASSI Report 用。Play終了時に呼ぶ。</summary>
    public static void EmitAvalancheTestToReport()
    {
        SnowLoopLogCapture.AppendToAssiReport("=== AVALANCHE TEST ===");
        SnowLoopLogCapture.AppendToAssiReport($"clusters_total={ClustersTotal}");
        SnowLoopLogCapture.AppendToAssiReport($"clusters_detached={ClustersDetached}");
        SnowLoopLogCapture.AppendToAssiReport($"max_chain_depth={MaxChainDepth}");
        SnowLoopLogCapture.AppendToAssiReport($"slide_distance_avg={SlideDistanceAvg:F3}");
        SnowLoopLogCapture.AppendToAssiReport($"mega_avalanche_triggered={MegaAvalancheTriggered.ToString().ToLower()}");

        SnowLoopLogCapture.AppendToAssiReport("=== WEAK POINT TEST ===");
        SnowLoopLogCapture.AppendToAssiReport($"weak_points_total={WeakPointsTotal}");
        SnowLoopLogCapture.AppendToAssiReport($"weak_point_hits={WeakPointHits}");
        SnowLoopLogCapture.AppendToAssiReport($"weak_point_mega_triggered={WeakPointMegaTriggered.ToString().ToLower()}");
    }
}
