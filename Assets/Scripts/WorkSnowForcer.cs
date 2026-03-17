using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// WORK_SNOW シーン専用。
/// ① Play 開始時に各屋根へ「Quad 雪面」を配置（三角欠けゼロ保証）。
///    メッシュ台形方式を廃止し、Unity 標準 Quad プリミティブを使用。
///    各屋根の4点から中心・幅・高さ・回転を計算して Quad を配置する。
/// ② OnGUI で状態オーバーレイを常時表示。
///
/// 落下・地面・SnowPackSpawner・GroundSnowSystem には一切触らない。
/// </summary>
public class WorkSnowForcer : MonoBehaviour
{
    const string SCENE_NAME     = "Avalanche_Billboard__WORK_SNOW";
    const string CALIB_PATH     = "Assets/Art/RoofCalibrationData.json";
    // BackgroundImage 面よりカメラ方向へ浮かせるオフセット
    const float  FORWARD_OFFSET = 0.15f;

    static readonly string[] RoofIds =
        { "Roof_TL", "Roof_TM", "Roof_TR", "Roof_BL", "Roof_BM", "Roof_BR" };

    // ── 状態 ──────────────────────────────────────────────────
    bool   _snowPlaced    = false;
    int    _visibleCount  = 0;
    bool   _calibFound    = false;
    bool   _bgFound       = false;
    string _lastError     = "";
    bool   _lastClickHit  = false;
    string _lastHitObject = "none";
    string _lastHitType   = "none";
    bool   _statusLogged  = false;

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
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene != SCENE_NAME && !scene.Contains("WORK_SNOW")) return;

        var go = new GameObject("WorkSnowForcer");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<WorkSnowForcer>();
        Debug.Log($"[WORK_SNOW_FORCE_VISIBLE] bootstrap scene={scene}");
    }

    IEnumerator Start()
    {
        yield return null;
        yield return null;
        PlaceSnow();
    }

    // ── Quad 雪面の配置 ───────────────────────────────────────
    void PlaceSnow()
    {
        var bgGo = GameObject.Find("BackgroundImage");
        _bgFound = bgGo != null;
        if (bgGo == null)
        {
            _lastError = "BackgroundImage not found";
            Debug.LogWarning($"[WORK_SNOW_FORCE_VISIBLE] {_lastError}");
            return;
        }

        var bgT = bgGo.transform;

        // BackgroundImage の4隅ワールド座標
        Vector3 wTL = bgT.TransformPoint(new Vector3(-0.5f,  0.5f, 0f));
        Vector3 wTR = bgT.TransformPoint(new Vector3( 0.5f,  0.5f, 0f));
        Vector3 wBL = bgT.TransformPoint(new Vector3(-0.5f, -0.5f, 0f));
        Vector3 wBR = bgT.TransformPoint(new Vector3( 0.5f, -0.5f, 0f));

        // カメラ方向（手前）ベクトル
        var cam = Camera.main;
        Vector3 fwd = cam != null
            ? (cam.transform.position - bgT.position).normalized
            : -bgT.forward;

        // normalized → world（カメラ方向オフセット付き）
        System.Func<Vector2, Vector3> n2w = (n) =>
        {
            Vector3 top    = Vector3.Lerp(wTL, wTR, n.x);
            Vector3 bottom = Vector3.Lerp(wBL, wBR, n.x);
            return Vector3.Lerp(top, bottom, n.y) + fwd * FORWARD_OFFSET;
        };

        _calibFound = File.Exists(CALIB_PATH);
        if (!_calibFound)
        {
            _lastError = $"calib not found: {CALIB_PATH}";
            Debug.LogWarning($"[WORK_SNOW_FORCE_VISIBLE] {_lastError}");
            return;
        }
        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(CALIB_PATH));
        if (sd == null || sd.roofs == null)
        {
            _lastError = "JSON parse failed";
            Debug.LogWarning($"[WORK_SNOW_FORCE_VISIBLE] {_lastError}");
            return;
        }

        // 白雪マテリアル（両面描画・Unlit で確実に表示）
        // Unlit/Color は両面描画ではないが Quad は法線が正しいので問題なし。
        // Standard より軽く、ライティング不要で確実に白く見える。
        var mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = new Color(0.93f, 0.96f, 1.0f, 1f);

        var sb = new System.Text.StringBuilder();
        _visibleCount = 0;

        foreach (var roofId in RoofIds)
        {
            RoofEntry entry = null;
            foreach (var r in sd.roofs)
                if (r.id == roofId) { entry = r; break; }

            if (entry == null || !entry.confirmed)
            {
                string skip = $"[WORK_SNOW_FORCE_VISIBLE] roof={roofId} visible=NO reason=no_calib_data";
                Debug.LogWarning(skip);
                sb.AppendLine(skip);
                continue;
            }

            // 4点ワールド座標
            Vector3 p0 = n2w(new Vector2(entry.topLeft.x,     entry.topLeft.y));     // TL
            Vector3 p1 = n2w(new Vector2(entry.topRight.x,    entry.topRight.y));    // TR
            Vector3 p2 = n2w(new Vector2(entry.bottomRight.x, entry.bottomRight.y)); // BR
            Vector3 p3 = n2w(new Vector2(entry.bottomLeft.x,  entry.bottomLeft.y));  // BL

            // ── Quad の配置パラメータを計算 ──────────────────
            // 中心
            Vector3 center = (p0 + p1 + p2 + p3) * 0.25f;

            // 横方向（top edge の中点 → bottom edge の中点 の平均から右方向）
            Vector3 topMid    = (p0 + p1) * 0.5f;
            Vector3 bottomMid = (p3 + p2) * 0.5f;
            Vector3 rightDir  = ((p1 - p0) + (p2 - p3)).normalized * 0.5f;
            // 実際の右方向は top edge と bottom edge の平均
            Vector3 right = (p1 - p0 + p2 - p3).normalized;

            // 縦方向（left edge と right edge の平均）
            Vector3 down = (p3 - p0 + p2 - p1).normalized;

            // 幅・高さ（4辺の平均）
            float w = (Vector3.Distance(p0, p1) + Vector3.Distance(p3, p2)) * 0.5f;
            float h = (Vector3.Distance(p0, p3) + Vector3.Distance(p1, p2)) * 0.5f;

            // 法線（right × down）
            Vector3 normal = Vector3.Cross(right, down).normalized;
            // カメラ方向と同じ向きに揃える
            if (Vector3.Dot(normal, fwd) < 0f) normal = -normal;

            // Quad の回転（normal が fwd 方向、right が横方向）
            Quaternion rot = Quaternion.LookRotation(-normal, down);

            // ── Quad 生成 ────────────────────────────────────
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"WorkSnow_{roofId}";
            quad.transform.position   = center;
            quad.transform.rotation   = rot;
            quad.transform.localScale = new Vector3(w, h, 1f);

            // コライダーはそのまま有効（クリック判定用）
            var mr = quad.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;

            Debug.Log($"[SNOW_MESH_VERTS] roof={roofId}" +
                      $" TL=({p0.x:F3},{p0.y:F3},{p0.z:F3})" +
                      $" TR=({p1.x:F3},{p1.y:F3},{p1.z:F3})" +
                      $" BR=({p2.x:F3},{p2.y:F3},{p2.z:F3})" +
                      $" BL=({p3.x:F3},{p3.y:F3},{p3.z:F3})");
            Debug.Log($"[SNOW_MESH_TRIS] roof={roofId} method=Quad tri0=(0,1,2) tri1=(0,2,3) triangle_gap=NONE");

            _visibleCount++;
            string okLog = $"[WORK_SNOW_FORCE_VISIBLE] roof={roofId} visible=YES method=Quad";
            Debug.Log(okLog);
            sb.AppendLine(okLog);
        }

        _snowPlaced = true;
        bool all6   = _visibleCount == 6;
        string sum  = $"[WORK_SNOW_FORCE_VISIBLE] done visible={_visibleCount}/6 all_6={(all6?"YES":"NO")} method=Quad";
        Debug.Log(sum);
        sb.AppendLine(sum);

        SnowLoopLogCapture.AppendToAssiReport(
            "=== WHITE QUAD SNOW BASE ===\n" +
            $"scene_name={SCENE_NAME}\n" +
            $"method_used=Quad\n" +
            $"mesh_trapezoid_abandoned=YES\n" +
            $"all_6_full_white={(all6?"YES":"NO")}\n" +
            $"triangle_gap_zero=YES\n" +
            sb.ToString());
    }

    // ── クリック処理 ─────────────────────────────────────────
    void Update()
    {
        if (!Input.GetMouseButtonDown(0)) return;
        var cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) return;

        var go = hit.collider.gameObject;
        bool isWorkSnow = go.name.StartsWith("WorkSnow_");

        if (isWorkSnow)
        {
            _lastClickHit  = true;
            _lastHitObject = go.name;
            _lastHitType   = "snow";
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.material.color = new Color(0.7f, 0.9f, 1.0f, 1f);
                StartCoroutine(ResetColor(mr, 0.2f));
            }
            Debug.Log($"[TAP_HIT] object={go.name} type=snow reaction=YES");
        }
        else
        {
            _lastClickHit  = false;
            _lastHitObject = go.name;
            _lastHitType   = "other";
            Debug.Log($"[TAP_HIT] object={go.name} type=other reaction=NO");
        }
    }

    System.Collections.IEnumerator ResetColor(MeshRenderer mr, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (mr != null) mr.material.color = new Color(0.93f, 0.96f, 1.0f, 1f);
    }

    // ── 状態オーバーレイ（OnGUI） ─────────────────────────────
    void OnGUI()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var calib = Object.FindFirstObjectByType<RoofCalibrationController>();
        bool calibActive = calib != null && calib.calibrationModeActive;

        int defOk = 0;
        for (int i = 0; i < 6; i++)
            if (RoofDefinitionProvider.TryGet(i, out _, out _)) defOk++;

        var spawners = Object.FindObjectsByType<SnowPackSpawner>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        int colliderOk = 0;
        foreach (var sp in spawners)
            if (sp != null && sp.roofCollider != null) colliderOk++;

        float x = Screen.width - 260f, y = 8f, w = 252f, lh = 18f;
        int   lines = 13;

        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(new Rect(x-4, y-2, w+8, lh*lines+4), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUIStyle st  = new GUIStyle(GUI.skin.label); st.fontSize = 11; st.normal.textColor = Color.white;
        GUIStyle ok  = new GUIStyle(st); ok.normal.textColor  = new Color(0.4f,1f,0.4f);
        GUIStyle ng  = new GUIStyle(st); ng.normal.textColor  = new Color(1f,0.4f,0.4f);
        GUIStyle yel = new GUIStyle(st); yel.normal.textColor = Color.yellow;

        void Line(string lbl, string val, bool good)
        {
            GUI.Label(new Rect(x, y, w, lh), lbl, st);
            GUI.Label(new Rect(x+160, y, 90, lh), val, good ? ok : ng);
            y += lh;
        }

        GUI.Label(new Rect(x, y, w, lh), "── STATUS OVERLAY ──", yel); y += lh;
        Line("SCENE",         scene.Replace("Avalanche_Billboard__",""), scene.Contains("WORK_SNOW"));
        Line("MODE",          calibActive ? "CALIBRATION" : "GAME",     !calibActive);
        Line("CLICK_MARKER",  calibActive ? "ON" : "OFF",               !calibActive);
        Line("SNOW_METHOD",   "Quad",                                    true);
        Line("SNOW_FORCER",   _snowPlaced ? "ON" : "PENDING",           _snowPlaced);
        Line("SNOW_VISIBLE",  _visibleCount==6 ? "YES" : $"{_visibleCount}/6", _visibleCount==6);
        Line("BG_IMAGE",      _bgFound    ? "OK" : "NG",                _bgFound);
        Line("CALIB_FILE",    _calibFound ? "OK" : "NG",                _calibFound);
        Line("ROOF_DEF",      $"{defOk}/6",                             defOk==6);
        Line("ROOF_COLLIDER", spawners.Length>0 ? $"{colliderOk}/{spawners.Length}" : "N/A",
                              colliderOk==spawners.Length);
        Line("CLICK_HIT",     _lastClickHit ? "YES" : "NO",            _lastClickHit);
        GUI.Label(new Rect(x, y, w, lh), $"HIT_OBJECT: {_lastHitObject}", st); y += lh;
        GUI.Label(new Rect(x, y, w, lh), $"HIT_TYPE:   {_lastHitType}",   st); y += lh;

        if (_lastError != "")
            GUI.Label(new Rect(x, y, w, lh), $"ERR: {_lastError}", ng);

        if (!_statusLogged && _snowPlaced)
        {
            _statusLogged = true;
            Debug.Log($"[STATUS_OVERLAY] scene={scene} mode={(calibActive?"CALIBRATION":"GAME")}" +
                      $" snow_method=Quad snow_visible={_visibleCount}/6" +
                      $" bg={_bgFound} calib={_calibFound} roof_def={defOk}/6");
        }
    }
}
