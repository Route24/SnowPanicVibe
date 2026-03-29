using UnityEngine;

/// <summary>
/// SNOW DESTROY TRACE – snow piece を消すシステムを全停止し、原因を特定する。
///
/// Play 開始時に自動起動。以下を無効化する:
///   BillboardSnowKiller (Update の KillAll を停止)
///   RoofSnowCleanup
///   GroundSnowAccumulator / GroundSnowSystem / GroundSnowPile
///   RollbackVerification
///
/// また ForceSnow_* / MinimalSnow_* を KillNames から守る。
/// </summary>
[DefaultExecutionOrder(-31999)]  // BillboardSnowKiller(-32000) の直後に実行
public class SnowDestroyTracer : MonoBehaviour
{
    static bool _applied = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "SnowCore_AntiProtocol") return;
        var go = new GameObject("SnowDestroyTracer");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<SnowDestroyTracer>();
        Debug.Log("[SNOW_DESTROY_TRACE] SnowDestroyTracer ready – will disable killer components on first frame");
    }

    void Start()
    {
        ApplyDisable();
    }

    // BillboardSnowKiller が Awake/Update で動く可能性があるため
    // Start + Update 両方で適用する
    void Update()
    {
        if (!_applied) ApplyDisable();
        // ForceSnow_* / MinimalSnow_* が y < -10 に落ちていないか監視
        CheckFallOut();
    }

    static void ApplyDisable()
    {
        _applied = true;

        // ── BillboardSnowKiller: Update を止める ─────────────
        foreach (var c in Object.FindObjectsByType<BillboardSnowKiller>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c == null) continue;
            c.enabled = false;
            Debug.Log($"[SNOW_DESTROY_TRACE] killer_component_disabled=BillboardSnowKiller go={c.gameObject.name}");
        }

        // ── RoofSnowCleanup ──────────────────────────────────
        foreach (var c in Object.FindObjectsByType<RoofSnowCleanup>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c == null) continue;
            c.enabled = false;
            Debug.Log($"[SNOW_DESTROY_TRACE] cleanup_disabled=RoofSnowCleanup go={c.gameObject.name}");
        }

        // ── GroundSnowAccumulator ────────────────────────────
        foreach (var c in Object.FindObjectsByType<GroundSnowAccumulator>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c == null) continue;
            c.enabled = false;
            Debug.Log($"[SNOW_DESTROY_TRACE] accumulation_disabled=GroundSnowAccumulator go={c.gameObject.name}");
        }

        // ── GroundSnowSystem ─────────────────────────────────
        foreach (var c in Object.FindObjectsByType<GroundSnowSystem>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c == null) continue;
            c.enabled = false;
            Debug.Log($"[SNOW_DESTROY_TRACE] accumulation_disabled=GroundSnowSystem go={c.gameObject.name}");
        }

        // ── GroundSnowPile ───────────────────────────────────
        foreach (var c in Object.FindObjectsByType<GroundSnowPile>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c == null) continue;
            c.enabled = false;
            Debug.Log($"[SNOW_DESTROY_TRACE] accumulation_disabled=GroundSnowPile go={c.gameObject.name}");
        }

        // ── RollbackVerification ─────────────────────────────
        foreach (var c in Object.FindObjectsByType<RollbackVerification>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c == null) continue;
            c.enabled = false;
            Debug.Log($"[SNOW_DESTROY_TRACE] rollback_disabled=RollbackVerification go={c.gameObject.name}");
        }

        // ── 結果サマリ ───────────────────────────────────────
        Debug.Log("[SNOW_DESTROY_TRACE] all_killer_components_disabled=YES");

        SnowLoopLogCapture.AppendToAssiReport(
            "=== SNOW DESTROY TRACE ===\n" +
            "killer_component_disabled=YES\n" +
            "accumulation_disabled=YES\n" +
            "cleanup_disabled=YES\n" +
            "BillboardSnowKiller=disabled\n" +
            "RoofSnowCleanup=disabled\n" +
            "GroundSnowAccumulator=disabled\n" +
            "GroundSnowSystem=disabled\n" +
            "GroundSnowPile=disabled\n" +
            "RollbackVerification=disabled");
    }

    static void CheckFallOut()
    {
        // ForceSnow_* と MinimalSnow_* が画面外に落ちていないか監視
        var allGos = Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var t in allGos)
        {
            if (t == null) continue;
            string n = t.gameObject.name;
            if (!n.StartsWith("ForceSnow_") && !n.StartsWith("MinimalSnow_")) continue;
            if (t.position.y < -10f)
            {
                Debug.Log($"[SNOW_FALL_OUT] name={n} pos=({t.position.x:F2},{t.position.y:F2},{t.position.z:F2})");
            }
        }
    }
}
