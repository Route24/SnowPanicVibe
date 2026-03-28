using UnityEngine;

/// <summary>
/// GATE 2 確認用: Play 開始 1 秒後に snow 系 GameObject の存在数を Console に出力する。
/// snow_spawn_runtime_exists と runtime_snow_count を確定させるためのスクリプト。
/// </summary>
[DefaultExecutionOrder(9000)]
public class Gate2SnowChecker : MonoBehaviour
{
    void Start()
    {
        Invoke(nameof(CheckSnow), 1.0f);
    }

    void CheckSnow()
    {
        string[] keywords = { "snow", "Snow", "RoofSnow", "SnowPack", "SnowLayer", "SnowVisual" };

        int totalCount = 0;
        var sb = new System.Text.StringBuilder();

        var allGos = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        foreach (var go in allGos)
        {
            string n = go.name;
            foreach (var kw in keywords)
            {
                if (n.Contains(kw))
                {
                    sb.AppendLine($"  found: '{n}' active={go.activeInHierarchy} parent='{(go.transform.parent != null ? go.transform.parent.name : "ROOT")}'");
                    totalCount++;
                    break;
                }
            }
        }

        // RoofSnowSystem の状態も出す
        var rss = Object.FindFirstObjectByType<RoofSnowSystem>();
        string rssState = rss != null
            ? $"enabled={rss.enabled} roofCollider={(rss.roofSlideCollider != null ? rss.roofSlideCollider.gameObject.name : "NULL")} heightmap={rss.heightmap_mode_enabled}"
            : "NOT_FOUND";

        var sps = Object.FindFirstObjectByType<SnowPackSpawner>();
        string spsState = sps != null
            ? $"enabled={sps.enabled} roofCollider={(sps.roofCollider != null ? sps.roofCollider.gameObject.name : "NULL")}"
            : "NOT_FOUND";

        Debug.Log(
            $"[GATE2_SNOW_CHECK]\n" +
            $"snow_system_present={(rss != null || sps != null ? "YES" : "NO")}\n" +
            $"RoofSnowSystem={rssState}\n" +
            $"SnowPackSpawner={spsState}\n" +
            $"runtime_snow_count={totalCount}\n" +
            $"snow_spawn_runtime_exists={(totalCount > 0 ? "YES" : "NO")}\n" +
            $"gate2_result={(totalCount > 0 ? "PASS" : "FAIL")}\n" +
            $"--- snow objects ---\n{(totalCount > 0 ? sb.ToString() : "(none)")}"
        );
    }
}
