using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// WORK_SNOW シーン専用。
/// ① Play 開始時に各屋根の4点キャリブデータから「1枚の台形メッシュ雪面」を生成。
///    タイル方式をやめ、隙間ゼロ・屋根ぴったりの白い雪面を実現する。
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
    // 雪面の厚み（MeshCollider 用に少し厚くする）
    const float  SNOW_THICKNESS = 0.04f;

    static readonly string[] RoofIds =
        { "Roof_TL", "Roof_TM", "Roof_TR", "Roof_BL", "Roof_BM", "Roof_BR" };

    // ── 状態（オーバーレイ表示用） ────────────────────────────
    bool   _snowPlaced    = false;
    int    _visibleCount  = 0;
    bool   _calibFound    = false;
    bool   _bgFound       = false;
    string _lastError     = "";

    // ── クリック状態 ──────────────────────────────────────────
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

    // ── 台形メッシュ雪面の生成 ────────────────────────────────
    void PlaceSnow()
    {
        // BackgroundImage のワールド4隅を取得
        var bgGo = GameObject.Find("BackgroundImage");
        _bgFound = bgGo != null;
        if (bgGo == null)
        {
            _lastError = "BackgroundImage not found";
            Debug.LogWarning($"[WORK_SNOW_FORCE_VISIBLE] {_lastError}");
            return;
        }

        var t   = bgGo.transform;
        Vector3 wTL = t.TransformPoint(new Vector3(-0.5f,  0.5f, 0f));
        Vector3 wTR = t.TransformPoint(new Vector3( 0.5f,  0.5f, 0f));
        Vector3 wBL = t.TransformPoint(new Vector3(-0.5f, -0.5f, 0f));
        Vector3 wBR = t.TransformPoint(new Vector3( 0.5f, -0.5f, 0f));

        // カメラ方向（手前）ベクトル
        var cam    = Camera.main;
        Vector3 fwd = cam != null
            ? (cam.transform.position - t.position).normalized
            : -t.forward;

        // normalized → world（カメラ方向オフセット付き）
        System.Func<Vector2, Vector3> n2w = (n) =>
        {
            Vector3 top    = Vector3.Lerp(wTL, wTR, n.x);
            Vector3 bottom = Vector3.Lerp(wBL, wBR, n.x);
            return Vector3.Lerp(top, bottom, n.y) + fwd * FORWARD_OFFSET;
        };

        // JSON 読み込み
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

        // 白雪マテリアル（SnowPackSpawner と同色）
        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.93f, 0.96f, 1.0f, 1f);
        mat.SetFloat("_Glossiness", 0.08f);
        mat.SetFloat("_Metallic",   0f);

        var sb = new System.Text.StringBuilder();
        _visibleCount = 0;

        foreach (var roofId in RoofIds)
        {
            // JSON からエントリ検索
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

            // 4点をワールド座標に変換
            Vector3 p0 = n2w(new Vector2(entry.topLeft.x,     entry.topLeft.y));     // TL
            Vector3 p1 = n2w(new Vector2(entry.topRight.x,    entry.topRight.y));    // TR
            Vector3 p2 = n2w(new Vector2(entry.bottomRight.x, entry.bottomRight.y)); // BR
            Vector3 p3 = n2w(new Vector2(entry.bottomLeft.x,  entry.bottomLeft.y));  // BL

            // 台形メッシュを生成して GameObject に付ける
            var snowGo = BuildTrapezoidSnow(roofId, p0, p1, p2, p3, mat, fwd);

            _visibleCount++;
            string okLog = $"[WORK_SNOW_FORCE_VISIBLE] roof={roofId} visible=YES trapezoid=YES";
            Debug.Log(okLog);
            sb.AppendLine(okLog);
        }

        _snowPlaced = true;
        bool all6   = _visibleCount == 6;
        string sum  = $"[WORK_SNOW_FORCE_VISIBLE] done visible={_visibleCount}/6 all_6_visible={(all6?"YES":"NO")}";
        Debug.Log(sum);
        sb.AppendLine(sum);

        SnowLoopLogCapture.AppendToAssiReport(
            "=== WHITE TRAPEZOID SNOW BASE ===\n" +
            $"scene_name={SCENE_NAME}\n" +
            $"all_6_fully_covered={(all6?"YES":"NO")}\n" +
            sb.ToString());
    }

    /// <summary>
    /// 4点（TL/TR/BR/BL）から台形メッシュを生成し、
    /// MeshFilter + MeshRenderer + MeshCollider を持つ GameObject を返す。
    /// </summary>
    GameObject BuildTrapezoidSnow(
        string roofId,
        Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
        Material mat, Vector3 fwd)
    {
        var go = new GameObject($"WorkSnow_{roofId}");

        // ── メッシュ構築 ──────────────────────────────────────
        // 表面（カメラ側）: p0=TL, p1=TR, p2=BR, p3=BL
        // 裏面（奥側）:     p0-p3 を fwd 方向に SNOW_THICKNESS 分ずらす
        Vector3 q0 = p0 - fwd * SNOW_THICKNESS;
        Vector3 q1 = p1 - fwd * SNOW_THICKNESS;
        Vector3 q2 = p2 - fwd * SNOW_THICKNESS;
        Vector3 q3 = p3 - fwd * SNOW_THICKNESS;

        var mesh = new Mesh();
        mesh.name = $"SnowMesh_{roofId}";

        // 頂点: 表面4点 + 裏面4点
        mesh.vertices = new Vector3[]
        {
            p0, p1, p2, p3,   // 0-3: 表面 TL,TR,BR,BL
            q0, q1, q2, q3,   // 4-7: 裏面
        };

        // UV（表面のみ）
        mesh.uv = new Vector2[]
        {
            new Vector2(0,1), new Vector2(1,1), new Vector2(1,0), new Vector2(0,0),
            new Vector2(0,1), new Vector2(1,1), new Vector2(1,0), new Vector2(0,0),
        };

        // 三角形（表面・裏面・4側面）
        mesh.triangles = new int[]
        {
            // 表面（手前）
            0,1,2,  0,2,3,
            // 裏面（奥）
            4,6,5,  4,7,6,
            // 上辺（TL-TR）
            0,4,1,  1,4,5,
            // 右辺（TR-BR）
            1,5,2,  2,5,6,
            // 下辺（BR-BL）
            2,6,3,  3,6,7,
            // 左辺（BL-TL）
            3,7,0,  0,7,4,
        };

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        go.AddComponent<MeshFilter>().mesh    = mesh;
        go.AddComponent<MeshRenderer>().material = mat;

        // MeshCollider（クリック判定用）
        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;

        // 識別用タグ
        go.name = $"WorkSnow_{roofId}";

        return go;
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
            // 色を一瞬変えて反応を示す（0.2秒後に白に戻す）
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
        Line("SCENE",          scene.Replace("Avalanche_Billboard__",""), scene.Contains("WORK_SNOW"));
        Line("MODE",           calibActive ? "CALIBRATION" : "GAME",     !calibActive);
        Line("CLICK_MARKER",   calibActive ? "ON" : "OFF",               !calibActive);
        Line("SNOW_FORCER",    _snowPlaced ? "ON" : "PENDING",           _snowPlaced);
        Line("SNOW_VISIBLE",   _visibleCount==6 ? "YES" : $"{_visibleCount}/6", _visibleCount==6);
        Line("BG_IMAGE",       _bgFound    ? "OK" : "NG",                _bgFound);
        Line("CALIB_FILE",     _calibFound ? "OK" : "NG",                _calibFound);
        Line("ROOF_DEF",       $"{defOk}/6",                             defOk==6);
        Line("ROOF_COLLIDER",  spawners.Length>0 ? $"{colliderOk}/{spawners.Length}" : "N/A",
                               colliderOk==spawners.Length);
        Line("CLICK_HIT",      _lastClickHit ? "YES" : "NO",            _lastClickHit);
        GUI.Label(new Rect(x, y, w, lh), $"HIT_OBJECT: {_lastHitObject}", st); y += lh;
        GUI.Label(new Rect(x, y, w, lh), $"HIT_TYPE:   {_lastHitType}",   st); y += lh;

        if (_lastError != "")
            GUI.Label(new Rect(x, y, w, lh), $"ERR: {_lastError}", ng);

        if (!_statusLogged && _snowPlaced)
        {
            _statusLogged = true;
            Debug.Log($"[STATUS_OVERLAY] scene={scene} mode={(calibActive?"CALIBRATION":"GAME")}" +
                      $" snow_visible={_visibleCount}/6 bg={_bgFound} calib={_calibFound}" +
                      $" roof_def={defOk}/6");
        }
    }
}
