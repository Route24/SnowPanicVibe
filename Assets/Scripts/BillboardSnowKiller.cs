using UnityEngine;

/// <summary>
/// Avalanche_Billboard_Test 専用。
/// Play 開始直後に Snow 系 GameObject を全て破棄して
/// 白い板や雪メッシュが出ないようにする。
/// </summary>
public class BillboardSnowKiller : MonoBehaviour
{
    static readonly string[] KillNames = new[]
    {
        "SnowPackVisual", "SnowPackStateIndicator", "SnowPackPiecesRoot",
        "SnowPackAnchor", "SnowField", "SnowBase", "RoofSnowPlane",
        "SnowPackSpawner", "RoofSnowSystem", "GroundSnowSystem",
        "SnowFallSystem", "CorniceRuntimeSnow", "SnowMvpBootstrap",
        "SnowLoopLogCapture", "RunStructureManager", "RunHUDUI",
        "AvalancheFeedback", "SnowPhysicsScoreManager", "ToolCooldownManager",
        "UIBootstrap", "UnifiedHUD", "SnowScoreDisplayUI", "RunResultUI",
    };

    void Awake()
    {
        // DontDestroyOnLoad 含む全シーンの対象 GO を破棄
        foreach (var killName in KillNames)
        {
            var found = GameObject.Find(killName);
            if (found != null)
            {
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

        Debug.Log("[BillboardSnowKiller] snow_system_hard_stop=DONE");
    }
}
