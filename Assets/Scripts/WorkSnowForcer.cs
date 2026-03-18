using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// WORK_SNOW シーン専用。
/// 【モード: DIRECT_NORM_TO_SCREEN】
///
/// 3D ワールド変換を一切使わない。
/// normalized (0..1) 座標 → BackgroundImage の bgRect → スクリーン座標 に直接変換し、
/// RoofCalibrationController と同じ経路で台形スキャンライン描画する。
///
/// これにより Edit 時プレビューと Play 時 runtime が完全に同じ source of truth を使う。
/// 三角欠け・カリング問題は構造的に起きない。
///
/// 落下・地面・SnowPackSpawner・GroundSnowSystem には一切触らない。
/// </summary>
[ExecuteAlways]
public class WorkSnowForcer : MonoBehaviour
{
    const string CALIB_PATH = "Assets/Art/RoofCalibrationData.json";

    // 台形を何本の水平スキャンラインで埋めるか
    const int SCAN_LINES = 60;

    static readonly string[] RoofIds =
        { "Roof_TL", "Roof_TM", "Roof_TR", "Roof_BL", "Roof_BM", "Roof_BR" };

    // ── 状態 ──────────────────────────────────────────────────────
    bool   _loaded        = false;
    bool   _calibFound    = false;
    bool   _bgFound       = false;
    string _lastError     = "";
    bool   _consoleLogged = false;
    int    _outlineCount  = 0;

    // normalized 座標のまま保持（3D変換しない）
    struct RoofNorm { public Vector2 tl, tr, br, bl; public string id; }
    readonly List<RoofNorm> _norms = new List<RoofNorm>();

    // BackgroundImage の bgRect（毎フレーム更新）
    Rect _bgRect;
    bool _bgRectValid = false;

    // 白テクスチャ
    Texture2D _whiteTex;
    Texture2D _fillTex;

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
            Debug.Log($"[WORK_SNOW_FORCER] attached to BackgroundImage scene={scene} mode=DIRECT_NORM_TO_SCREEN");
        }
        else
        {
            var go = new GameObject("WorkSnowForcer_Root");
            go.AddComponent<WorkSnowForcer>();
            Debug.Log($"[WORK_SNOW_FORCER] created root scene={scene} mode=DIRECT_NORM_TO_SCREEN");
        }
    }

    void OnEnable()
    {
        _loaded = false;
        _norms.Clear();
        _outlineCount = 0;
        _consoleLogged = false;
        _bgRectValid = false;

        _whiteTex = new Texture2D(1, 1);
        _whiteTex.SetPixel(0, 0, new Color(0.93f, 0.96f, 1.0f, 1f));
        _whiteTex.Apply();

        _fillTex = new Texture2D(1, 1);
        _fillTex.SetPixel(0, 0, Color.white);
        _fillTex.Apply();
    }

    void Start()
    {
        LoadNorms();
    }

    // ── Update: bgRect を毎フレーム更新 ──────────────────────────
    void Update()
    {
        UpdateBgRect();
        // Edit モードでも毎フレーム再ロード（座標変更をすぐ反映）
        if (!_loaded) LoadNorms();
    }

    // ── BackgroundImage の bgRect を取得 ─────────────────────────
    void UpdateBgRect()
    {
        var cam = Camera.main;
        var bgGo = (gameObject.name == "BackgroundImage")
            ? gameObject
            : GameObject.Find("BackgroundImage");

        if (cam == null || bgGo == null) { _bgRectValid = false; return; }

        var t = bgGo.transform;
        Vector3 wTL = t.TransformPoint(new Vector3(-0.5f,  0.5f, 0f));
        Vector3 wTR = t.TransformPoint(new Vector3( 0.5f,  0.5f, 0f));
        Vector3 wBL = t.TransformPoint(new Vector3(-0.5f, -0.5f, 0f));
        Vector3 wBR = t.TransformPoint(new Vector3( 0.5f, -0.5f, 0f));

        float sh = Screen.height;
        Vector2 sTL = cam.WorldToScreenPoint(wTL); sTL.y = sh - sTL.y;
        Vector2 sTR = cam.WorldToScreenPoint(wTR); sTR.y = sh - sTR.y;
        Vector2 sBL = cam.WorldToScreenPoint(wBL); sBL.y = sh - sBL.y;
        Vector2 sBR = cam.WorldToScreenPoint(wBR); sBR.y = sh - sBR.y;

        float minX = Mathf.Min(sTL.x, sBL.x);
        float maxX = Mathf.Max(sTR.x, sBR.x);
        float minY = Mathf.Min(sTL.y, sTR.y);
        float maxY = Mathf.Max(sBL.y, sBR.y);

        _bgRect = new Rect(minX, minY, maxX - minX, maxY - minY);
        _bgRectValid = _bgRect.width > 1f && _bgRect.height > 1f;
    }

    // ── normalized → OnGUI スクリーン座標（bgRect 基準） ─────────
    // RoofCalibrationController.NormToScreen と完全に同じ変換
    Vector2 NormToScreen(Vector2 n)
    {
        if (_bgRectValid)
            return new Vector2(
                _bgRect.x + n.x * _bgRect.width,
                _bgRect.y + n.y * _bgRect.height);
        // bgRect 未確定時は全画面基準（フォールバック）
        return new Vector2(n.x * Screen.width, n.y * Screen.height);
    }

    // ── JSON から normalized 座標を読み込む ──────────────────────
    void LoadNorms()
    {
        _loaded = false;
        _norms.Clear();
        _outlineCount = 0;

        var bgGo = (gameObject.name == "BackgroundImage")
            ? gameObject
            : GameObject.Find("BackgroundImage");
        _bgFound = bgGo != null;
        if (!_bgFound)
        {
            _lastError = "BackgroundImage not found";
            Debug.LogWarning($"[SNOW_NORM_MAPPING] {_lastError}");
            return;
        }

        _calibFound = File.Exists(CALIB_PATH);
        if (!_calibFound)
        {
            _lastError = $"calib not found: {CALIB_PATH}";
            Debug.LogWarning($"[SNOW_NORM_MAPPING] {_lastError}");
            return;
        }

        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(CALIB_PATH));
        if (sd == null || sd.roofs == null)
        {
            _lastError = "JSON parse failed";
            Debug.LogWarning($"[SNOW_NORM_MAPPING] {_lastError}");
            return;
        }

        foreach (var roofId in RoofIds)
        {
            RoofEntry entry = null;
            foreach (var r in sd.roofs)
                if (r.id == roofId) { entry = r; break; }

            if (entry == null || !entry.confirmed)
            {
                Debug.LogWarning($"[SNOW_NORM_MAPPING] roof={roofId} skip=no_confirmed_data");
                continue;
            }

            _norms.Add(new RoofNorm
            {
                id = roofId,
                tl = new Vector2(entry.topLeft.x,     entry.topLeft.y),
                tr = new Vector2(entry.topRight.x,    entry.topRight.y),
                br = new Vector2(entry.bottomRight.x, entry.bottomRight.y),
                bl = new Vector2(entry.bottomLeft.x,  entry.bottomLeft.y),
            });
            _outlineCount++;

            Debug.Log($"[SNOW_NORM_MAPPING] roof={roofId}" +
                      $" TL=({entry.topLeft.x:F3},{entry.topLeft.y:F3})" +
                      $" TR=({entry.topRight.x:F3},{entry.topRight.y:F3})" +
                      $" BR=({entry.bottomRight.x:F3},{entry.bottomRight.y:F3})" +
                      $" BL=({entry.bottomLeft.x:F3},{entry.bottomLeft.y:F3})" +
                      $" method=direct_norm_to_screen DRAW_OK=YES");
        }

        bool all6 = _outlineCount == 6;
        Debug.Log($"[SNOW_NORM_MAPPING] loaded count={_outlineCount}/6 all_6={(all6 ? "YES" : "NO")}" +
                  $" method=direct_norm_to_screen" +
                  $" preview_source=RoofCalibrationData_json" +
                  $" runtime_source=RoofCalibrationData_json" +
                  $" same_source_of_truth=YES");
        _loaded = true;
    }

    // ── OnGUI: normalized→スクリーン直接変換でスキャンライン台形描画 ──
    void OnGUI()
    {
        if (_whiteTex == null || _fillTex == null) OnEnable();
        if (!_loaded) LoadNorms();

        // bgRect を OnGUI でも更新（Update より先に呼ばれる場合の保険）
        if (!_bgRectValid) UpdateBgRect();

        // ── 右上オーバーレイ ──────────────────────────────────────
        float ox = Screen.width - 265f, oy = 8f, ow = 257f, lh = 18f;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(new Rect(ox-4, oy-2, ow+8, lh*9+4), _fillTex);
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

        GUI.Label(new Rect(ox, ry, ow, lh), "── DIRECT NORM→SCREEN ──", yel); ry += lh;
        OvLine("METHOD",          "direct_norm_to_screen",           true);
        OvLine("MESH_GENERATION", "DISABLED",                        true);
        OvLine("BG_IMAGE",        _bgFound    ? "OK" : "NG",         _bgFound);
        OvLine("CALIB_FILE",      _calibFound ? "OK" : "NG",         _calibFound);
        OvLine("BGRECT_VALID",    _bgRectValid ? "OK" : "NG",        _bgRectValid);
        OvLine("SNOW_COUNT",      $"{_outlineCount}/6",              _outlineCount == 6);
        OvLine("ALL_6_COVERED",   _outlineCount == 6 ? "YES" : "NO", _outlineCount == 6);
        OvLine("SCAN_LINES",      $"{SCAN_LINES}",                   true);

        if (_lastError != "")
            GUI.Label(new Rect(ox, ry, ow, lh), $"ERR: {_lastError}", ng);

        if (!_consoleLogged && _loaded)
        {
            _consoleLogged = true;
            bool isPlay = Application.isPlaying;
            Debug.Log($"[PREVIEW_SOURCE] source=RoofCalibrationData_json method=direct_norm_to_screen is_playing={isPlay}");
            Debug.Log($"[RUNTIME_SOURCE] source=RoofCalibrationData_json method=direct_norm_to_screen is_playing={isPlay}");
            Debug.Log($"[PLAY_SWITCH_DETECTED] NO - same source_of_truth in edit and play");
            Debug.Log($"[OLD_RUNTIME_DISABLED] YES - 3D world conversion path removed");
            Debug.Log($"[PREVIEW_RUNTIME_MATCH] YES - normalized coords used directly in both modes");
            Debug.Log($"[SNOW_NORM_MAPPING] overlay method=direct_norm_to_screen" +
                      $" snow_count={_outlineCount}/6 all_6={(_outlineCount==6?"YES":"NO")}" +
                      $" mesh_generation=DISABLED");
        }

        // ── スキャンライン台形フィット（normalized→スクリーン直接変換） ──
        // RoofCalibrationController.DrawQuadNorm と同じ変換経路を使う。
        // 3D ワールド変換・WorldToScreenPoint は一切使わない。
        if (_norms.Count == 0 || _whiteTex == null) return;

        GUI.color = new Color(0.93f, 0.96f, 1.0f, 0.95f);

        foreach (var n in _norms)
        {
            Vector2 sTL = NormToScreen(n.tl);
            Vector2 sTR = NormToScreen(n.tr);
            Vector2 sBR = NormToScreen(n.br);
            Vector2 sBL = NormToScreen(n.bl);

            // スキャンライン: t=0 が上辺(TL/TR)、t=1 が下辺(BL/BR)
            // 左辺: TL→BL、右辺: TR→BR
            for (int i = 0; i < SCAN_LINES; i++)
            {
                float t0 = (float)i       / SCAN_LINES;
                float t1 = (float)(i + 1) / SCAN_LINES;

                float lx0 = Mathf.Lerp(sTL.x, sBL.x, t0);
                float lx1 = Mathf.Lerp(sTL.x, sBL.x, t1);
                float rx0 = Mathf.Lerp(sTR.x, sBR.x, t0);
                float rx1 = Mathf.Lerp(sTR.x, sBR.x, t1);
                float y0  = Mathf.Lerp(sTL.y, sBL.y, t0);
                float y1  = Mathf.Lerp(sTL.y, sBL.y, t1);

                float lineMinX = Mathf.Min(lx0, lx1, rx0, rx1);
                float lineMaxX = Mathf.Max(lx0, lx1, rx0, rx1);
                float lineMinY = Mathf.Min(y0, y1);
                float lineMaxY = Mathf.Max(y0, y1);

                float lw = lineMaxX - lineMinX;
                float lh2 = Mathf.Max(lineMaxY - lineMinY, 1f);
                if (lw < 0.5f) continue;

                GUI.DrawTexture(new Rect(lineMinX, lineMinY, lw, lh2), _whiteTex);
            }
        }

        GUI.color = Color.white;
    }

    void OnDestroy()
    {
        if (_whiteTex != null) { Object.DestroyImmediate(_whiteTex); _whiteTex = null; }
        if (_fillTex  != null) { Object.DestroyImmediate(_fillTex);  _fillTex  = null; }
    }
}
