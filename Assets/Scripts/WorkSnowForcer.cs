using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// WORK_SNOW シーン専用。
/// Play 開始時に RoofCalibrationData.json から屋根中心座標を計算し、
/// 各屋根の上に白い雪キューブを直接配置して「見える雪」を実現する。
///
/// 落下・地面・SnowPackSpawner・GroundSnowSystem には一切触らない。
/// </summary>
public class WorkSnowForcer : MonoBehaviour
{
    // ── 定数 ──────────────────────────────────────────────────
    const string SCENE_NAME   = "Avalanche_Billboard__WORK_SNOW";
    const string CALIB_PATH   = "Assets/Art/RoofCalibrationData.json";
    const int    PIECES_X     = 6;   // 横方向キューブ数
    const int    PIECES_Z     = 4;   // 奥行きキューブ数
    const float  PIECE_SIZE   = 0.22f;
    const float  PIECE_GAP    = 0.04f;
    const float  ABOVE_ROOF   = 0.12f; // 屋根面より上に浮かせるオフセット

    static readonly string[] RoofIds =
        { "Roof_TL", "Roof_TM", "Roof_TR", "Roof_BL", "Roof_BM", "Roof_BR" };

    // ── JSON デシリアライズ用 ──────────────────────────────────
    [System.Serializable] class V2 { public float x, y; }
    [System.Serializable] class RoofEntry
    {
        public string id;
        public V2 topLeft, topRight, bottomRight, bottomLeft;
        public bool confirmed;
    }
    [System.Serializable] class SaveData { public RoofEntry[] roofs; }

    // ── Bootstrap ─────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying) return;

        // WORK_SNOW シーン以外では何もしない
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene != SCENE_NAME) return;

        var go = new GameObject("WorkSnowForcer");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<WorkSnowForcer>();
        Debug.Log($"[WORK_SNOW_FORCE_VISIBLE] bootstrap scene={scene}");
    }

    IEnumerator Start()
    {
        // BackgroundImage の Start が完了するまで 1 フレーム待つ
        yield return null;
        PlaceSnow();
    }

    void PlaceSnow()
    {
        // ── BackgroundImage のワールド座標を取得 ──────────────
        var bgGo = GameObject.Find("BackgroundImage");
        if (bgGo == null)
        {
            Debug.LogWarning("[WORK_SNOW_FORCE_VISIBLE] BackgroundImage not found");
            return;
        }

        var t = bgGo.transform;
        Vector3 wTL = t.TransformPoint(new Vector3(-0.5f,  0.5f, 0f));
        Vector3 wTR = t.TransformPoint(new Vector3( 0.5f,  0.5f, 0f));
        Vector3 wBL = t.TransformPoint(new Vector3(-0.5f, -0.5f, 0f));
        Vector3 wBR = t.TransformPoint(new Vector3( 0.5f, -0.5f, 0f));

        System.Func<Vector2, Vector3> normToWorld = (n) =>
        {
            Vector3 top    = Vector3.Lerp(wTL, wTR, n.x);
            Vector3 bottom = Vector3.Lerp(wBL, wBR, n.x);
            return Vector3.Lerp(top, bottom, n.y);
        };

        // ── JSON 読み込み ─────────────────────────────────────
        if (!File.Exists(CALIB_PATH))
        {
            Debug.LogWarning($"[WORK_SNOW_FORCE_VISIBLE] calib file not found path={CALIB_PATH}");
            return;
        }
        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(CALIB_PATH));
        if (sd == null || sd.roofs == null)
        {
            Debug.LogWarning("[WORK_SNOW_FORCE_VISIBLE] calib JSON parse failed");
            return;
        }

        // ── 雪マテリアル（白・不透明）────────────────────────
        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.92f, 0.96f, 1.0f, 1f);
        mat.SetFloat("_Glossiness", 0.1f);

        // ── 各屋根に雪を配置 ──────────────────────────────────
        int visibleCount = 0;
        var sb = new System.Text.StringBuilder();

        foreach (var roofId in RoofIds)
        {
            RoofEntry entry = null;
            foreach (var r in sd.roofs)
                if (r.id == roofId) { entry = r; break; }

            if (entry == null || !entry.confirmed)
            {
                string log = $"[WORK_SNOW_FORCE_VISIBLE] roof={roofId} visible=NO reason=no_calib_data";
                Debug.LogWarning(log);
                sb.AppendLine(log);
                continue;
            }

            // 4点の中心をワールド座標で計算
            Vector3 wPtTL = normToWorld(new Vector2(entry.topLeft.x,     entry.topLeft.y));
            Vector3 wPtTR = normToWorld(new Vector2(entry.topRight.x,    entry.topRight.y));
            Vector3 wPtBR = normToWorld(new Vector2(entry.bottomRight.x, entry.bottomRight.y));
            Vector3 wPtBL = normToWorld(new Vector2(entry.bottomLeft.x,  entry.bottomLeft.y));

            Vector3 center = (wPtTL + wPtTR + wPtBR + wPtBL) * 0.25f;

            // 横方向・奥行き方向の単位ベクトル
            Vector3 rightDir = (wPtTR - wPtTL).normalized;
            Vector3 downDir  = (wPtBL - wPtTL).normalized;
            float   roofW    = Vector3.Distance(wPtTL, wPtTR);
            float   roofD    = Vector3.Distance(wPtTL, wPtBL);

            // 屋根の法線（手前向き）
            Vector3 roofNormal = Vector3.Cross(rightDir, downDir).normalized;
            if (Vector3.Dot(roofNormal, Vector3.forward) < 0f) roofNormal = -roofNormal;

            // 雪の親オブジェクト
            var roofRoot = new GameObject($"WorkSnow_{roofId}");
            roofRoot.transform.position = center;

            // グリッド配置
            float stepX = (PIECE_SIZE + PIECE_GAP);
            float stepZ = (PIECE_SIZE + PIECE_GAP);
            float totalW = PIECES_X * stepX - PIECE_GAP;
            float totalD = PIECES_Z * stepZ - PIECE_GAP;

            int placed = 0;
            for (int iz = 0; iz < PIECES_Z; iz++)
            {
                for (int ix = 0; ix < PIECES_X; ix++)
                {
                    // 屋根面上の相対位置（-0.5〜0.5 の範囲に収める）
                    float u = (ix + 0.5f) / PIECES_X - 0.5f;
                    float v = (iz + 0.5f) / PIECES_Z - 0.5f;

                    // 屋根面上のワールド座標
                    Vector3 pos = center
                        + rightDir * (u * roofW * 0.85f)
                        + downDir  * (v * roofD * 0.85f)
                        + roofNormal * ABOVE_ROOF;

                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = $"SnowPiece_{roofId}_{ix}_{iz}";
                    cube.transform.SetParent(roofRoot.transform, true);
                    cube.transform.position = pos;
                    cube.transform.localScale = new Vector3(PIECE_SIZE, PIECE_SIZE * 0.5f, PIECE_SIZE);

                    // コライダーを無効化（雪テスト用なので物理不要）
                    var col = cube.GetComponent<Collider>();
                    if (col != null) col.enabled = false;

                    var mr = cube.GetComponent<MeshRenderer>();
                    if (mr != null) mr.sharedMaterial = mat;

                    placed++;
                }
            }

            visibleCount++;
            string okLog = $"[WORK_SNOW_FORCE_VISIBLE] roof={roofId} visible=YES pieces={placed} center=({center.x:F2},{center.y:F2},{center.z:F2})";
            Debug.Log(okLog);
            sb.AppendLine(okLog);
        }

        bool all6 = visibleCount == 6;
        string summary = $"[WORK_SNOW_FORCE_VISIBLE] done visible_count={visibleCount}/6 all_6_visible={(all6 ? "YES" : "NO")}";
        Debug.Log(summary);
        sb.AppendLine(summary);

        SnowLoopLogCapture.AppendToAssiReport(
            "=== WORK_SNOW VISIBLE SNOW ONLY ===\n" +
            $"scene_name={SCENE_NAME}\n" +
            $"all_6_visible={(all6 ? "YES" : "NO")}\n" +
            sb.ToString());
    }
}
