using System.Collections;
using UnityEngine;

/// <summary>
/// Play 開始時に全 SnowPackSpawner の packDepthMeters を強制設定して
/// 6軒すべての屋根に初期積雪を表示する。
///
/// 地面停止・落下先・UI には一切触らない。
/// </summary>
public class InitialRoofSnowForcer : MonoBehaviour
{
    // 初期積雪量（目視で明確に見える量）
    const float INITIAL_DEPTH = 0.35f;

    static readonly string[] RoofIds =
        { "Roof_TL", "Roof_TM", "Roof_TR", "Roof_BL", "Roof_BM", "Roof_BR" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying) return;
        // WORK_SNOW シーンでは InitialRoofSnowForcer を起動しない
        // （SnowPackSpawner 経由で 3D Cube が生成され画面中央に白ブロックが出る）
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("WORK_SNOW")) return;
        var go = new GameObject("InitialRoofSnowForcer");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<InitialRoofSnowForcer>();
    }

    IEnumerator Start()
    {
        // SnowPackSpawner の OnEnable/Start が完了するまで 2 フレーム待つ
        yield return null;
        yield return null;

        ForceInitialSnow();
    }

    void ForceInitialSnow()
    {
        var spawners = Object.FindObjectsByType<SnowPackSpawner>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int total = 0;
        int zeroBefore = 0;
        int colliderFixed = 0;
        var sb = new System.Text.StringBuilder();

        foreach (var sp in spawners)
        {
            if (sp == null) continue;

            // roofCollider が null なら houseIndex に対応した屋根コライダーを探して設定する
            // （[SnowPack] roofCollider is not assigned. 警告を防ぐ）
            if (sp.roofCollider == null)
            {
                var resolved = FindRoofColliderForHouse(sp.houseIndex);
                if (resolved != null)
                {
                    sp.roofCollider = resolved;
                    colliderFixed++;
                }
            }

            // packDepthMeters が小さすぎる場合のみ強制設定
            if (sp.packDepthMeters < INITIAL_DEPTH)
            {
                zeroBefore++;
                sp.packDepthMeters = INITIAL_DEPTH;
            }

            sp.RebuildSnowPack("InitialRoofSnowForcer");
            total++;

            int count = sp.GetPackedCubeCountRealtime();
            string roofName = sp.gameObject.name;

            // 親を辿って Roof_XX 名を探す
            var t = sp.transform;
            for (int d = 0; d < 5; d++)
            {
                if (t == null) break;
                foreach (var id in RoofIds)
                    if (t.name.Contains(id)) { roofName = id; break; }
                t = t.parent;
            }

            bool visible = count > 0;
            bool colliderOk = sp.roofCollider != null;
            bool defOk = RoofDefinitionProvider.TryGet(sp.houseIndex, out _, out _);

            string log = $"[SNOW_VISIBLE_CHECK] roof={roofName} count={count} visible={(visible ? "YES" : "NO")}"
                       + $" roofColliderAssigned={(colliderOk ? "YES" : "NO")}"
                       + $" roofDefinitionResolved={(defOk ? "YES" : "NO")}";
            Debug.Log(log);
            sb.AppendLine(log);
        }

        string summary =
            $"[InitialRoofSnowForcer] spawner_count={total} forced_count={zeroBefore}"
            + $" collider_fixed={colliderFixed} initial_depth={INITIAL_DEPTH}";
        Debug.Log(summary);
        sb.AppendLine(summary);

        SnowLoopLogCapture.AppendToAssiReport(
            "=== INITIAL ROOF SNOW RESTORE ===\n" +
            $"all_roofs_targeted={total}\n" +
            $"depth_forced_count={zeroBefore}\n" +
            $"collider_fixed_count={colliderFixed}\n" +
            $"initial_depth={INITIAL_DEPTH}\n" +
            sb.ToString());
    }

    /// <summary>
    /// houseIndex に対応した屋根コライダーを Hierarchy から探す。
    /// </summary>
    static Collider FindRoofColliderForHouse(int houseIndex)
    {
        if (houseIndex < 0 || houseIndex >= RoofIds.Length) return null;
        string roofId = RoofIds[houseIndex];

        var allColliders = Object.FindObjectsByType<Collider>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var c in allColliders)
        {
            if (c == null) continue;
            if (c.name.Contains(roofId) ||
                (c.transform.parent != null && c.transform.parent.name.Contains(roofId)))
                return c;
        }

        // フォールバック: "RoofSlideCollider" という名前で探す
        var byName = GameObject.Find("RoofSlideCollider");
        if (byName != null) return byName.GetComponent<Collider>();

        return null;
    }
}
