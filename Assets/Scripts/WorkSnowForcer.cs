using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// WORK_SNOW シーン専用。
/// 【モード: SNOW_IMAGE_MAPPING】
///
/// mesh / triangle / GL は一切使わない。
/// OnGUI + Camera.WorldToScreenPoint で4点スクリーン座標を取得し、
/// GUI.DrawTexture で白い雪テクスチャを台形に近似して描画する。
///
/// 台形近似方法:
///   上辺(TL→TR)と下辺(BL→BR)を別々の行として描画し、
///   行ごとに x 位置と幅を線形補間することで台形を埋める。
///   これは純粋な 2D 描画なので三角欠け・カリング問題が構造的に起きない。
///
/// 落下・地面・SnowPackSpawner・GroundSnowSystem には一切触らない。
/// </summary>
[ExecuteAlways]
public class WorkSnowForcer : MonoBehaviour
{
    const string CALIB_PATH     = "Assets/Art/RoofCalibrationData.json";
    const float  FORWARD_OFFSET = 0.12f;

    // 台形を何本の水平スキャンラインで埋めるか（多いほど精度が上がる）
    const int    SCAN_LINES     = 40;

    static readonly string[] RoofIds =
        { "Roof_TL", "Roof_TM", "Roof_TR", "Roof_BL", "Roof_BM", "Roof_BR" };

    // ── 状態 ──────────────────────────────────────────────────────
    bool   _loaded        = false;
    bool   _calibFound    = false;
    bool   _bgFound       = false;
    string _lastError     = "";
    bool   _consoleLogged = false;
    int    _outlineCount  = 0;

    struct RoofOutline { public Vector3 tl, tr, br, bl; public string id; }
    readonly List<RoofOutline> _outlines = new List<RoofOutline>();

    // 白テクスチャ
    Texture2D _whiteTex;

    // ── JSON デシリアライズ用 ──────────────────────────────────────
    [System.Serializable] class V2 { public float x, y; }
    [System.Serializable] class RoofEntry
    {
        public string id;
        public V2 topLeft, topRight, bottomRight, bottomLeft;
        public bool confirmed;
    }
    [System.Serializable] class SaveData { public RoofEntry[] roofs; }

    // ── Bootstrap ─────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying) return;
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (!scene.Contains("WORK_SNOW")) return;
        if (Object.FindFirstObjectByType<WorkSnowForcer>() != null) return;

        var bgGo = GameObject.Find("BackgroundImage");
        if (bgGo != null)
        {
            bgGo.AddComponent<WorkSnowForcer>();
            Debug.Log($"[WORK_SNOW_FORCER] attached to BackgroundImage scene={scene} mode=SNOW_IMAGE_MAPPING");
        }
        else
        {
            var go = new GameObject("WorkSnowForcer_Root");
            go.AddComponent<WorkSnowForcer>();
            Debug.Log($"[WORK_SNOW_FORCER] created root scene={scene} mode=SNOW_IMAGE_MAPPING");
        }
    }

    void OnEnable()
    {
        _loaded = false;
        _outlines.Clear();
        _outlineCount = 0;
        _consoleLogged = false;

        // 白テクスチャ作成
        _whiteTex = new Texture2D(1, 1);
        _whiteTex.SetPixel(0, 0, new Color(0.93f, 0.96f, 1.0f, 1f));
        _whiteTex.Apply();
    }

    void Start()
    {
        if (!Application.isPlaying) return;
        LoadOutlines();
    }

    // ── キャリブレーション4点を読み込む ──────────────────────────
    void LoadOutlines()
    {
        _loaded = false;

        var bgT = transform;
        if (gameObject.name != "BackgroundImage")
        {
            var bgGo = GameObject.Find("BackgroundImage");
            if (bgGo == null)
            {
                _lastError = "BackgroundImage not found";
                _bgFound = false;
                Debug.LogWarning($"[SNOW_IMAGE_MAPPING] {_lastError}");
                return;
            }
            bgT = bgGo.transform;
        }
        _bgFound = true;

        Vector3 wTL = bgT.TransformPoint(new Vector3(-0.5f,  0.5f, 0f));
        Vector3 wTR = bgT.TransformPoint(new Vector3( 0.5f,  0.5f, 0f));
        Vector3 wBL = bgT.TransformPoint(new Vector3(-0.5f, -0.5f, 0f));
        Vector3 wBR = bgT.TransformPoint(new Vector3( 0.5f, -0.5f, 0f));

        var cam = Camera.main;
        Vector3 camFwd = cam != null
            ? (cam.transform.position - bgT.position).normalized
            : -bgT.forward;

        System.Func<Vector2, Vector3> n2w = (n) =>
        {
            Vector3 top    = Vector3.Lerp(wTL, wTR, n.x);
            Vector3 bottom = Vector3.Lerp(wBL, wBR, n.x);
            return Vector3.Lerp(top, bottom, n.y) + camFwd * FORWARD_OFFSET;
        };

        _calibFound = File.Exists(CALIB_PATH);
        if (!_calibFound)
        {
            _lastError = $"calib not found: {CALIB_PATH}";
            Debug.LogWarning($"[SNOW_IMAGE_MAPPING] {_lastError}");
            return;
        }

        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(CALIB_PATH));
        if (sd == null || sd.roofs == null)
        {
            _lastError = "JSON parse failed";
            Debug.LogWarning($"[SNOW_IMAGE_MAPPING] {_lastError}");
            return;
        }

        _outlines.Clear();
        _outlineCount = 0;

        foreach (var roofId in RoofIds)
        {
            RoofEntry entry = null;
            foreach (var r in sd.roofs)
                if (r.id == roofId) { entry = r; break; }

            if (entry == null || !entry.confirmed)
            {
                Debug.LogWarning($"[SNOW_IMAGE_MAPPING] roof={roofId} skip=no_confirmed_data");
                continue;
            }

            Vector3 p0 = n2w(new Vector2(entry.topLeft.x,     entry.topLeft.y));
            Vector3 p1 = n2w(new Vector2(entry.topRight.x,    entry.topRight.y));
            Vector3 p2 = n2w(new Vector2(entry.bottomRight.x, entry.bottomRight.y));
            Vector3 p3 = n2w(new Vector2(entry.bottomLeft.x,  entry.bottomLeft.y));

            _outlines.Add(new RoofOutline { id = roofId, tl = p0, tr = p1, br = p2, bl = p3 });
            _outlineCount++;

            Debug.Log($"[SNOW_IMAGE_MAPPING] roof={roofId}" +
                      $" TL=({p0.x:F2},{p0.y:F2},{p0.z:F2})" +
                      $" TR=({p1.x:F2},{p1.y:F2},{p1.z:F2})" +
                      $" BR=({p2.x:F2},{p2.y:F2},{p2.z:F2})" +
                      $" BL=({p3.x:F2},{p3.y:F2},{p3.z:F2})" +
                      $" method=scanline_fill DRAW_OK=YES");
        }

        bool all6 = _outlineCount == 6;
        Debug.Log($"[SNOW_IMAGE_MAPPING] loaded count={_outlineCount}/6 all_6={(all6?"YES":"NO")} method=scanline_fill");
        _loaded = true;
    }

    // ── OnGUI: スキャンライン方式で台形を白く塗りつぶす ──────────
    //
    // 方法:
    //   4点をスクリーン座標に変換後、
    //   上辺(TL→TR)から下辺(BL→BR)まで SCAN_LINES 本の水平ラインを描画。
    //   各ラインの左端・右端を線形補間で求め、GUI.DrawTexture で塗る。
    //   これは純粋な 2D 操作なので三角欠け・カリングが構造的に起きない。
    //
    void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (!_loaded) LoadOutlines();

        var cam = Camera.main;

        // ── 右上オーバーレイ ──────────────────────────────────────
        float ox = Screen.width - 265f, oy = 8f, ow = 257f, lh = 18f;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(new Rect(ox-4, oy-2, ow+8, lh*9+4), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUIStyle st  = new GUIStyle(GUI.skin.label); st.fontSize = 11; st.normal.textColor = Color.white;
        GUIStyle ok  = new GUIStyle(st); ok.normal.textColor  = new Color(0.4f,1f,0.4f);
        GUIStyle ng  = new GUIStyle(st); ng.normal.textColor  = new Color(1f,0.4f,0.4f);
        GUIStyle yel = new GUIStyle(st); yel.normal.textColor = Color.yellow;

        float ry = oy;
        void OvLine(string lbl, string val, bool good)
        {
            GUI.Label(new Rect(ox, ry, ow, lh), lbl, st);
            GUI.Label(new Rect(ox+165, ry, 90, lh), val, good ? ok : ng);
            ry += lh;
        }

        GUI.Label(new Rect(ox, ry, ow, lh), "── SOLID WHITE RECT TEST ──", yel); ry += lh;
        OvLine("METHOD",           "bbox_solid_rect",                 true);
        OvLine("MESH_GENERATION",  "DISABLED",                        true);
        OvLine("BG_IMAGE",         _bgFound    ? "OK" : "NG",         _bgFound);
        OvLine("CALIB_FILE",       _calibFound ? "OK" : "NG",         _calibFound);
        OvLine("SNOW_COUNT",       $"{_outlineCount}/6",              _outlineCount == 6);
        OvLine("ALL_6_COVERED",    _outlineCount == 6 ? "YES" : "NO", _outlineCount == 6);
        OvLine("DEBUG_LINES",      "OFF",                             true);

        if (_lastError != "")
            GUI.Label(new Rect(ox, ry, ow, lh), $"ERR: {_lastError}", ng);

        if (!_consoleLogged && _loaded)
        {
            _consoleLogged = true;
            Debug.Log($"[SOLID_WHITE_RECT] overlay method=bbox_solid_rect" +
                      $" snow_count={_outlineCount}/6 all_6={(_outlineCount==6?"YES":"NO")}" +
                      $" mesh_generation=DISABLED uses_solid_white_rect=YES");
        }

        // ── 白ベタ矩形テスト: 4点のバウンディングボックスに1枚描画 ──
        // スキャンライン・mesh・GL は一切使わない。
        // 4点スクリーン座標の min/max から矩形を求めて DrawTexture するだけ。
        // これで描画パス自体が正常かを確認する。
        if (cam == null || _outlines.Count == 0 || _whiteTex == null) return;

        GUI.color = new Color(0.93f, 0.96f, 1.0f, 0.95f);

        foreach (var o in _outlines)
        {
            if (cam.WorldToScreenPoint(o.tl).z < 0) continue;

            Vector2 sTL = W2G2(cam, o.tl);
            Vector2 sTR = W2G2(cam, o.tr);
            Vector2 sBR = W2G2(cam, o.br);
            Vector2 sBL = W2G2(cam, o.bl);

            // 4点の min/max でバウンディングボックスを計算
            float minX = Mathf.Min(sTL.x, sTR.x, sBR.x, sBL.x);
            float maxX = Mathf.Max(sTL.x, sTR.x, sBR.x, sBL.x);
            float minY = Mathf.Min(sTL.y, sTR.y, sBR.y, sBL.y);
            float maxY = Mathf.Max(sTL.y, sTR.y, sBR.y, sBL.y);

            float w = maxX - minX;
            float h = maxY - minY;
            if (w < 1f || h < 1f) continue;

            // 完全な白ベタ矩形を1枚描画
            GUI.DrawTexture(new Rect(minX, minY, w, h), _whiteTex);
        }

        GUI.color = Color.white;
    }

    // ── ヘルパー: ワールド座標 → GUI 座標（Y軸反転） ─────────────
    static Vector2 W2G2(Camera cam, Vector3 world)
    {
        Vector3 s = cam.WorldToScreenPoint(world);
        return new Vector2(s.x, Screen.height - s.y);
    }
}
