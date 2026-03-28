using UnityEngine;

/// <summary>
/// GATE 2 確認用: Play 開始 1 秒後に snow 系 GameObject の存在数と visual/count バインドを出力する。
/// 許可タグ: [SNOW_BASELINE] [SNOW_VISUAL_BIND] [SNOW_COUNT_BIND]
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
            foreach (var kw in keywords)
            {
                if (go.name.Contains(kw))
                {
                    sb.Append($"'{go.name}' parent='{(go.transform.parent != null ? go.transform.parent.name : "ROOT")}' ");
                    totalCount++;
                    break;
                }
            }
        }

        // [SNOW_BASELINE]
        var rss = Object.FindFirstObjectByType<RoofSnowSystem>();
        var sps = Object.FindFirstObjectByType<SnowPackSpawner>();
        Debug.Log(
            $"[SNOW_BASELINE] " +
            $"snow_system_present={(rss != null || sps != null ? "YES" : "NO")} " +
            $"runtime_snow_count={totalCount} " +
            $"gate2_result={(totalCount > 0 ? "PASS" : "FAIL")} " +
            $"objects={sb}"
        );

        // [SNOW_VISUAL_BIND] — RoofSnowLayer の visual 状態
        var roofLayer = GameObject.Find("RoofSnowLayer");
        if (roofLayer != null)
        {
            var rend = roofLayer.GetComponent<Renderer>();
            var mf = roofLayer.GetComponent<MeshFilter>();
            Vector3 lr = roofLayer.transform.localEulerAngles;
            Debug.Log(
                $"[SNOW_VISUAL_BIND] " +
                $"visual_object_name={roofLayer.name} " +
                $"visual_parent={(roofLayer.transform.parent != null ? roofLayer.transform.parent.name : "ROOT")} " +
                $"mesh={(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "NULL")} " +
                $"material={(rend != null && rend.sharedMaterial != null ? rend.sharedMaterial.name : "NULL")} " +
                $"renderer_enabled={(rend != null ? rend.enabled.ToString() : "N/A")} " +
                $"localEuler=({lr.x:F1},{lr.y:F1},{lr.z:F1})"
            );
        }
        else
        {
            Debug.Log("[SNOW_VISUAL_BIND] RoofSnowLayer=NOT_FOUND");
        }

        // [SNOW_COUNT_BIND] — SnowPackPiecesRoot の childCount が実際の雪ピース数
        var piecesRoot = GameObject.Find("SnowPackPiecesRoot");
        int pieceCount = piecesRoot != null ? piecesRoot.transform.childCount : -1;
        var sps2 = Object.FindFirstObjectByType<SnowPackSpawner>();
        Debug.Log(
            $"[SNOW_COUNT_BIND] " +
            $"count_source=SnowPackPiecesRoot.childCount " +
            $"current_snow_piece_count={pieceCount} " +
            $"SnowPackPiecesRoot_exists={(piecesRoot != null ? "YES" : "NO")} " +
            $"SnowPackSpawner_exists={(sps2 != null ? "YES" : "NO")} " +
            $"snow_registered={(pieceCount > 0 ? "YES" : "NO")}"
        );
    }
}
