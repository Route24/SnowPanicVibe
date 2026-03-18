using UnityEngine;

/// <summary>
/// Avalanche_Billboard_Test 専用。
/// Script Execution Order = -32000 で最優先実行。
/// Awake で Snow 系 GO / コンポーネントを全破棄し、
/// Update でも毎フレーム RoofSnowLayer を監視・即破棄する。
/// </summary>
[DefaultExecutionOrder(-32000)]
public class BillboardSnowKiller : MonoBehaviour
{
    static readonly string[] KillNames = new[]
    {
        "SnowPackVisual", "SnowPackStateIndicator", "SnowPackPiecesRoot",
        "SnowPackAnchor", "SnowField", "SnowBase", "RoofSnowPlane",
        "RoofSnowLayer",  // RoofSnowSystem が生成する白い板
        "SnowPackSpawner", "RoofSnowSystem", "GroundSnowSystem",
        "SnowFallSystem", "CorniceRuntimeSnow", "SnowMvpBootstrap",
        "SnowLoopLogCapture", "RunStructureManager", "RunHUDUI",
        "AvalancheFeedback", "SnowPhysicsScoreManager", "ToolCooldownManager",
        "UIBootstrap", "UnifiedHUD", "SnowScoreDisplayUI", "RunResultUI",
        "SnowPackPiecesRoot", "SnowPackAnchor",
    };

    void Awake()
    {
        // WORK_SNOW シーンでは雪ゲームシステムを使うため KillAll しない
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene.Contains("WORK_SNOW")) return;
        KillAll();
    }

    // [SNOW_DESTROY_TRACE] Update の KillAll は SnowDestroyTracer により停止済み
    // ForceSnow_ / MinimalSnow_ を誤って破棄しないよう Update を無効化
    void Update()
    {
        // KillAll() は呼ばない（SnowDestroyTracer が enabled=false にする）
        Debug.Log("[SNOW_DESTROY_TRACE] BillboardSnowKiller.Update suppressed by SnowDestroyTracer");
    }

    void KillAll()
    {
        foreach (var killName in KillNames)
        {
            var found = GameObject.Find(killName);
            if (found != null)
            {
                Debug.Log($"[WHITE_PANEL_SOURCE] object_name={found.name} component_name=MeshRenderer script_name=RoofSnowSystem created_at_runtime=YES callsite=EnsureRoofVisual");
                Debug.Log($"[BillboardSnowKiller] destroyed={killName}");
                Destroy(found);
            }
        }

        // SnowPackSpawner コンポーネントを持つ全 GO を破棄
        foreach (var spawner in FindObjectsByType<SnowPackSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (spawner != null)
            {
                Debug.Log($"[BillboardSnowKiller] destroyed_spawner={spawner.gameObject.name}");
                Destroy(spawner.gameObject);
            }
        }

        // RoofSnowSystem を持つ全 GO を破棄
        foreach (var rss in FindObjectsByType<RoofSnowSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (rss != null)
            {
                Debug.Log($"[BillboardSnowKiller] destroyed_rss={rss.gameObject.name}");
                Destroy(rss.gameObject);
            }
        }
    }
}
