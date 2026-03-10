using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 雪サイズ不一致の原因切り分け専用。
/// Roof Renderer / Collider / Snow Spawn Area / Visible Snow の4 bounds を取得・可視化・ログ。
/// Avalanche_Test_OneHouse で動作。
/// </summary>
public class SnowSizeDiagnostics : MonoBehaviour
{
    [Header("Diagnostic")]
    [Tooltip("true で有効。OneHouse シーンのみ自動 ON")]
    public bool enableDiagnostics = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureInOneHouse()
    {
        string scene = SceneManager.GetActiveScene().name ?? "";
        if (!scene.Contains("OneHouse")) return;
        if (Object.FindFirstObjectByType<SnowSizeDiagnostics>() != null) return;
        var cam = Camera.main;
        var go = cam != null ? cam.gameObject : new GameObject("SnowSizeDiagnostics");
        go.AddComponent<SnowSizeDiagnostics>();
        Debug.Log("[SnowSizeDiagnostics] auto-added to OneHouse scene");
    }
    [Tooltip("可視化の持続時間（秒）。0=永続表示")]
    public float gizmoDuration = 0f;
    [Tooltip("ワイヤー太さ")]
    public float wireThickness = 0.02f;

    static bool _loggedOnce;
    static Bounds _roofRendererBounds;
    static Bounds _roofColliderBounds;
    static Bounds _snowSpawnAreaBounds;
    static Bounds _visibleSnowBounds;
    static bool _hasData;
    static float _lastDrawTime;
    static string _rootCause = "?";
    static string _rootCauseDetail = "";

    void Start()
    {
        string scene = SceneManager.GetActiveScene().name ?? "";
        if (!enableDiagnostics || !scene.Contains("OneHouse")) return;
        Invoke(nameof(CollectAndLog), 0.5f);
    }

    void CollectAndLog()
    {
        if (_loggedOnce) return;

        var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
        if (spawner == null || spawner.roofCollider == null)
        {
            Debug.LogWarning("[SnowSizeDiagnostics] SnowPackSpawner or roofCollider not found");
            return;
        }

        Renderer roofRenderer = spawner.targetSnowRenderer;
        if (roofRenderer == null) roofRenderer = spawner.roofCollider.GetComponent<Renderer>();
        if (roofRenderer == null) roofRenderer = spawner.roofCollider.GetComponentInParent<Renderer>();
        if (roofRenderer == null) roofRenderer = spawner.roofCollider.GetComponentInChildren<Renderer>();

        _roofRendererBounds = roofRenderer != null ? roofRenderer.bounds : new Bounds(spawner.roofCollider.bounds.center, Vector3.zero);
        _roofColliderBounds = spawner.roofCollider.bounds;

        var data = spawner.GetSnowSizeDiagnosticData();
        Vector3 center = data.roofCenter;
        Vector3 R = data.roofR.normalized;
        Vector3 F = data.roofF.normalized;
        float halfW = data.roofWidth * 0.5f;
        float halfL = data.roofLength * 0.5f;

        Vector3 spawnMin = center - R * halfW - F * halfL - data.roofN * 0.01f;
        Vector3 spawnMax = center + R * halfW + F * halfL + data.roofN * 0.08f;
        _snowSpawnAreaBounds = new Bounds((spawnMin + spawnMax) * 0.5f, spawnMax - spawnMin);

        _visibleSnowBounds = new Bounds(center, Vector3.zero);
        bool hasVisible = false;
        if (data.piecesRoot != null)
        {
            for (int i = 0; i < data.piecesRoot.childCount; i++)
            {
                var tr = data.piecesRoot.GetChild(i);
                if (tr == null || !tr.gameObject.activeSelf) continue;
                var r = tr.GetComponentInChildren<Renderer>(true);
                if (r != null && r.enabled)
                {
                    if (!hasVisible) { _visibleSnowBounds = r.bounds; hasVisible = true; }
                    else _visibleSnowBounds.Encapsulate(r.bounds);
                }
            }
        }
        if (!hasVisible) _visibleSnowBounds = _snowSpawnAreaBounds;

        float rw = ProjectedWidth(_roofRendererBounds, R, F);
        float rd = ProjectedDepth(_roofRendererBounds, R, F);
        float cw = ProjectedWidth(_roofColliderBounds, R, F);
        float cd = ProjectedDepth(_roofColliderBounds, R, F);
        float sw = data.roofWidth;
        float sd = data.roofLength;
        float vw = ProjectedWidth(_visibleSnowBounds, R, F);
        float vd = ProjectedDepth(_visibleSnowBounds, R, F);

        Debug.Log($"[RoofRendererBounds] center=({_roofRendererBounds.center.x:F3},{_roofRendererBounds.center.y:F3},{_roofRendererBounds.center.z:F3}) size=({_roofRendererBounds.size.x:F3},{_roofRendererBounds.size.y:F3},{_roofRendererBounds.size.z:F3})");
        Debug.Log($"[RoofColliderBounds] center=({_roofColliderBounds.center.x:F3},{_roofColliderBounds.center.y:F3},{_roofColliderBounds.center.z:F3}) size=({_roofColliderBounds.size.x:F3},{_roofColliderBounds.size.y:F3},{_roofColliderBounds.size.z:F3})");
        Debug.Log($"[SnowSpawnAreaBounds] center=({_snowSpawnAreaBounds.center.x:F3},{_snowSpawnAreaBounds.center.y:F3},{_snowSpawnAreaBounds.center.z:F3}) size=({_snowSpawnAreaBounds.size.x:F3},{_snowSpawnAreaBounds.size.y:F3},{_snowSpawnAreaBounds.size.z:F3})");
        Debug.Log($"[VisibleSnowBounds] center=({_visibleSnowBounds.center.x:F3},{_visibleSnowBounds.center.y:F3},{_visibleSnowBounds.center.z:F3}) size=({_visibleSnowBounds.size.x:F3},{_visibleSnowBounds.size.y:F3},{_visibleSnowBounds.size.z:F3})");

        float rvsSw = rw - sw;
        float rvsSd = rd - sd;
        float rvsVw = rw - vw;
        float rvsVd = rd - vd;
        float cvsSw = cw - sw;
        float cvsSd = cd - sd;
        float cvsVw = cw - vw;
        float cvsVd = cd - vd;

        Debug.Log($"[Diff] renderer_vs_spawn_width={rvsSw:F3} renderer_vs_spawn_depth={rvsSd:F3} renderer_vs_visible_width={rvsVw:F3} renderer_vs_visible_depth={rvsVd:F3} collider_vs_spawn_width={cvsSw:F3} collider_vs_spawn_depth={cvsSd:F3} collider_vs_visible_width={cvsVw:F3} collider_vs_visible_depth={cvsVd:F3}");

        DetermineRootCause(rw, rd, cw, cd, sw, sd, vw, vd, roofRenderer != null, hasVisible);

        Debug.Log($"[RootCause] root_cause={_rootCause} root_cause_detail={_rootCauseDetail}");
        SnowLoopLogCapture.AppendToAssiReport($"=== RoofRendererBounds === center=({_roofRendererBounds.center.x:F3},{_roofRendererBounds.center.y:F3},{_roofRendererBounds.center.z:F3}) size=({_roofRendererBounds.size.x:F3},{_roofRendererBounds.size.y:F3},{_roofRendererBounds.size.z:F3})");
        SnowLoopLogCapture.AppendToAssiReport($"=== RoofColliderBounds === center=({_roofColliderBounds.center.x:F3},{_roofColliderBounds.center.y:F3},{_roofColliderBounds.center.z:F3}) size=({_roofColliderBounds.size.x:F3},{_roofColliderBounds.size.y:F3},{_roofColliderBounds.size.z:F3})");
        SnowLoopLogCapture.AppendToAssiReport($"=== SnowSpawnAreaBounds === center=({_snowSpawnAreaBounds.center.x:F3},{_snowSpawnAreaBounds.center.y:F3},{_snowSpawnAreaBounds.center.z:F3}) size=({_snowSpawnAreaBounds.size.x:F3},{_snowSpawnAreaBounds.size.y:F3},{_snowSpawnAreaBounds.size.z:F3})");
        SnowLoopLogCapture.AppendToAssiReport($"=== VisibleSnowBounds === center=({_visibleSnowBounds.center.x:F3},{_visibleSnowBounds.center.y:F3},{_visibleSnowBounds.center.z:F3}) size=({_visibleSnowBounds.size.x:F3},{_visibleSnowBounds.size.y:F3},{_visibleSnowBounds.size.z:F3})");
        SnowLoopLogCapture.AppendToAssiReport($"=== Diff === renderer_vs_spawn_width={rvsSw:F3} renderer_vs_spawn_depth={rvsSd:F3} renderer_vs_visible_width={rvsVw:F3} renderer_vs_visible_depth={rvsVd:F3} collider_vs_spawn_width={cvsSw:F3} collider_vs_spawn_depth={cvsSd:F3} collider_vs_visible_width={cvsVw:F3} collider_vs_visible_depth={cvsVd:F3}");
        SnowLoopLogCapture.AppendToAssiReport($"=== RootCause === root_cause={_rootCause} root_cause_detail={_rootCauseDetail}");

        _hasData = true;
        _lastDrawTime = Time.time;
        _loggedOnce = true;
    }

    static float ProjectedWidth(Bounds b, Vector3 R, Vector3 F)
    {
        float minR = float.MaxValue, maxR = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = b.center + new Vector3((i & 1) != 0 ? b.extents.x : -b.extents.x, (i & 2) != 0 ? b.extents.y : -b.extents.y, (i & 4) != 0 ? b.extents.z : -b.extents.z);
            float r = Vector3.Dot(c - b.center, R);
            if (r < minR) minR = r;
            if (r > maxR) maxR = r;
        }
        return maxR - minR;
    }

    static float ProjectedDepth(Bounds b, Vector3 R, Vector3 F)
    {
        float minF = float.MaxValue, maxF = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = b.center + new Vector3((i & 1) != 0 ? b.extents.x : -b.extents.x, (i & 2) != 0 ? b.extents.y : -b.extents.y, (i & 4) != 0 ? b.extents.z : -b.extents.z);
            float f = Vector3.Dot(c - b.center, F);
            if (f < minF) minF = f;
            if (f > maxF) maxF = f;
        }
        return maxF - minF;
    }

    static void DetermineRootCause(float rw, float rd, float cw, float cd, float sw, float sd, float vw, float vd, bool hasRenderer, bool hasVisible)
    {
        bool spawnSmall = sw < rw - 0.05f || sd < rd - 0.05f;
        bool spawnVsCollider = Mathf.Abs(sw - cw) > 0.08f || Mathf.Abs(sd - cd) > 0.08f;
        bool visibleSmallVsSpawn = vw < sw - 0.05f || vd < sd - 0.05f;
        bool visibleSmallVsRenderer = vw < rw - 0.05f || vd < rd - 0.05f;
        bool rendererVsCollider = Mathf.Abs(rw - cw) > 0.08f || Mathf.Abs(rd - cd) > 0.08f;

        if (!hasRenderer)
        {
            _rootCause = "A";
            _rootCauseDetail = "Roof renderer not found. Snow uses collider as reference.";
        }
        else if (rendererVsCollider && spawnSmall && Mathf.Abs(sw - cw) < 0.05f)
        {
            _rootCause = "A";
            _rootCauseDetail = "Roof renderer and collider have different sizes. Snow follows collider (or RoofDefinition), not renderer.";
        }
        else if (spawnSmall && !visibleSmallVsSpawn)
        {
            _rootCause = "B";
            _rootCauseDetail = "Snow spawn area is smaller than roof. RoofDefinition/def.width/def.depth or collider projection yields smaller values.";
        }
        else if (visibleSmallVsSpawn)
        {
            _rootCause = "C";
            _rootCauseDetail = "Visible snow bounds are smaller than spawn area. Piece scale/position or mesh size changes after spawn.";
        }
        else if (visibleSmallVsRenderer && !spawnSmall)
        {
            _rootCause = "D";
            _rootCauseDetail = "Visible snow mesh appears smaller. Possible piece visualScale, mesh bounds, or renderer scale.";
        }
        else if (spawnVsCollider)
        {
            _rootCause = "E";
            _rootCauseDetail = "Spawn area differs from collider. Parent scale or RoofDefinition transform may affect.";
        }
        else if (spawnSmall || visibleSmallVsRenderer)
        {
            _rootCause = "F";
            _rootCauseDetail = $"Multiple causes. spawnSmall={spawnSmall} visibleSmallVsSpawn={visibleSmallVsSpawn} visibleSmallVsRenderer={visibleSmallVsRenderer} rendererVsCollider={rendererVsCollider}";
        }
        else
        {
            _rootCause = "OK";
            _rootCauseDetail = "All bounds match within tolerance.";
        }
    }

    void OnDrawGizmos()
    {
        if (!_hasData || !enableDiagnostics) return;
        if (gizmoDuration > 0f && Time.time - _lastDrawTime > gizmoDuration && _lastDrawTime > 0f) return;

        Gizmos.color = Color.red;
        DrawWireBounds(_roofRendererBounds);
        Gizmos.color = Color.green;
        DrawWireBounds(_roofColliderBounds);
        Gizmos.color = Color.yellow;
        DrawWireBounds(_snowSpawnAreaBounds);
        Gizmos.color = Color.blue;
        DrawWireBounds(_visibleSnowBounds);
    }

    void Update()
    {
        if (_hasData && enableDiagnostics && gizmoDuration > 0f)
            _lastDrawTime = Time.time;
        if (!_hasData || !enableDiagnostics) return;
        if (gizmoDuration > 0f && Time.time - _lastDrawTime > gizmoDuration && _lastDrawTime > 0f) return;
        DrawBoundsLines(_roofRendererBounds, Color.red);
        DrawBoundsLines(_roofColliderBounds, Color.green);
        DrawBoundsLines(_snowSpawnAreaBounds, Color.yellow);
        DrawBoundsLines(_visibleSnowBounds, Color.blue);
    }

    static void DrawWireBounds(Bounds b)
    {
        if (b.size.sqrMagnitude < 0.0001f) return;
        Gizmos.DrawWireCube(b.center, b.size);
    }

    static void DrawBoundsLines(Bounds b, Color c)
    {
        if (b.size.sqrMagnitude < 0.0001f) return;
        Vector3 e = b.extents;
        Vector3 c0 = b.center;
        Vector3 p000 = c0 + new Vector3(-e.x, -e.y, -e.z);
        Vector3 p100 = c0 + new Vector3(e.x, -e.y, -e.z);
        Vector3 p010 = c0 + new Vector3(-e.x, e.y, -e.z);
        Vector3 p110 = c0 + new Vector3(e.x, e.y, -e.z);
        Vector3 p001 = c0 + new Vector3(-e.x, -e.y, e.z);
        Vector3 p101 = c0 + new Vector3(e.x, -e.y, e.z);
        Vector3 p011 = c0 + new Vector3(-e.x, e.y, e.z);
        Vector3 p111 = c0 + new Vector3(e.x, e.y, e.z);
        Debug.DrawLine(p000, p100, c, 0.1f);
        Debug.DrawLine(p000, p010, c, 0.1f);
        Debug.DrawLine(p000, p001, c, 0.1f);
        Debug.DrawLine(p100, p110, c, 0.1f);
        Debug.DrawLine(p100, p101, c, 0.1f);
        Debug.DrawLine(p010, p110, c, 0.1f);
        Debug.DrawLine(p010, p011, c, 0.1f);
        Debug.DrawLine(p001, p101, c, 0.1f);
        Debug.DrawLine(p001, p011, c, 0.1f);
        Debug.DrawLine(p110, p111, c, 0.1f);
        Debug.DrawLine(p101, p111, c, 0.1f);
        Debug.DrawLine(p011, p111, c, 0.1f);
    }

}
